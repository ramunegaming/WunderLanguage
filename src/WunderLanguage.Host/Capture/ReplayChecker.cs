using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace WunderLanguage.Host.Capture
{
    // M7b offline fixup replay (v1). Refeeds recorded corpus ocr lines through the REAL
    // private static fixup/normalize/hold methods via reflection (zero core diff).
    // Scope: fixup determinism + hold-predicate agreement. Full stability-machine replay (M7c)
    // needs an injectable clock + OCR feed (loop refactor) — NOT covered here.
    // Revert: delete this file + the --replay block in App.xaml.cs.
    internal static class ReplayChecker
    {
        public static int Run(string path)
        {
            int checked_ = 0, match = 0, drift = 0, minedOrStale = 0, holds = 0;
            // Trip-equality counters (revert: revert punct-1to1-gate —
            // delete these + Trip() + the classification block + revert
            // the done-line/return): rc now reports scroll-composite
            // detection regressions, chain-drift stays informational.
            int tripNew = 0, tripLostGenuine = 0, tripRelief = 0;
            try
            {
                var t = typeof(CaptureController);
                string Call(string name, string arg)
                {
                    var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null) { FileLogger.Log($"[replay] missing method {name}"); return "<missing>"; }
                    return (string)m.Invoke(null, new object[] { arg })!;
                }
                bool Hold(string arg)
                {
                    var m = t.GetMethod("HasUntrustedLatinRun", BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null) return false;
                    return (bool)m.Invoke(null, new object[] { arg })!;
                }
                bool Trip(string arg)
                {
                    var m = t.GetMethod("IsUnterminatedComposite", BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null) return false;
                    try { return (bool)m.Invoke(null, new object[] { arg })!; } catch { return false; }
                }
                foreach (var line in ReadCorpusLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(line); }
                    catch { continue; }
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("type", out var ty) || ty.GetString() != "ocr") continue;
                        string raw = root.TryGetProperty("raw", out var r) ? (r.GetString() ?? "") : "";
                        string recorded = root.TryGetProperty("extracted", out var e) ? (e.GetString() ?? "") : "";
                        bool mined = root.TryGetProperty("mined", out var md) && md.ValueKind == JsonValueKind.True;
                        int loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : -1;
                        string recomputed;
                        try { recomputed = Call("ApplyDisplayFixups", Call("NormalizePunctForStability", Call("ApplyZeldaOcrFixups", raw))); }
                        catch (Exception ex) { FileLogger.Log($"[replay] loop {loop} ERROR: {ex.Message}"); drift++; continue; }
                        checked_++;
                        if (recomputed == recorded) { match++; }
                        else if (mined) { minedOrStale++; FileLogger.Log($"[replay] loop {loop} mined-shortcircuit (live used mined map, replay can't): raw=\"{raw}\""); }
                        else if (recomputed == raw) { minedOrStale++; FileLogger.Log($"[replay] loop {loop} mined-or-stale (code no-op, recorded fixed): raw=\"{raw}\""); }
                        else { drift++; FileLogger.Log($"[replay] loop {loop} DRIFT: raw=\"{raw}\" recorded=\"{recorded}\" now=\"{recomputed}\""); }
                        try { if (Hold(recomputed)) holds++; } catch { }
                        // Trip-equality classification: recorded was made by
                        // the OLD normalize chain, recomputed by the current
                        // one. A scroll-composite trip must survive chain
                        // rewrites (detection reads the same evidence the
                        // rewrite used to canonicalize): a NEW trip = false
                        // suppression (red); a LOST trip = red unless raw
                        // carries no genuine boundary (… 。 ． .) — that loss
                        // is fabricated-… relief (old rewrite manufactured
                        // scroll evidence from quotes/middots, suppressing
                        // real lines — intended to stop).
                        try
                        {
                            bool tRec = Trip(recorded), tNow = Trip(recomputed);
                            if (tRec != tNow)
                            {
                                bool rawGenuine = raw.IndexOf('…') >= 0 || raw.IndexOf('。') >= 0
                                    || raw.IndexOf('．') >= 0 || raw.IndexOf('.') >= 0;
                                if (tNow && !tRec)
                                {
                                    tripNew++;
                                    FileLogger.Log($"[replay] loop {loop} TRIP-NEW (false suppression): raw=\"{raw}\"");
                                }
                                else if (rawGenuine)
                                {
                                    tripLostGenuine++;
                                    FileLogger.Log($"[replay] loop {loop} TRIP-LOST-GENUINE: raw=\"{raw}\" recorded=\"{recorded}\" now=\"{recomputed}\"");
                                }
                                else
                                {
                                    tripRelief++;
                                    if (tripRelief <= 8) FileLogger.Log($"[replay] loop {loop} TRIP-RELIEF (fabricated … dropped, real line may publish): raw=\"{raw}\"");
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { FileLogger.Log($"[replay] ERROR: {ex.Message}"); return 9999; }
            FileLogger.Log($"[replay] done checked={checked_} match={match} drift={drift} mined-or-stale={minedOrStale} holds={holds} trip-new={tripNew} trip-lost-genuine={tripLostGenuine} trip-relief={tripRelief}");
            return tripNew + tripLostGenuine;
        }
        // Transparent reader: .gz (current) and legacy plaintext side by
        // side, so old sessions replay without migration pressure.
        private static System.Collections.Generic.IEnumerable<string> ReadCorpusLines(string path)
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