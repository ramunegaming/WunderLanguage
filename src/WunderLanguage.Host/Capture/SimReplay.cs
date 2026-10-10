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
    // Slice v3: recorded blocks (t/x/y/w/h/c/n/s/d) ride along, so
    // FrameSuspectDiv sees real div and the menu/battle trackers see real
    // geometry; the pack is selected from the filename (FFVI/Zelda, else
    // replay-default) so battle rows route like live. OCR context (cw/tw)
    // restores when banked, else 0 (old corpora predate it).
    // Scope honesty: JA-side dialogue decisions only. Dwell path (needs
    // settle bitmaps) stays live-only; wt is banked but the step counts
    // loops, so feed order = recorded loop order.
    // Observability only (exit 0): the report measures how much of live
    // behavior text+block feed replay explains; gaps drive the next slice.
    // Revert: delete this file + the --simreplay block in App.xaml.cs.
    internal static class SimReplay
    {
        private static int Num(JsonElement e)
        {
            try
            {
                if (e.ValueKind == JsonValueKind.Number) { try { return e.GetInt32(); } catch { return (int)e.GetDouble(); } }
                if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out int v)) return v;
            }
            catch { }
            return 0;
        }
        private static double NumD(JsonElement e)
        {
            try
            {
                if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
                if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), out double v)) return v;
            }
            catch { }
            return 0.0;
        }
        private static List<OcrBlock> ParseBlocks(JsonElement root)
        {
            var out_ = new List<OcrBlock>();
            try
            {
                if (!root.TryGetProperty("blocks", out var bs) || bs.ValueKind != JsonValueKind.Array) return out_;
                foreach (var b in bs.EnumerateArray())
                {
                    try
                    {
                        string t = b.TryGetProperty("t", out var te) ? (te.GetString() ?? "") : "";
                        int x = b.TryGetProperty("x", out var xe) ? Num(xe) : 0;
                        int y = b.TryGetProperty("y", out var ye) ? Num(ye) : 0;
                        int w = b.TryGetProperty("w", out var we) ? Num(we) : 0;
                        int h = b.TryGetProperty("h", out var he) ? Num(he) : 0;
                        double c = b.TryGetProperty("c", out var ce) ? NumD(ce) : 1.0;
                        int n = b.TryGetProperty("n", out var ne) ? Num(ne) : 1;
                        string s = b.TryGetProperty("s", out var se) ? (se.GetString() ?? "detector") : "detector";
                        double d = b.TryGetProperty("d", out var de) ? NumD(de) : 0.0;
                        out_.Add(new OcrBlock(t, x, y, w, h, (double)w * h, c, n, s, d));
                    }
                    catch { }
                }
            }
            catch { }
            return out_;
        }
        public static int Run(string path)
        {
            try
            {
                var fed = new List<(int loop, string raw, int cw, int tw, int fw, int fh, List<OcrBlock> blocks, string ja)>();
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
                            string raw = root.TryGetProperty("raw", out var r2) ? (r2.GetString() ?? "") : "";
                            int loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : fed.Count;
                            int cw = root.TryGetProperty("cw", out var cwe) ? Num(cwe) : 0;
                            int tw = root.TryGetProperty("tw", out var twe) ? Num(twe) : 0;
                            int fw = root.TryGetProperty("fw", out var fwe) ? Num(fwe) : 0;
                            int fh = root.TryGetProperty("fh", out var fhe) ? Num(fhe) : 0;
                            if (root.TryGetProperty("wt", out var w) && w.ValueKind == JsonValueKind.Number) { wtTotal++; if (w.GetInt64() > 0) wtCovered++; }
                            fed.Add((loop, raw, cw, tw, fw, fh, ParseBlocks(root), ex));
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
                // Slice v3 pack select (filename-driven, same Resolve the
                // session hook uses; restored after — process exits anyway).
                // FFVI corpora then route battle rows to the battle tracker
                // exactly like live instead of leaking into dialogue.
                var prevPack = Packs.PackRegistry.Current;
                string packUsed = prevPack.GameId;
                try
                {
                    string u = (path ?? "").ToUpperInvariant();
                    if (u.Contains("FFVI") || u.Contains("FANTASY_VI")) { Packs.PackRegistry.SetCurrent(Packs.FFVI.FfviPackInstance.Shared); packUsed = "ffvi"; }
                    else if (u.Contains("ZELDA") || u.Contains("DENSETSU")) { Packs.PackRegistry.SetCurrent(Packs.Zelda.ZeldaPackInstance.Shared); packUsed = "zelda"; }
                }
                catch { }
                var token = new System.Threading.CancellationToken(false);
                long blockLines = 0;
                // Slice v4 frame dims: banked per-line fw/fh ONLY (future
                // corpora). The extents estimator was tried and convicted:
                // block coords live in detector space (~1522px wide vs a
                // ~SNES-size bitmap), so max(x+w) is ~2x the true crop and
                // every /fw /fh ratio halves — overlap REGRESSED 82→74 and
                // 97→95. Banked-or-unset; unset (0) keeps the v3 behavior
                // that measured best on old corpora. Live refreshes per loop
                // off a constant crop, so one value stays faithful.
                int sesW = 0, sesH = 0;
                string dimSrc = "none";
                foreach (var f in fed)
                {
                    if (f.fw > sesW) sesW = f.fw;
                    if (f.fh > sesH) sesH = f.fh;
                }
                if (sesW > 0 && sesH > 0) dimSrc = "banked";
                else { sesW = 0; sesH = 0; }
                try { ctl.SetFrameDimsForSim(sesW, sesH); } catch { }
                foreach (var (loop, raw, cw, tw, fw, fh, blocks, ja) in fed)
                {
                    try
                    {
                        if (blocks.Count > 0) blockLines++;
                        ctl.SetOcrContextForSim(raw, cw, tw);
                        ctl.ProcessOcrObservationAsync(loop, ja, blocks, token).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) { FileLogger.Log($"[simreplay] loop {loop} ERROR: {ex.Message}"); break; }
                }
                try { Packs.PackRegistry.SetCurrent(prevPack); } catch { }
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
                FileLogger.Log($"[simreplay] done fed={fed.Count} wt={wtCovered}/{wtTotal} blocks={blockLines}/{fed.Count} pack={packUsed} dims={sesW}x{sesH}({dimSrc}) simFires={simFires} simUpdates={simUpdates} recordedFires={recordedFires.Count} recordedUpdates={recordedUpdates.Count} fireOverlap={overlap}");
                for (int i = 0; i < Math.Min(5, simFireJa.Count); i++)
                    FileLogger.Log($"[simreplay] simfire[{i}]=\"{simFireJa[i]}\"");
                // Gap analysis (slice v2 polish): name the misses (recorded
                // but never sim-fired) and extras (sim-fired but never
                // recorded), bounded. Causes scope slice v3.
                try
                {
                    var sim = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var j in simFireJa) sim[j] = sim.TryGetValue(j, out int c) ? c + 1 : 1;
                    int shown = 0;
                    foreach (var j in recordedFires)
                    {
                        if (sim.TryGetValue(j, out int c) && c > 0) { sim[j] = c - 1; continue; }
                        if (shown < 8) FileLogger.Log($"[simreplay] missed=\"{j}\"");
                        shown++;
                    }
                    FileLogger.Log($"[simreplay] missedTotal={shown}");
                    var rec2 = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var j in recordedFires) rec2[j] = rec2.TryGetValue(j, out int c) ? c + 1 : 1;
                    shown = 0;
                    foreach (var j in simFireJa)
                    {
                        if (rec2.TryGetValue(j, out int c) && c > 0) { rec2[j] = c - 1; continue; }
                        if (shown < 8) FileLogger.Log($"[simreplay] extra=\"{j}\"");
                        shown++;
                    }
                    FileLogger.Log($"[simreplay] extraTotal={shown}");
                }
                catch { }
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
