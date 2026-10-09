using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WunderLanguage.Host.Ocr;

namespace WunderLanguage.Host.Capture
{
    // M7c slice v2 feed: (loop, extracted) from a recorded corpus through
    // the REAL ProcessOcrObservationAsync, collecting _simPublished. No
    // emulator, no sidecar, no CAT (translation stubbed by _simMode).
    // Scope honesty: JA-side dialogue decisions only. Dwell path (needs
    // settle bitmaps) and TITLE/menu path stay live-only; empty blocks mean
    // FrameSuspectDiv=0 (suspect-hold never engages) and density gate sees
    // _lastOcrTotal=0 (never suppresses); pack is the replay default
    // (vocab paths dead — same caveat as M7b). Clock fudge: corpus wt is
    // banked but the step counts loops, so feed order = recorded loop order.
    // Observability only (exit 0): the report measures how much of live
    // behavior text-feed replay explains; gaps drive the next slice.
    // Revert: delete this file + the --simreplay block in App.xaml.cs.
    internal static class SimReplay
    {
        public static int Run(string path)
        {
            try
            {
                var fed = new List<(int loop, string ja)>();
                var recordedFires = new List<string>();
                var recordedUpdates = new List<string>();
                long wtCovered = 0, wtTotal = 0;
                foreach (var line in ReadCorpusLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(line); }
                    catch { continue; }
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("type", out var ty)) continue;
                        string t = ty.GetString() ?? "";
                        if (t == "ocr")
                        {
                            string ex = root.TryGetProperty("extracted", out var e) ? (e.GetString() ?? "") : "";
                            int loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : fed.Count;
                            if (root.TryGetProperty("wt", out var w) && w.ValueKind == JsonValueKind.Number) { wtTotal++; if (w.GetInt64() > 0) wtCovered++; }
                            fed.Add((loop, ex));
                        }
                        else if (t == "bubble")
                        {
                            string kind = root.TryGetProperty("kind", out var k) ? (k.GetString() ?? "") : "";
                            string ja = root.TryGetProperty("ja", out var j) ? (j.GetString() ?? "") : "";
                            if (kind.StartsWith("dialogue-decision:", StringComparison.Ordinal)) recordedFires.Add(ja);
                            else if (kind == "dialogue-update") recordedUpdates.Add(ja);
                        }
                    }
                }
                var ctl = new CaptureController { _simMode = true };
                var blocks = new List<OcrBlock>();
                var token = new System.Threading.CancellationToken(false);
                foreach (var (loop, ja) in fed)
                {
                    try { ctl.ProcessOcrObservationAsync(loop, ja, blocks, token).GetAwaiter().GetResult(); }
                    catch (Exception ex) { FileLogger.Log($"[simreplay] loop {loop} ERROR: {ex.Message}"); break; }
                }
                int simFires = 0, simUpdates = 0;
                var simFireJa = new List<string>();
                foreach (var s in ctl._simPublished)
                {
                    if (s.StartsWith("fire:", StringComparison.Ordinal)) { simFires++; simFireJa.Add(s.Substring(5)); }
                    else simUpdates++;
                }
                // Multiset overlap (order-insensitive): how many sim decisions
                // reproduce a recorded decision line.
                var rec = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var j in recordedFires) rec[j] = rec.TryGetValue(j, out int c) ? c + 1 : 1;
                int overlap = 0;
                foreach (var j in simFireJa) if (rec.TryGetValue(j, out int c) && c > 0) { rec[j] = c - 1; overlap++; }
                FileLogger.Log($"[simreplay] done fed={fed.Count} wt={wtCovered}/{wtTotal} simFires={simFires} simUpdates={simUpdates} recordedFires={recordedFires.Count} recordedUpdates={recordedUpdates.Count} fireOverlap={overlap}");
                for (int i = 0; i < Math.Min(5, simFireJa.Count); i++)
                    FileLogger.Log($"[simreplay] simfire[{i}]=\"{simFireJa[i]}\"");
                return 0;
            }
            catch (Exception ex) { try { FileLogger.Log($"[simreplay] ERROR: {ex.Message}"); } catch { } return 9999; }
        }
        private static IEnumerable<string> ReadCorpusLines(string path)
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(path);
                using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                using var sr = new StreamReader(gz);
                string? line;
                while ((line = sr.ReadLine()) != null)
                    yield return line;
            }
            else
            {
                foreach (var line in File.ReadLines(path))
                    yield return line;
            }
        }
    }
}
