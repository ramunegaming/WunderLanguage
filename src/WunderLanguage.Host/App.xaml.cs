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
        // W1 split (2026-10-17): dedicated translator process (clean
        // address space — OCR residency poisons weight reads). Non-fatal:
        // OCR works without it (per-line fallback covers translations).
        // Revert: delete field + start/stop calls.
        private readonly SidecarProcessManager _catSidecar = new("cat");

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
            try { _catSidecar.Stop(); } catch { }
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
            Sidecar.CrashDumps.EnsureLocalDumps();
            FileLogger.Log($"[startup] stage=app-start ms=0");
            // M7b offline replay: --replay <corpus.jsonl> refeeds recorded OCR through the real
            // fixup/normalize/hold path. No emulator, no sidecar, no GPU. Revert: delete this block.
            try
            {
                var args = e?.Args ?? System.Array.Empty<string>();
                // --diagnostics honored pre-settings: replay exits before
                // settings load, and the normal path re-decides with the
                // file value below (CLI stays sticky via || there).
                foreach (var a in args)
                    if (a == "--diagnostics") { Settings.Diag.Enabled = true; FileLogger.Log("[diag] enabled=True (cli, pre-settings)"); break; }
                for (int i = 0; i < args.Length; i++)
                {
                    // Capture-swap offline gate (2026-11-11): --captureswap
                    // proves the blank-frame rescue machinery on this
                    // machine - shared blank classifier both ways, both
                    // capture primitives on a real on-screen window of
                    // ours, production CaptureWindowRegion end-to-end with
                    // size agreement. No emulator, no sidecar, no GPU;
                    // exit 0 only if every assertion holds.
                    // Revert: revert capture-swap (delete this block +
                    // the WindowCapture swap machinery).
                    if (args[i] == "--captureswap")
                    {
                        FileLogger.ReplayMode = true;
                        bool swapOk = Capture.WindowCapture.RunSwapOfflineTest();
                        FileLogger.Log($"[captureswap] exit ok={swapOk}");
                        Shutdown(swapOk ? 0 : 1);
                        return;
                    }
                    // Calibration coverage offline gate (2026-11-11):
                    // --calibcover shows the real overlay, pumps the
                    // dispatcher, and asserts its physical frame covers
                    // the PMv2 raw virtual screen within 2px on every
                    // edge. No emulator, no sidecar, no GPU; exit 0 only
                    // on full coverage. Revert: revert calib-cover (delete
                    // this block + CalibrationWindow.RunCoverageGate +
                    // the coverage correction + the raw-metrics clamp).
                    if (args[i] == "--calibcover")
                    {
                        FileLogger.ReplayMode = true;
                        bool covOk = CalibrationWindow.RunCoverageGate();
                        FileLogger.Log($"[calibcover] exit ok={covOk}");
                        Shutdown(covOk ? 0 : 1);
                        return;
                    }
                    // Feed position-save defer offline gate (2026-11-12):
                    // --feeddefer proves the debounce with the real
                    // DeferredAction: 500 schedules -> 0 fires, then a 1.3s
                    // quiet pump -> exactly 1. No emulator, no sidecar, no
                    // GPU; exit 0 only if both hold. Revert: revert
                    // feed-defer (delete this block + DeferredAction +
                    // the AppShell defer/throttle wiring).
                    if (args[i] == "--feeddefer")
                    {
                        FileLogger.ReplayMode = true;
                        bool deferOk = DeferredAction.RunOfflineTest();
                        FileLogger.Log($"[feeddefer] exit ok={deferOk}");
                        Shutdown(deferOk ? 0 : 1);
                        return;
                    }
                    // Feed resize offline gate (2026-11-12): --feedresize
                    // proves the resize contract with the real panel -
                    // docked NoResize / detached CanResize, 340x220 minimums
                    // with coercion, SizeChanged signal, and the FeedWidth/
                    // FeedHeight payload round-trip through the real
                    // Save()/Load() code. Never writes settings.json.
                    // No emulator, no sidecar, no GPU; exit 0 only if every
                    // assertion holds. Revert: revert feed-resize (delete
                    // this block + SidePanelWindow.RunResizeOfflineTest +
                    // the AppShell/AppSettings wiring).
                    if (args[i] == "--feedresize")
                    {
                        FileLogger.ReplayMode = true;
                        bool resizeOk = SidePanelWindow.RunResizeOfflineTest();
                        FileLogger.Log($"[feedresize] exit ok={resizeOk}");
                        Shutdown(resizeOk ? 0 : 1);
                        return;
                    }
                    // Dock-affordance visibility offline gate (2026-11-12):
                    // --dockvis = pure coverage predicate table + real
                    // monitor readback on a real window (small -> not
                    // covered, stretched to monitor -> covered) + the real
                    // panel's visibility flips. No emulator, no sidecar,
                    // no GPU; exit 0 only if every assertion holds.
                    // Revert: revert dock-hide (delete this block +
                    // SidePanelWindow.RunDockVisOfflineTest + the
                    // WindowCapture detection + the AppShell glue).
                    if (args[i] == "--dockvis")
                    {
                        FileLogger.ReplayMode = true;
                        bool visOk = SidePanelWindow.RunDockVisOfflineTest();
                        FileLogger.Log($"[dockvis] exit ok={visOk}");
                        Shutdown(visOk ? 0 : 1);
                        return;
                    }
                    if (args[i] == "--replay" && i + 1 < args.Length)
                    {
                        FileLogger.ReplayMode = true; // synthetic runs never mirror to app2.log
                        int mism = Capture.ReplayChecker.Run(args[i + 1]);
                        FileLogger.Log($"[replay] exit mismatches={mism}");
                        Shutdown(mism == 0 ? 0 : 1);
                        return;
                    }
                    // M7c simcheck (slice v1): proves the injectable clock seam.
                    // ManualClock at T0 -> cost-flush correctly holds; +61s ->
                    // flush path runs. No emulator, no sidecar, instant.
                    // Revert: delete this block + Capture/SimClock.cs.
                    if (args[i] == "--simcheck")
                    {
                        FileLogger.ReplayMode = true;
                        bool ok = Capture.SimCheck.Run();
                        FileLogger.Log($"[simcheck] exit ok={ok}");
                        Shutdown(ok ? 0 : 1);
                        return;
                    }
                    // M7c simreplay (slice v2): corpus (loop, extracted) via
                    // the real stability step. Revert: delete this block +
                    // Capture/SimReplay.cs.
                    if (args[i] == "--simreplay" && i + 1 < args.Length)
                    {
                        FileLogger.ReplayMode = true;
                        // Bottleneck 8: optional 2nd arg "universal" forces
                        // the unknown-game pack (cold-start price).
                        // Revert with SimReplay.ForcePackArg.
                        if (i + 2 < args.Length) Capture.SimReplay.ForcePackArg = args[i + 2];
                        int rc = Capture.SimReplay.Run(args[i + 1]);
                        FileLogger.Log($"[simreplay] exit rc={rc}");
                        Shutdown(rc == 0 ? 0 : 1);
                        return;
                    }
                    // Error-code chip/popup offline gate (2026-10-23):
                    // --errortest [code] shows a bare side panel with the
                    // chip posted (no code = E1 -> E2 -> E4 in-place
                    // sequence + unknown-code guard; a code = that one,
                    // e.g. E5/E6 to eyeball registry-only wording) then
                    // just sits there - no emulator, no sidecar, no GPU;
                    // the gate screenshots/clicks and taskkills it.
                    // Revert: revert error-codes (delete this block).
                    if (args[i] == "--errortest")
                    {
                        FileLogger.ReplayMode = true;
                        ErrorCodeTestRun(i + 1 < args.Length && args[i + 1].StartsWith("E") ? args[i + 1] : null);
                        return;
                    }
                    // HDR detection offline gate (2026-11-11): --hdrtest
                    // self-checks the DisplayHdr struct sizes, exercises
                    // the EXACT flat-frame classifier the capture loop
                    // runs (solid/edge/noise), and dumps every active
                    // display path (source, monitor name, HDR flags).
                    // No emulator, no sidecar, no GPU; exit 0 only if all
                    // three answered. Revert: revert hdr-detect (delete
                    // this block + Capture/DisplayHdr.cs).
                    if (args[i] == "--hdrtest")
                    {
                        FileLogger.ReplayMode = true;
                        bool hdrOk = Capture.DisplayHdr.RunOfflineTest();
                        FileLogger.Log($"[hdrtest] exit ok={hdrOk}");
                        Shutdown(hdrOk ? 0 : 1);
                        return;
                    }
                }
            }
            catch (System.Exception ex) { FileLogger.Log($"[replay] ERROR: {ex.Message}"); Shutdown(1); return; }
            // Fresh troubleshoot bundle per launch (never on close, so crashes keep evidence).
            // keep-shots.armed sentinel (same pattern as keep-log.armed):
            // present → shots accumulate across launches (newest 200 kept,
            // oldest pruned — bounded storage); absent → last-run-only wipe
            // as before. Revert: delete sentinel check + prune (keep wipe).
            try
            {
                string tsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "troubleshoot");
                string shotsSentinel = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "keep-shots.armed");
                if (System.IO.Directory.Exists(tsDir))
                {
                    if (System.IO.File.Exists(shotsSentinel))
                    {
                        var fis = new System.Collections.Generic.List<System.IO.FileInfo>(new System.IO.DirectoryInfo(tsDir).GetFiles());
                        fis.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                        for (int k = 200; k < fis.Count; k++)
                            try { fis[k].Delete(); } catch { }
                        FileLogger.Log($"[shots] keep-armed, newest {System.Math.Min(200, fis.Count)} files kept");
                    }
                    else
                        foreach (var f in System.IO.Directory.GetFiles(tsDir))
                            try { System.IO.File.Delete(f); } catch { }
                }
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
                StartPressureLog();
                // CAT translator: best-effort. A failed CAT start must not
                // kill the app (OCR + fallback cover function); the CAT
                // manager retries per its own policy and warm-watches.
                try
                {
                    await _catSidecar.StartAsync(null);
                    FileLogger.Log("CAT sidecar start accepted (warming behind)");
                }
                catch (Exception cex)
                {
                    FileLogger.Log($"CAT sidecar start failed (degraded, OCR continues): {cex.Message.Split('\n')[0]}");
                }
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

            var autoArgs = e?.Args ?? System.Array.Empty<string>();
            var settings = AppSettings.Load();
            // Diagnostics gate: settings file and/or --diagnostics flag.
            // Decided here, read everywhere via Settings.Diag.Enabled.
            bool diagCli = false;
            string? autoSub = null;
            for (int i = 0; i < autoArgs.Length; i++)
            {
                if (autoArgs[i] == "--diagnostics") diagCli = true;
                if (autoArgs[i] == "--connect" && i + 1 < autoArgs.Length) autoSub = autoArgs[i + 1];
            }
            Settings.Diag.Enabled = settings.Diagnostics || diagCli;
            FileLogger.Log($"[diag] enabled={Settings.Diag.Enabled} (file={settings.Diagnostics} cli={diagCli})");
            // Test automation (perf batch: unattended loop-cost smoke test).
            // --connect <title-substring> attaches to the first matching
            // visible window with a whole-window region and skips the
            // calibration prompt. No match (or no flag) = ConnectWindow as
            // before. Revert: delete this block + TryAutoConnect.
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

        protected override void OnExit(ExitEventArgs e)
        {
            // The sidecar exits with the app: a lingering python.exe causes more
            // confusion than the cold-start seconds it saves (stale-pipeline
            // mismatch, orphaned GPU memory). Crash orphans are still replaced
            // by the version check on next launch.
            FileLogger.Log("App exiting - stopping sidecar");
            StopPressureLog();
            try { _sidecar.Stop(); } catch { }
            try { _catSidecar.Stop(); } catch { }
            base.OnExit(e);
        }

        // Perf batch (pressure logging): host + sidecar RSS and GPU
        // utilization/memory on a 60s cadence, off the loop thread. First
        // line lands 60s after healthy; numbers feed GPU-tier decisions
        // for release (3060/6GB here — weaker machines unmeasured).
        // Revert: delete field/methods + 2 call sites.
        private System.Threading.Timer? _pressureTimer;
        private void StartPressureLog()
        {
            try
            {
                StopPressureLog();
                _pressureTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        int pid = _sidecar.SidecarPid();
                        double hostMB = 0;
                        try { using var me = Process.GetCurrentProcess(); hostMB = me.PagedMemorySize64 / 1048576.0; } catch { }
                        // System-wide GPU state + our sidecar's share of it
                        // (per-PID VRAM is the release-critical number; host
                        // bytes and system totals alone can't attribute).
                        string gpu = "n/a", cudamem = "n/a";
                        try
                        {
                            string util = RunNvidiaSmi("--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits");
                            var parts = util.Trim().Split(',');
                            if (parts.Length >= 3) gpu = $"u{parts[0].Trim()}% {parts[1].Trim()}/{parts[2].Trim()}MB";
                            string apps = RunNvidiaSmi("--query-compute-apps=pid,used_memory --format=csv,noheader,nounits");
                            foreach (var line in apps.Split('\n'))
                            {
                                var ap = line.Trim().Split(',');
                                if (ap.Length >= 2 && ap[0].Trim() == pid.ToString() && pid != 0)
                                {
                                    string mem = ap[1].Trim().Split(' ')[0];
                                    cudamem = $"{pid}:{mem}MB";
                                    break;
                                }
                            }
                            if (pid == 0) cudamem = "adopted";
                            else if (cudamem == "n/a")
                            {
                                // PID present but no compute context (idle
                                // sidecar holds mappings, no kernels live).
                                cudamem = $"{pid}:idle";
                            }
                        }
                        catch { }
                        // Survival batch 3 (WI1 Ch5): piggyback the 60s
                        // cadence — no new timer. mem rides the pressure
                        // line (no extra log volume); shed fires only on a
                        // real OS Low signal (healthy boxes sit at high).
                        // Revert: delete to the [memory] log line.
                        string memState = "unknown";
                        string shed = "-";
                        try
                        {
                            memState = Sidecar.MemoryPressure.Sample();
                            shed = memState == "low" ? Sidecar.MemoryPressure.Shed(pid) : "-";
                        }
                        catch { }
                        FileLogger.Log($"[pressure] host={hostMB:F0}MB gpu={gpu} sidecar={cudamem} mem={memState}");
                        if (memState == "low")
                            FileLogger.Log($"[memory] state=low shed={shed} pid={pid}");
                    }
                    catch { }
                }, null, 60000, 60000);
            }
            catch { }
        }
        private void StopPressureLog()
        {
            try { _pressureTimer?.Dispose(); _pressureTimer = null; } catch { }
        }
        // --errortest gate surface (revert: revert error-codes): a bare
        // SidePanelWindow + the real ShowError path, sequenced so a gate
        // can verify every state (new post, in-place update, click ->
        // popup via the SAME handler a mouse click takes, unknown-code
        // guard). Windows render to PNGs off the visual tree, so a
        // fullscreen emulator covering the screen can't blind the gate
        // (coordinate screenshots/clicks would land in the user's game).
        // Deliberately never returns to the normal startup flow; exits by
        // taskkill. PNGs land in Roaming\WunderLanguage\gate_errortest.
        private static void ErrorCodeTestRun(string? code)
        {
            FileLogger.Log($"[error] --errortest start (code={code ?? "sequence"})");
            var panel = new Windows.SidePanelWindow();
            panel.Show();
            string dumpDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WunderLanguage", "gate_errortest");
            System.IO.Directory.CreateDirectory(dumpDir);
            if (code != null)
            {
                panel.ShowError(code);
                DumpWindowPng(panel, System.IO.Path.Combine(dumpDir, $"panel_{code}.png"));
                return;
            }
            panel.ShowError("E1");
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            int step = 0;
            t.Tick += (_, _) =>
            {
                step++;
                if (step == 1)
                {
                    // In-place update: same chip row, E2 text.
                    panel.ShowError("E2");
                }
                else if (step == 2)
                {
                    var popup = panel.TestClickChip();
                    DumpWindowPng(panel, System.IO.Path.Combine(dumpDir, "panel_e2.png"));
                    if (popup != null)
                        DumpWindowPng(popup, System.IO.Path.Combine(dumpDir, "popup_e2.png"));
                }
                else if (step == 3)
                {
                    // Second in-place update (E4) + unknown-code guard.
                    // E99 must NEVER be registered in ErrorCodes — E9 is
                    // now a real code (revert hdr-detect), so the
                    // unregistered sentinel moved up to keep this guard
                    // honest.
                    panel.ShowError("E4");
                    DumpWindowPng(panel, System.IO.Path.Combine(dumpDir, "panel_e4.png"));
                    panel.ShowError("E99");
                    t.Stop();
                }
            };
            t.Start();
        }
        // --errortest PNG dump: renders a window straight off the visual
        // tree. Revert: revert error-codes.
        private static void DumpWindowPng(System.Windows.Window w, string path)
        {
            try
            {
                w.UpdateLayout();
                int wpx = Math.Max(1, (int)Math.Ceiling(w.ActualWidth));
                int hpx = Math.Max(1, (int)Math.Ceiling(w.ActualHeight));
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    wpx, hpx, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(w);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(path);
                enc.Save(fs);
                FileLogger.Log($"[error] dump {path} {wpx}x{hpx}");
            }
            catch (System.Exception ex) { FileLogger.Log("[error] dump failed: " + ex.Message); }
        }
        // One nvidia-smi query, best-effort ("" on any failure). Split out
        // so the pressure tick reads top-down; 10s cap each.
        private static string RunNvidiaSmi(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                if (p is not null && p.WaitForExit(10000))
                    return p.StandardOutput.ReadToEnd() ?? "";
            }
            catch { }
            return "";
        }
    }
}
