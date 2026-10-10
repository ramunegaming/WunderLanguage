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
        internal static string? ForcePackArg = null;
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
                // Bottleneck 8: optional "universal" 2nd arg forces the
                // unknown-game pack, measuring the cold-start price (every
                // vocab-gated path dead) on the same corpus.
                var prevPack = Packs.PackRegistry.Current;
                string packUsed = prevPack.GameId;
                bool forceUniversal = (ForcePackArg ?? "").Equals("universal", StringComparison.OrdinalIgnoreCase);
                if (forceUniversal) { try { Packs.PackRegistry.SetCurrent(Packs.UniversalPackInstance.Shared); packUsed = "universal"; } catch { } }
                else
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
                        ctl._simLoop = loop;
                        ctl.ProcessOcrObservationAsync(loop, ja, blocks, token).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) { FileLogger.Log($"[simreplay] loop {loop} ERROR: {ex.Message}"); break; }
                }
                try { Packs.PackRegistry.SetCurrent(prevPack); } catch { }
                int simFires = 0, simUpdates = 0;
                var simFireJa = new List<string>();
                // B9 panel latency (stream-latency paper: AL forgives, DAL
                // never does; interactive-latency paper: distributions not
                // means; SRE: percentiles + budget assertion). Entries are
                // kind:loop:decision:ja (ja may contain colons — 4-part max).
                var simFireLoops = new List<int>();
                var simFireKinds = new List<string>();
                foreach (var s in ctl._simPublished)
                {
                    string[] p = s.Split(new char[] { ':' }, 4);
                    if (p.Length < 4) continue;
                    if (!int.TryParse(p[1], out int lp)) lp = -1;
                    if (p[0] == "fire") { simFires++; simFireJa.Add(p[3]); simFireLoops.Add(lp); simFireKinds.Add(p[2]); }
                    else simUpdates++;
                }
                // Sim lag: fire loop minus the start of the most recent
                // CONSECUTIVE run of that JA at/under the fire loop (the
                // run that actually caused the fire). Naive global
                // first-seen inflates on repeated lines (same JA two scenes
                // apart) — convicted 2026-10-10 (max=704 artifact).
                // Sim erasure (NE analogue): updates per sim fire.
                var lagSeq = new List<int>();
                var kindLag = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                for (int i = 0; i < simFireJa.Count; i++)
                {
                    int fireLoop = i < simFireLoops.Count ? simFireLoops[i] : -1;
                    // Last fed index at/under the fire loop showing this JA…
                    int runEnd = -1;
                    for (int j = fed.Count - 1; j >= 0; j--)
                        if (fed[j].loop <= fireLoop && fed[j].ja == simFireJa[i]) { runEnd = j; break; }
                    if (runEnd < 0) continue;
                    // …extended backward while consecutive loops show it.
                    int runStart = runEnd;
                    while (runStart - 1 >= 0 && fed[runStart - 1].ja == simFireJa[i]
                        && fed[runStart - 1].loop == fed[runStart].loop - 1) runStart--;
                    {
                        int lag = fireLoop - fed[runStart].loop;
                        if (lag < 0) continue;
                        lagSeq.Add(lag);
                        string k = i < simFireKinds.Count ? simFireKinds[i] : "?";
                        if (!kindLag.TryGetValue(k, out var kl)) { kl = new List<int>(); kindLag[k] = kl; }
                        kl.Add(lag);
                    }
                }
                // DAL-like catch-up debt (never forgives) runs on FIRE
                // ORDER: accumulated growth of consecutive lags. Rises when
                // the game outruns us (mash/autoscroll); flat on recovery.
                long dalDebt = 0;
                for (int i = 1; i < lagSeq.Count; i++)
                    if (lagSeq[i] > lagSeq[i - 1]) dalDebt += lagSeq[i] - lagSeq[i - 1];
                var lags = new List<int>(lagSeq);
                lags.Sort();
                double simLag = lags.Count > 0 ? lags.Sum(l => (long)l) / (double)lags.Count : -1;
                int lagP50 = lags.Count > 0 ? lags[lags.Count / 2] : -1;
                int lagP95 = lags.Count > 0 ? lags[Math.Min(lags.Count - 1, (int)(lags.Count * 0.95))] : -1;
                int lagMax = lags.Count > 0 ? lags[lags.Count - 1] : -1;
                var kindMean = new System.Text.StringBuilder();
                foreach (var kv in kindLag)
                {
                    kv.Value.Sort();
                    kindMean.Append(kv.Key + "=" + kv.Value.Count + "x@" + (kv.Value.Sum(l => (long)l) / Math.Max(1, kv.Value.Count)) + " ");
                }
                double simNE = simFires > 0 ? (double)simUpdates / simFires : -1;
                // Live erasure on the recorded corpus (updates per decision
                // fire). Live lag needs loop-stamped bubbles (banked going
                // forward; old corpora lack it → reported n/a).
                double liveNE = recordedFires.Count > 0 ? (double)recordedUpdates.Count / recordedFires.Count : -1;
                // Multiset overlap (order-insensitive): how many sim decisions
                // reproduce a recorded decision line.
                var rec = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var j in recordedFires) rec[j] = rec.TryGetValue(j, out int c) ? c + 1 : 1;
                int overlap = 0;
                foreach (var j in simFireJa) if (rec.TryGetValue(j, out int c) && c > 0) { rec[j] = c - 1; overlap++; }
                FileLogger.Log($"[simreplay] done fed={fed.Count} wt={wtCovered}/{wtTotal} blocks={blockLines}/{fed.Count} pack={packUsed} dims={sesW}x{sesH}({dimSrc}) simFires={simFires} simUpdates={simUpdates} recordedFires={recordedFires.Count} recordedUpdates={recordedUpdates.Count} fireOverlap={overlap}");
                FileLogger.Log($"[b6metrics] simNE={simNE:0.00} (updates/fire) simLag={simLag:0.0}loops liveNE={liveNE:0.00} liveLag=n/a(old corpora lack bubble loop stamps)");
                // B9 panel-latency budget (human-anchored: nominal loop
                // ~0.5s; 2–4s irritating → p50 in single digits, weak-hold
                // path 12–18 by explicit design so p95 accommodates ~30;
                // NE at paper-stabilized 0.1 scale with headroom). Budgets
                // are regression tripwires with headroom (5–10x observed
                // 2026-10-10: p50=2, p95≤9, max≤21, NE≤0.39 — tightened
                // from the first generous pass after the run-based lag
                // definition convicted the first-seen artifact), not
                // aspirations. Exit stays 0 (observe, SRE-style).
                bool bOk = lagP50 <= 10 && lagP95 <= 30 && lagMax <= 60 && simNE <= 0.5;
                FileLogger.Log($"[panellat] p50={lagP50} p95={lagP95} max={lagMax} dalDebt={dalDebt} kinds=({kindMean.ToString().Trim()}) BUDGET-{(bOk ? "OK" : "EXCEEDED")}(p50<=10,p95<=30,max<=60,NE<=0.5)");
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
