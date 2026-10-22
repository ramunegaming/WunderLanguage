using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace WunderLanguage.Host
{
    // G1 single-instance guard (revert: revert g1-single-instance):
    // round-3 forensics (2026-10-22, app2.log) proved two host instances
    // ran SIMULTANEOUSLY - duplicate [env] startup lines 82ms apart, a
    // "log-cost probe failed: file being used by another process" line,
    // and a second instance's boot lines interleaved with the first
    // instance's live OCR loops. Two hosts = two sidecars fighting
    // :8000/:8001, two capture loops on one emulator, split GPU - that
    // failure set IS round-3's "6x OCR collapse + sidecar-refused
    // outage". This guard kills the class at the door.
    // Named mutex acquired at MODULE INIT - before App.Main and with
    // zero edits to the parked W1b files. The OS releases a named mutex
    // on any process death (kill/crash included), so no stale lock.
    // --replay/--simcheck/--simreplay runs are exempt (offline QA may
    // run beside a live session; they never touch ports, GPU, or the
    // emulator window).
    internal static class SingleInstanceGuard
    {
        // Static field so the owned mutex is never GC-finalized while the
        // app lives (finalization would release the handle = guard lost).
        private static Mutex? _mutex;

        [ModuleInitializer]
        internal static void Init()
        {
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                    if (string.Equals(a, "--replay", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a, "--simcheck", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a, "--simreplay", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a, "--catstats", StringComparison.OrdinalIgnoreCase))
                        return;
                _mutex = new Mutex(true, @"Local\WunderLanguage_SingleInstance", out bool createdNew);
                if (!createdNew)
                {
                    try { FileLogger.Log("[single-instance] second launch detected - exiting (revert: revert g1-single-instance)"); } catch { }
                    try
                    {
                        System.Windows.MessageBox.Show(
                            "WunderLanguage is already running.\n\n" +
                            "A second copy would fight the first for the emulator window, " +
                            "the sidecar ports and the GPU.",
                            "WunderLanguage", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                    catch { }
                    Environment.Exit(1);
                }
            }
            catch { }
        }
    }
}
