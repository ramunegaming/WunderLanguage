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
                foreach (var line in File.ReadLines(path))
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
                    }
                }
            }
            catch (Exception ex) { FileLogger.Log($"[replay] ERROR: {ex.Message}"); return 9999; }
            FileLogger.Log($"[replay] done checked={checked_} match={match} drift={drift} mined-or-stale={minedOrStale} holds={holds}");
            return drift;
        }
    }
}