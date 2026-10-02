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

        protected override void OnExit(ExitEventArgs e)
        {
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
