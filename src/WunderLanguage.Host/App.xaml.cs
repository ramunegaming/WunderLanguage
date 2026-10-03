using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WunderLanguage.Host.Settings;
using WunderLanguage.Host.Sidecar;
using WunderLanguage.Host.Windows;

namespace WunderLanguage.Host
{
    public partial class App : Application
    {
        private readonly SidecarProcessManager _sidecar = new();

        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                FileLogger.Log($"AppDomain unhandled exception: {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                FileLogger.Log($"Unobserved task exception: {e.Exception}");
                e.SetObserved();
            };
            // Belt-and-braces: OnExit covers clean shutdowns; a crash that skips
            // it still gets the sidecar reaped here instead of orphaning python.exe.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { _sidecar.Stop(); } catch { }
            };
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            FileLogger.Log($"DispatcherUnhandledException: {e.Exception}");
            MessageBox.Show(
                $"WunderLanguage hit an unexpected error:\n\n{e.Exception.Message}\n\nFull details logged to:\n{FileLogger.Path_}",
                "WunderLanguage - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Perf baseline, batch 1 (cited Systems Performance: baseline
            // statistics + latency breakdown). Observe-only: a stopwatch and
            // one env line per launch. No behavior change. Revert: delete
            // the sw/LogStartupStage/LogEnvLines calls + the two helpers.
            var sw = Stopwatch.StartNew();
            FileLogger.Log("App starting up");
            LogEnvLine();
            ProbeLogCost();
            FileLogger.Log($"[startup] stage=app-start ms=0");
            // M7b offline replay: --replay <corpus.jsonl> refeeds recorded OCR through the real
            // fixup/normalize/hold path. No emulator, no sidecar, no GPU. Revert: delete this block.
            try
            {
                var args = e?.Args ?? System.Array.Empty<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--replay" && i + 1 < args.Length)
                    {
                        int mism = Capture.ReplayChecker.Run(args[i + 1]);
                        FileLogger.Log($"[replay] exit mismatches={mism}");
                        Shutdown(mism == 0 ? 0 : 1);
                        return;
                    }
                }
            }
            catch (System.Exception ex) { FileLogger.Log($"[replay] ERROR: {ex.Message}"); Shutdown(1); return; }
            // Fresh troubleshoot bundle per launch (never on close, so crashes keep evidence).
            try
            {
                string tsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "troubleshoot");
                if (System.IO.Directory.Exists(tsDir))
                    foreach (var f in System.IO.Directory.GetFiles(tsDir))
                        try { System.IO.File.Delete(f); } catch { }
            }
            catch { }

            var loading = new LoadingWindow();
            loading.Show();
            loading.Activate();
            FileLogger.Log("Loading window shown");
            FileLogger.Log($"[startup] stage=loading-shown ms={sw.ElapsedMilliseconds}");

            try
            {
                var progress = new Progress<string>(status =>
                {
                    FileLogger.Log($"Status: {status}");
                    loading.SetStatus(status);
                });
                await _sidecar.StartAsync(progress);
                FileLogger.Log("Sidecar reported healthy");
                FileLogger.Log($"[startup] stage=sidecar-healthy ms={sw.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Sidecar startup failed: {ex}");
                MessageBox.Show(
                    $"Could not start the translation engine:\n\n{ex.Message}\n\nFull details logged to:\n{FileLogger.Path_}",
                    "WunderLanguage", MessageBoxButton.OK, MessageBoxImage.Error);
                loading.Close();
                Shutdown();
                return;
            }

            loading.Close();
            FileLogger.Log("Loading window closed, showing connect window");

            var settings = AppSettings.Load();
            // Test automation (perf batch: unattended loop-cost smoke test).
            // --connect <title-substring> attaches to the first matching
            // visible window with a whole-window region and skips the
            // calibration prompt. No match (or no flag) = ConnectWindow as
            // before. Revert: delete this block + TryAutoConnect.
            string? autoSub = null;
            var autoArgs = e?.Args ?? System.Array.Empty<string>();
            for (int i = 0; i < autoArgs.Length; i++)
                if (autoArgs[i] == "--connect" && i + 1 < autoArgs.Length) autoSub = autoArgs[i + 1];
            if (!string.IsNullOrWhiteSpace(autoSub) && TryAutoConnect(autoSub, settings, sw))
                return;
            var connectWindow = new ConnectWindow(settings);
            connectWindow.Show();
            connectWindow.Activate();
            FileLogger.Log("Connect window shown");
            FileLogger.Log($"[startup] stage=connect-shown ms={sw.ElapsedMilliseconds}");
        }

        // One env line per launch: every future crash/perf log carries its
        // own machine context (CPU/RAM/GPU/runtime/config). Best-effort —
        // never throws, never blocks startup (nvidia-smi capped at 3s).
        private static void LogEnvLine()
        {
            try
            {
                string config =
#if DEBUG
                    "Debug";
#else
                    "Release";
#endif
                double ramGB = 0;
                try { ramGB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0; } catch { }
                string gpu = "unknown";
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "--query-gpu=name --format=csv,noheader",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                    };
                    using var p = Process.Start(psi);
                    if (p is not null && p.WaitForExit(3000) && p.ExitCode == 0)
                        gpu = (p.StandardOutput.ReadToEnd() ?? "").Trim().Replace('\r', ' ').Replace('\n', ' ');
                    if (string.IsNullOrWhiteSpace(gpu)) gpu = "none";
                }
                catch { gpu = "unknown"; }
                FileLogger.Log($"[env] cpus={Environment.ProcessorCount} ramGB={ramGB:0.0} os={Environment.OSVersion.VersionString} x64={Environment.Is64BitProcess} fx={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} config={config} gpu={gpu}");
            }
            catch { }
        }

        // Observer-effect audit (perf batch): what one FileLogger.Log call
        // costs on THIS machine/disk/AV — same syscalls (open/append/close
        // on a scratch file in the same dir), 200 iterations, one summary
        // line, zero pollution of app.log. Single-thread floor; lock
        // contention under threading only adds. Revert: delete call + method.
        private static void ProbeLogCost()
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage");
                System.IO.Directory.CreateDirectory(dir);
                string probe = System.IO.Path.Combine(dir, "logprobe.tmp");
                string line = "[probe] 0123456789 abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789\n";
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 200; i++)
                    System.IO.File.AppendAllText(probe, line);
                sw.Stop();
                try { System.IO.File.Delete(probe); } catch { }
                FileLogger.Log($"[log-cost] filelog mean={sw.Elapsed.TotalMilliseconds / 200:F3}ms over 200 (single-thread floor)");
            }
            catch (Exception ex) { FileLogger.Log($"[log-cost] probe failed: {ex.Message}"); }
        }

        // Headless attach for smoke tests: same AppShell path as the
        // Connect button, minus the calibration prompt (whole-window
        // region). Never throws into startup: false = normal window.
        private static bool TryAutoConnect(string substr, AppSettings settings, System.Diagnostics.Stopwatch sw)
        {
            try
            {
                var wins = Capture.WindowCapture.ListVisibleWindows();
                var hit = wins.Find(w => (w.Title ?? "").Contains(substr, StringComparison.OrdinalIgnoreCase));
                if (hit is null || hit.Handle == IntPtr.Zero)
                {
                    FileLogger.Log($"[startup] auto-connect no match for \"{substr}\" ({wins.Count} windows)");
                    return false;
                }
                Capture.WindowCapture.RestoreIfMinimized(hit.Handle);
                if (!Capture.WindowCapture.TryGetWindowBounds(hit.Handle, out var bounds))
                {
                    FileLogger.Log("[startup] auto-connect bounds failed");
                    return false;
                }
                var region = new Capture.GameRegion(bounds, bounds);
                var shell = new Windows.AppShell(hit.Handle, region, settings);
                shell.Start();
                FileLogger.Log($"[startup] stage=auto-connect ms={sw.ElapsedMilliseconds} window=\"{hit.Title}\"");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[startup] auto-connect failed: {ex.Message}");
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)        {
            // The sidecar exits with the app: a lingering python.exe causes more
            // confusion than the cold-start seconds it saves (stale-pipeline
            // mismatch, orphaned GPU memory). Crash orphans are still replaced
            // by the version check on next launch.
            FileLogger.Log("App exiting - stopping sidecar");
            try { _sidecar.Stop(); } catch { }
            base.OnExit(e);
        }
    }
}
