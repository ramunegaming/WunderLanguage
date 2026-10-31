using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace WunderLanguage.Host.Sidecar
{
    // Launches and owns the Python OCR/translation service as a background
    // process with no visible window.
    //
    // W1 service split (2026-10-17): this manager is role-parameterized
    // ("ocr" = capture service on :8000, "cat" = translator service on
    // :8001). One role per instance; the App owns one of each. OCR keeps
    // every legacy behavior (adopt/kill sweep, nocatfb retry, EngineDown
    // popup on exhausted budget). CAT always retries WITH the translator
    // (loads are reliable in a clean process); exhaustion shows the
    // terminal panel notice for BOTH roles (revert: revert
    // cat-reliability) — the old "fallback covers" assumption is false
    // for the Offline provider.
    // Revert: delete role plumbing (single OCR instance).
    public class SidecarProcessManager
    {
        private readonly string _role;
        private readonly string _module;
        private readonly int _port;
        private readonly string _readyStage;
        private readonly bool _restartWithCat;
        private string HealthUrl => $"http://127.0.0.1:{_port}/health";
        private Process? _process;
        private string? _pythonExe;
        private readonly List<string> _recentOutput = new();
        // OS-owned group (WI1 Ch3): kill-on-close job. Lives as long as the
        // sidecar; closing it reaps the whole python tree on dirty host death.
        private readonly SidecarJob _job = new();

        // Auto-restart (item 1 close-out): the job's active-process-zero is
        // the restart signal. Requested deaths suppress it; unrequested
        // ones get 3 quiet respawns (OCR 5s apart; CAT backoff 10s/60s/240s
        // to outlast transient load windows, revert: revert
        // cat-reliability), then the terminal panel notice fires once.
        private bool _stopRequested;
        private int _restarts;
        // Terminal-state notice (2026-10-13): when the budget is spent
        // the panel must say the engine died — 30 min of silent nothing
        // reads as policy failure. Static event (manager instance lives
        // in App, panel in AppShell); silent restarts unchanged, only
        // the terminal state notifies, once. Payload carries the ERROR
        // CODE ("E2"/"E3" by role) since 2026-10-23 — the registry in
        // Diagnostics/ErrorCodes.cs owns the wording (chip + popup), so
        // panel text and popup text can never drift. ErrorCodeNotice is
        // the informational sibling (E1: first load-window death, the
        // retrying burst becomes visible instead of silent).
        // Revert: revert error-codes (EngineDown goes back to carrying
        // the role-specific message text).
        public static event Action<string>? EngineDown;
        public static event Action<string>? ErrorCodeNotice;
        // Transport watchdog (item 2, 2026-10-30; revert:
        // revert transport-watchdog): the capture loop detects the
        // deaf-listener death (refused connections while the process is
        // alive) but does not own the manager - App does. Publish the
        // OCR instance here (one per process; the first OCR role wins)
        // so the loop can ask for a restart without new plumbing.
        public static SidecarProcessManager? OcrInstance;
        // Ready flag: set only when the healthy-wait has seen OCR ready,
        // cleared on death/stop. The watchdog must never kill during the
        // boot warmup (connection-refused there is normal, cited
        // 2026-10-08) - ready + refused = the deaf-listener class.
        private volatile bool _serviceReady;
        // Pending error replay (2026-10-23; revert: revert error-codes):
        // signals are one-shot, but the ONLY subscriber
        // (AppShell.BuildCaptureController) exists from Connect onward -
        // an error fired while sitting at the Connect window would be
        // lost and the connected session would start silently broken
        // (exactly the class this feature exists to kill). Keep the last
        // signal + its role: a healthy restart of THAT role clears it,
        // AppShell replays it when it subscribes. One slot shared by both
        // manager instances (last error wins; role-guarded clear so an
        // OCR restart can never wipe a pending CAT error).
        public static string? PendingErrorSignal;
        private static string? _pendingErrorRole;
        public static void SetPendingError(string code, string role)
        {
            PendingErrorSignal = code;
            _pendingErrorRole = role;
        }
        public static void ClearPendingError(string role)
        {
            if (_pendingErrorRole == role)
            {
                PendingErrorSignal = null;
                _pendingErrorRole = null;
            }
        }
        private bool _restarting;
        private CancellationTokenSource? _restartCts;
        // W1 root cause (2026-10-14, convicted): CAT 4-bit weight load
        // segfaults ~140s after start (deferred +120s thread) on 6GB VRAM
        // even with 4492MB free — threshold tuning cannot catch it. A
        // healthy-period death is therefore proof the spike will recur, so
        // the retry relaunches OCR-only (nocatfb). Startup-time deaths keep
        // the existing behavior (GPU first, one fallback). Fresh host
        // processes always try GPU first (no stale flags across launches).
        // Revert: revert nocatfb-retry.
        private bool _wasHealthy;
        private bool _noCatAfterCrash;

        public SidecarProcessManager(string role = "ocr")
        {
            _role = role == "cat" ? "cat" : "ocr";
            _module = _role == "cat" ? "cat_service" : "main";
            _port = _role == "cat" ? 8001 : 8000;
            _readyStage = _role;
            // OCR: legacy nocatfb policy (healthy death ⇒ OCR-only retry).
            // CAT: its whole job is the translator - always retry with it.
            _restartWithCat = _role == "cat";
            _job.TreeDied += OnTreeDied;
            if (_role == "ocr" && OcrInstance is null) OcrInstance = this;
        }

        private void LogDeathForensics()
        {
            try
            {
                string uptime = "?";
                string code = "?";
                int? deadPid = null;
                try
                {
                    var p = _process;
                    if (p is not null)
                    {
                        try { uptime = (DateTime.UtcNow - p.StartTime.ToUniversalTime()).ToString(@"hh\:mm\:ss"); } catch { }
                        try { if (p.HasExited) code = p.ExitCode.ToString(); else code = "still-running"; } catch { }
                        // crash-diag-logger: needed to attribute the Windows
                        // fault event to THIS death (rapid storms have 3
                        // python crashes within a minute). Revert: revert
                        // crash-diag-logger.
                        try { deadPid = p.Id; } catch { }
                    }
                    else code = "no-process";
                }
                catch { }
                string tail = "";
                try
                {
                    lock (_recentOutput)
                    {
                        int from = Math.Max(0, _recentOutput.Count - 40);
                        tail = string.Join("\n", _recentOutput.Skip(from));
                    }
                }
                catch { }
                if (tail.Length > 12000) tail = tail.Substring(tail.Length - 12000);
                // crash-diag-logger (2026-10-23, revert: revert
                // crash-diag-logger): the Windows Application log holds a
                // post-mortem receipt for every crash (EventID 1000:
                // faulting module + offset + exception code) - 56 records
                // over 8 days all say torch_cpu.dll+0x8e4a279 / 0xC0000005
                // during weight reads, and we never once read them. The
                // stderr ring also truncated the faulthandler torch frames
                // to 3KB (lost on 2026-10-23). A death now records: the
                // system memory snapshot (never logged before - the
                // "was the machine under pressure" question was
                // unanswerable), the Windows fault event for this pid, the
                // python-side 1s memory/VRAM sampler tail (pre-death
                // timeline), and a 12KB ring instead of 3KB.
                string snapshot = SystemMemorySnapshot();
                string fault = QueryWindowsFaultEvent(deadPid);
                string loader = DiagLoaderTail(15);
                var sb = new System.Text.StringBuilder();
                sb.Append($"[job:{_role}] death forensics restarts={_restarts} uptime={uptime} exit={code}");
                sb.Append($"\n[job:{_role}] death system-snapshot {snapshot}");
                sb.Append($"\n[job:{_role}] death windows-fault {fault}");
                if (!string.IsNullOrWhiteSpace(loader))
                    sb.Append($"\n[job:{_role}] death loader-timeline (diag_cat_load.csv tail):\n{loader}");
                sb.Append($"\ntail:\n{(string.IsNullOrWhiteSpace(tail) ? "(ring empty)" : tail)}");
                FileLogger.Log(sb.ToString());
            }
            catch { }
        }

        // --- crash-diag-logger helpers (revert: revert crash-diag-logger) ---

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static long Mb(long bytes) => bytes / (1024 * 1024);

        private static string SystemMemorySnapshot()
        {
            try
            {
                var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                if (!GlobalMemoryStatusEx(ref m)) return "(GlobalMemoryStatusEx failed)";
                return $"availPhysMB={Mb((long)m.ullAvailPhys)}/{Mb((long)m.ullTotalPhys)} freeCommitMB={Mb((long)m.ullAvailPageFile)}/{Mb((long)m.ullTotalPageFile)} load%={m.dwMemoryLoad}";
            }
            catch (Exception ex) { return "(snapshot failed: " + ex.Message + ")"; }
        }

        // Windows post-mortem receipt: Application log EventID 1000 =
        // faulting application + module + offset + exception code. Match
        // by faulting PID (exact); fall back to the newest python fault
        // in the window (event-write lag makes exact pairing ambiguous
        // during rapid storms, pid never is). Revert: revert crash-diag-logger.
        private static string QueryWindowsFaultEvent(int? pid)
        {
            try
            {
                var q = new EventLogQuery("Application", PathType.LogName,
                    "*[System[(EventID=1000) and TimeCreated[timediff(@SystemTime) <= 3600000]]]");
                string? newestPython = null;
                string? pidMatch = null;
                using (var reader = new EventLogReader(q))
                {
                    EventRecord rec;
                    while ((rec = reader.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            string? msg = null;
                            try { msg = rec.FormatDescription(); } catch { }
                            if (msg == null || !msg.Contains("python.exe", StringComparison.OrdinalIgnoreCase)) continue;
                            string compact = CompactFault(msg);
                            newestPython = compact;
                            if (pidMatch == null && pid.HasValue &&
                                msg.Contains($"Faulting process id: 0x{pid.Value:X}", StringComparison.OrdinalIgnoreCase))
                                pidMatch = compact;
                        }
                    }
                }
                if (pidMatch != null) return "match=pid " + pidMatch;
                if (newestPython != null) return "match=newest-python " + newestPython;
                return "no EventID=1000 for python in last 60min";
            }
            catch (Exception ex) { return "(event log query failed: " + ex.Message + ")"; }
        }

        private static string CompactFault(string msg)
        {
            try
            {
                string module = FaultField(msg, @"Faulting module name: ([^\r\n]+)");
                string offset = FaultField(msg, @"Fault offset: (\S+)");
                string exc = FaultField(msg, @"Exception code: (\S+)");
                return $"module={module} offset={offset} exc={exc}";
            }
            catch { return "(fault parse failed)"; }
        }

        private static string FaultField(string input, string pattern)
        {
            var m = System.Text.RegularExpressions.Regex.Match(input, pattern);
            return m.Success ? m.Groups[1].Value : "?";
        }

        // Python-side 1s memory sampler tail (cat_service writes it via
        // WUNDER_LOGDIR) - the RAM/commit/VRAM timeline BEFORE the death.
        // Revert: revert crash-diag-logger.
        private static string DiagLoaderTail(int maxLines)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage");
                string file = Path.Combine(dir, "diag_cat_load.csv");
                if (!File.Exists(file)) return "";
                var lines = File.ReadAllLines(file);
                int from = Math.Max(0, lines.Length - maxLines);
                return string.Join("\n", lines.Skip(from));
            }
            catch { return ""; }
        }

        // True when the dead process died young enough to be a LOAD
        // failure rather than a runtime one: the CAT weight load is
        // 15-25s (measured), so anything under 45s is "it never got to
        // the ready state". StartTime survives HasExited (cached) and
        // is permission-safe here (same user); any doubt -> false, which
        // just means no E1 chip. Revert: revert error-codes.
        private bool InLoadWindow()
        {
            try
            {
                var p = _process;
                if (p is null) return false;
                double sec = (DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds;
                return sec >= 0 && sec <= 45;
            }
            catch { return false; }
        }

        private void OnTreeDied()
        {
            try
            {
                // Death forensics (2026-10-13): 4 mid-session deaths with
                // zero logged cause — the ring holds unfiltered stderr the
                // keeper never shows. Dump BEFORE any restart clears it.
                // Same shape as the startup early-exit dump. Revert: delete
                // the call.
                try { LogDeathForensics(); } catch { }
                _serviceReady = false;
                if (_stopRequested || _restarting) return;
                // Error chip E1 (2026-10-23; revert: revert error-codes):
                // a CAT death INSIDE the load window is the "failed to
                // start" class - post once so a retrying burst is visible
                // on the panel instead of silent (restarts otherwise stay
                // silent by policy; only the terminal E2 announces).
                // CAT only: OCR has no known load-window death class.
                if (_role == "cat" && _restarts == 0 && InLoadWindow())
                {
                    FileLogger.Log($"[job:{_role}] load-window death - error chip E1 signaled (retrying, budget {_restarts}/3)");
                    try { SetPendingError("E1", _role); } catch { }
                    try { ErrorCodeNotice?.Invoke("E1"); } catch { }
                }
                if (_restarts >= 3)
                {
                    // Terminal state must show on the panel (AppShell
                    // 2026-10-13 conviction: a spent budget that stays
                    // silent reads as policy failure). CAT included
                    // (revert: revert cat-reliability): the "fallback
                    // covers" assumption is false for the Offline
                    // provider - the 2026-10-23 session sat through5
                    // silent minutes after CAT's budget burned inside a
                    // crash window: detection ran, every publish died at
                    // translation, no bubbles, no visible sign of why.
                    // Since 2026-10-23 the panel row is an error-code CHIP
                    // (registry owns the wording; popup has the detail).
                    string downCode = _role == "cat" ? "E2" : "E3";
                    FileLogger.Log($"[job:{_role}] restart budget exhausted - engine stays down until relaunch (error chip {downCode})");
                    try { SetPendingError(downCode, _role); } catch { }
                    try { EngineDown?.Invoke(downCode); } catch { }
                    // One-shot: don't spam every future death (there are no
                    // future trees, but a second zero can't be ruled out).
                    _restarts++;
                    return;
                }
                _restarting = true;
                bool nocatRetry = false;
                try { nocatRetry = !_restartWithCat && _wasHealthy; } catch { }
                if (nocatRetry)
                {
                    try { _noCatAfterCrash = true; } catch { }
                    try { FileLogger.Log("[job] healthy-period death: retrying OCR-only (nocatfb, CAT spike convicted)"); } catch { }
                }
                else if (_role == "cat")
                {
                    try { FileLogger.Log("[job:cat] translator death: retrying WITH translator (clean-process loads are reliable)"); } catch { }
                }
                _restartCts = new CancellationTokenSource();
                var token = _restartCts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _restarts++;
                        FileLogger.Log($"[job] restart attempt={_restarts}");
                        // CAT backoff (revert: revert cat-reliability): the
                        // 2026-10-23 incident burned all 3 attempts ~25s
                        // apart inside a ~105s transient crash window (CAT
                        // weight load overlapping the OCR model load +
                        // emulator/game) and the translator then stayed
                        // down all session - a load minutes later succeeded
                        // first try. CAT retries now space out 10s/60s/240s
                        // so a window gets outlasted; OCR keeps the fast
                        // 5s (its death class is different).
                        int delayMs = 5000;
                        if (_role == "cat") delayMs = _restarts == 1 ? 10_000 : _restarts == 2 ? 60_000 : 240_000;
                        await Task.Delay(delayMs, token);
                        lock (_recentOutput) _recentOutput.Clear();
                        await StartAsync(null);
                        FileLogger.Log("[job] restart healthy");
                        // A healthy start makes any pending signal for THIS
                        // role stale (E1 "failed to start" clearly didn't
                        // stick); role-guarded so OCR's health can't clear
                        // a pending CAT error. Revert: revert error-codes.
                        try { ClearPendingError(_role); } catch { }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        FileLogger.Log($"[job] restart failed: {ex.Message.Split('\n')[0]}");
                    }
                    finally { _restarting = false; }
                }, token);
            }
            catch { }
        }

        public async Task StartAsync(IProgress<string>? statusProgress = null)
        {
            FileLogger.Log("SidecarProcessManager.StartAsync called");
            statusProgress?.Report("Checking translation engine...");

            string? sidecarDir = FindSidecarDirectory();
            _pythonExe = sidecarDir is null ? null : Path.Combine(sidecarDir, "venv", "Scripts", "python.exe");
            string? runningVersion = await GetRunningVersionAsync(_port);
            string? diskVersion = GetDiskVersion(sidecarDir);
            if (runningVersion is not null && runningVersion == diskVersion)
            {
                // Adopted, not owned: a previous host may have died dirty
                // (Task Manager / debugger stop skips OnExit AND ProcessExit),
                // so Stop() reaps it on OUR exit instead of leaving it immortal.
                FileLogger.Log($"Reusing healthy sidecar version={runningVersion} - matches disk.");
                // Polite-tenant priority applies to adopted engines too (a
                // previous host may predate the policy).
                try
                {
                    foreach (var p in Process.GetProcessesByName("python"))
                    {
                        try
                        {
                            if ((p.MainModule?.FileName ?? "").StartsWith(sidecarDir ?? "?", StringComparison.OrdinalIgnoreCase))
                                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                        }
                        catch { }
                    }
                }
                catch { }
                statusProgress?.Report("Translation engine already running.");
                // Adopted mid-warm is still warming: gate on OCR readiness
                // (returns instantly when already ready) so the capture loop
                // never meets a 503 /ocr.
                await WaitForHealthyAsync(statusProgress);
                StartCatWatcher();
                return;
            }
            if (runningVersion is not null)
            {
                // Stale sweep is OCR-owned (unit approach): the App starts
                // OCR first, which reaps every venv python when stale; the
                // CAT manager never mass-kills (it would fight the OCR
                // manager over shared interpreter processes).
                if (_role == "ocr")
                {
                    FileLogger.Log($"Stale sidecar detected running={runningVersion} disk={diskVersion} - killing python.exe.");
                    foreach (var p in Process.GetProcessesByName("python"))
                    {
                        try { p.Kill(); } catch { }
                    }
                    await Task.Delay(2000);
                }
                else
                {
                    FileLogger.Log($"Stale CAT sidecar detected running={runningVersion} disk={diskVersion} - spawning fresh (OCR sweep owns kills).");
                }
            }
            FileLogger.Log($"Sidecar directory resolved to: {sidecarDir ?? "NOT FOUND"}");
            if (sidecarDir is null)
                throw new InvalidOperationException("Could not find the ocr-service folder relative to this app.");

            string pythonExe = _pythonExe ?? Path.Combine(sidecarDir, "venv", "Scripts", "python.exe");
            FileLogger.Log($"Python exe path: {pythonExe} (exists: {File.Exists(pythonExe)})");
            if (!File.Exists(pythonExe))
                throw new InvalidOperationException(
                    $"Python venv not found at {pythonExe}. Run the ocr-service setup steps first.");

            statusProgress?.Report("Starting translation engine...");

            // One-shot GPU-load-crash fallback (cited 2026-10-01: CAT 4-bit
            // weight load segfaults at shard 1 under load, twice in a row —
            // bitsandbytes fragility, fails fast in seconds). First attempt
            // is full GPU; if the process dies during startup, retry once
            // with the translator disabled (OCR stays GPU-fast, /translate
            // degrades to the host fallback chain). No stale flags: every
            // launch tries GPU first.
            bool cpuFallback = false;
            var spawnSw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-m uvicorn {_module}:app --host 127.0.0.1 --port {_port}",
                    WorkingDirectory = sidecarDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (cpuFallback || _noCatAfterCrash)
                    psi.Environment["WUNDER_NO_CAT"] = "1";
                // crash-diag-logger (revert: revert crash-diag-logger):
                // the python side writes full all-threads faulthandler
                // dumps + a 1s memory/VRAM sampler into the app log dir.
                psi.Environment["WUNDER_LOGDIR"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage");
                if (_role == "cat")
                {
                    psi.Environment["WUNDER_ROLE"] = "cat";
                    // CAT crash class (revert: revert cat-reliability):
                    // 2026-10-23, exit 0xC0000005 during safetensors shard
                    // reads - the 2026-10-17 "weight reads segfault" family
                    // in its CROSS-PROCESS form (clean process, load
                    // overlap window + emulator pressure). mmap'd shard
                    // reads fault under system load; HF_HUB_DISABLE_MMAP
                    // forces plain reads - the documented Windows
                    // mitigation. Cost ~+1s on the one-time load.
                    psi.Environment["HF_HUB_DISABLE_MMAP"] = "1";
                }
                else
                    psi.Environment["WUNDER_ROLE"] = "ocr";

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is null) return;
                    if (ShouldLogSidecarLine(e.Data))
                        FileLogger.Log($"[sidecar stdout] {e.Data}");
                    lock (_recentOutput) _recentOutput.Add(e.Data);
                };
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is null) return;
                    if (ShouldLogSidecarLine(e.Data))
                        FileLogger.Log($"[sidecar stderr] {e.Data}");
                    lock (_recentOutput) _recentOutput.Add(e.Data);
                };

                if (!_process.Start())
                    throw new InvalidOperationException("Failed to start the OCR service process.");

                // Perf batch B (polite tenant, 2nd book pass: priorities,
                // not limits — we yield CPU to the game, never cap it).
                // BelowNormal, never Idle: starvation would break the
                // product, a notch down just schedules the game first.
                // Host loop stays Normal (UI responsiveness). Revert:
                // delete this block.
                try { _process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                // Group the child in the kill-on-close job (no-op if the job
                // failed to create; nested-job hosts may refuse — harmless).
                // Tree shape (cited 2026-10-06 snapshot): venv python.exe is a
                // launcher stub — it re-execs the base interpreter as a
                // CHILD (uvicorn "server process" PID != spawned PID), so
                // _process.HasExited watches the stub, not the server. The
                // job covers the whole tree anyway (cap, kill-on-close and
                // these notifications all apply to stub+server+probes);
                // server liveness is decided by /health, never by HasExited.
                try
                {
                    _job.Ensure();
                    bool ok = _job.Assign(_process);
                    // Enforceable backstop behind the BelowNormal hint (WI1
                    // Ch3/Ch4): burst spikes can never take more than half
                    // the machine. Revert: delete these two lines.
                    int cap = _job.SetRateCap(50);
                    FileLogger.Log($"[job] assign pid={_process.Id} ok={ok} cpucap={cap}%");
                }
                catch { }

                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                FileLogger.Log($"[{_role}] process started, PID: {_process.Id} (OCR-only fallback: {cpuFallback || _noCatAfterCrash})");

                if (_role == "cat")
                {
                    // Load-before-serve (convicted 2026-10-17: concurrent
                    // server activity during weight load segfaults): HTTP
                    // binds only after weights load, so readiness waiting
                    // would block app startup ~60s. Never block on the
                    // translator — warm-watch covers readiness, TreeDied
                    // covers early death. Brief liveness check catches
                    // stillborn spawns for the degraded-start log.
                    await Task.Delay(3000);
                    if (_process is { HasExited: true })
                        throw new InvalidOperationException($"CAT sidecar exited during startup (code {_process.ExitCode}).");
                    FileLogger.Log($"[startup] stage=cat-spawn-accepted port={_port}");
                    StartCatWatcher();
                    return;
                }

                try
                {
                    await WaitForHealthyAsync(statusProgress);
                    FileLogger.Log($"[startup] stage=sidecar-spawn-healthy role={_role} port={_port} ms={spawnSw.ElapsedMilliseconds} fallback={cpuFallback}");
                    try { _wasHealthy = true; } catch { }
                    if (_role == "cat") StartCatWatcher();
                    return;
                }
                catch (InvalidOperationException ex) when (!cpuFallback && ex.Message.Contains("exited unexpectedly"))
                {
                    FileLogger.Log($"Sidecar crashed during startup (likely CAT GPU load) - retrying once with translator disabled: {ex.Message.Split('\n')[0]}");
                    statusProgress?.Report("Translation engine stumbled on GPU load - retrying in OCR-only mode (translations fall back)...");
                    try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
                    lock (_recentOutput) _recentOutput.Clear();
                    await Task.Delay(2000);
                    cpuFallback = true;
                }
            }
        }

        // Log hygiene: tqdm progress bars, per-line transformers warnings,
        // and observe/define/health chatter never diagnose anything but
        // dominate every log. Crash diagnosis still gets everything via
        // _recentOutput. POST /ocr + POST /translate stay (latency gaps).
        private static bool ShouldLogSidecarLine(string data)
        {
            // Diagnostics-gated chatter: per-line word/confusion stats are
            // dev evidence (highest-volume sidecar lines after POSTs).
            // POST /ocr + /translate + stage/error lines always stay.
            if (!Settings.Diag.Enabled)
            {
                if (data.Contains("[confusion]", StringComparison.Ordinal)) return false;
                if (data.Contains("[words]", StringComparison.Ordinal)) return false;
            }            if (data.Contains("Loading weights:", StringComparison.Ordinal)) return false;
            if (data.Contains("it/s]", StringComparison.Ordinal)) return false;
            if (data.Contains("POST /observe", StringComparison.Ordinal)) return false;
            if (data.Contains("POST /define", StringComparison.Ordinal)) return false;
            if (data.Contains("GET /health", StringComparison.Ordinal)) return false;
            if (data.Contains("Both `max_new_tokens`", StringComparison.Ordinal)) return false;
            if (data.Contains("generation_config", StringComparison.Ordinal)) return false;
            if (data.Contains("pipelines sequentially", StringComparison.Ordinal)) return false;
            if (data.Contains("clean_up_tokenization", StringComparison.Ordinal)) return false;
            return true;
        }

        private async Task WaitForHealthyAsync(IProgress<string>? statusProgress)
        {
            using var http = new HttpClient();

            // Bumped from 180s (3 minutes) - that was sized around the
            // smaller CAT-Translate-1.4b model, which happened to finish
            // its one-time ~2-3GB download just under that cap. Stepping
            // up to 3.3b (a ~6-7GB one-time download, quantized to 4-bit
            // during loading) blew straight past it - the sidecar was
            // still genuinely working (had only just started downloading
            // in earnest at the 164s mark) when the old timeout gave up
            // on it. 1200s (20 minutes) is generous enough for a large
            // download even on a slow connection, while still eventually
            // giving up if something is genuinely stuck rather than
            // waiting forever with no signal. This only matters on first
            // run - once a model is cached locally, subsequent launches
            // load from disk and start in seconds, not minutes.
            const int maxWaitSeconds = 1200;

            for (int i = 0; i < maxWaitSeconds; i++)
            {
                // Check if the process already died, rather than waiting out
                // a pointless multi-minute timeout when it failed instantly.
                if (_process is { HasExited: true })
                {
                    string output;
                    lock (_recentOutput) output = string.Join("\n", _recentOutput);

                    FileLogger.Log($"Sidecar exited early with code {_process.ExitCode}. Output:\n{output}");
                    throw new InvalidOperationException(
                        $"The translation engine process exited unexpectedly (code {_process.ExitCode}).\n\n" +
                        $"Recent output:\n{(string.IsNullOrWhiteSpace(output) ? "(none captured)" : output)}");
                }

                try
                {
                    var response = await http.GetAsync(HealthUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        // Staged readiness: HTTP up means the API is bound,
                        // not that models are loaded. Startup completes on
                        // OCR ready (CAT warms behind; /translate falls back
                        // meanwhile). A reported stage failure throws with
                        // the reason instead of waiting out the cap blind.
                        string ocr = "ready";
                        try
                        {
                            string body = await response.Content.ReadAsStringAsync();
                            using var doc = System.Text.Json.JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("ocr", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.String)
                                ocr = o.GetString() ?? "ready";
                        }
                        catch { }
                        if (ocr == "ready")
                        {
                            FileLogger.Log("Sidecar health check succeeded.");
                            _serviceReady = true;
                            return;
                        }
                        if (ocr.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"The translation engine reported an OCR failure: {ocr}.");
                        statusProgress?.Report($"Warming up OCR models... ({i}s elapsed)");
                        await Task.Delay(1000);
                        continue;
                    }
                }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex)
                {
                    if (i % 10 == 0)
                        FileLogger.Log($"Health check attempt {i} failed: {ex.Message}");
                }

                statusProgress?.Report(i < 10
                    ? $"Starting translation engine... ({i}s)"
                    : $"Still starting - first run downloads translation models and can take " +
                      $"several minutes on a slower connection ({i}s elapsed)...");

                await Task.Delay(1000);
            }

            throw new InvalidOperationException("The translation engine did not start in time.");
        }

        private static async Task<bool> IsHealthyAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                var response = await http.GetAsync("http://127.0.0.1:8000/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindSidecarDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "ocr-service");
                if (File.Exists(Path.Combine(candidate, "main.py")))
                    return candidate;

                dir = dir.Parent;
            }

            return null;
        }

        // CAT-warmth visibility (perf batch: startup completes at OCR
        // ready while CAT warms behind — nothing surfaced the transition).
        // Fire-and-forget poller, log-only: ready/disabled/failed lands as
        // [startup] stage=cat-<state>. No popup — the fallback chain covers
        // function, and a dialog mid-gameplay is worse than the gap.
        // Bounded (~5min cap to cover the 120s CAT deferral + load;
        // cited 2026-10-08), cancelled on Stop. Revert: delete + 2 calls.
        private CancellationTokenSource? _catWatchCts;
        private void StartCatWatcher()
        {
            try
            {
                try { _catWatchCts?.Cancel(); } catch { }
                _catWatchCts = new CancellationTokenSource();
                var token = _catWatchCts.Token;
                long t0 = Environment.TickCount64;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                        for (int i = 0; i < 150 && !token.IsCancellationRequested; i++)
                        {
                            await Task.Delay(2000, token);
                            string cat = "unknown";
                            try
                            {
                                var resp = await http.GetAsync(HealthUrl, token);
                                if (resp.IsSuccessStatusCode)
                                {
                                    string body = await resp.Content.ReadAsStringAsync(token);
                                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                                    if (doc.RootElement.TryGetProperty("cat", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                                        cat = c.GetString() ?? "unknown";
                                }
                                else continue;
                            }
                            catch { continue; }
                            if (cat == "warming" || cat == "unknown") continue;
                            FileLogger.Log($"[startup] stage=cat-{cat} ms={Environment.TickCount64 - t0}");
                            return;
                        }
                        FileLogger.Log("[startup] stage=cat-watch-timeout (still warming after ~3min)");
                    }
                    catch { }
                }, token);
            }
            catch { }
        }

        private static async Task<string?> GetRunningVersionAsync(int port)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                var response = await http.GetAsync($"http://127.0.0.1:{port}/health");
                if (!response.IsSuccessStatusCode) return null;
                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("code_version", out var v))
                    return v.GetString();
            }
            catch { }
            return null;
        }

        private static string? GetDiskVersion(string? sidecarDir)
        {
            try
            {
                if (sidecarDir is null) return null;
                string mainPy = Path.Combine(sidecarDir, "main.py");
                if (!File.Exists(mainPy)) return null;
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var fs = File.OpenRead(mainPy);
                string hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                return "py-" + hash.Substring(0, 8);
            }
            catch { return null; }
        }

        // Perf batch (pressure logging): owned sidecar PID for the
        // App-owned 60s [pressure] line. Host-side bytes are not the
        // signal (safetensors weights are file mappings, CUDA allocs
        // live on-device — an idle torch sidecar commits ~1MB), so the
        // line attributes per-PID VRAM via nvidia-smi instead. Adopted
        // (null _process) reports 0. Never throws.
        public int SidecarPid()
        {
            try
            {
                var p = _process;
                if (p is null || p.HasExited) return 0;
                return p.Id;
            }
            catch { return 0; }
        }

        // Transport watchdog restart (item 2, 2026-10-30; revert:
        // revert transport-watchdog). The 2026-10-30 FFVI leg died for
        // 5 minutes with NO death signal: the sidecar's HTTP accept
        // coroutine broke (WinError 64) while the process stayed alive,
        // so the job never saw a death and OCR requests were refused
        // forever. Ready + refused = a deaf listener that no crash path
        // can catch, so THIS is the seam that kills the tree: the normal
        // TreeDied handler then owns forensics + the restart budget +
        // the terminal E3 if the budget is spent. Guards: never during
        // boot warmup (_serviceReady false), never during an in-flight
        // restart, never after Stop().
        public bool TryRestartDeaf(string cause)
        {
            try
            {
                if (_stopRequested || _restarting) return false;
                if (!_serviceReady) return false;
                var p = _process;
                if (p is null || p.HasExited) return false;
                _serviceReady = false;
                FileLogger.Log($"[job:{_role}] transport watchdog: pid={p.Id} ready but refusing connections ({cause}) - killing tree for restart (budget {_restarts}/3)");
                p.Kill(entireProcessTree: true);
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[job:{_role}] transport watchdog restart failed: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            // Requested death: the job watcher must NOT restart the tree
            // it sees dying below (kill races the completion packets).
            _stopRequested = true;
            _serviceReady = false;
            try { _restartCts?.Cancel(); } catch { }
            try { _catWatchCts?.Cancel(); } catch { }
            try
            {
                if (_process is { HasExited: false })
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort on shutdown.
            }
            // Closing the job handle reaps any stragglers the explicit kill
            // missed (grandchildren uvicorn spawns). Dispose AFTER the kill
            // attempt so the log line above keeps its PID meaning.
            try { _job.Dispose(); } catch { }
            // Reuse-path sidecars are adopted, not owned (_process is null).
            // Reap OUR venv's python so a dirty-killed previous host can't
            // leave an immortal orphan. Path-matched (never touches a user's
            // unrelated python), and skipped while another Host is still
            // alive so a double-launched sibling keeps its engine.
            if (_process is null && _pythonExe is not null
                && Process.GetProcessesByName("WunderLanguage.Host").Length <= 1)
            {
                foreach (var p in Process.GetProcessesByName("python"))
                {
                    try
                    {
                        if (string.Equals(p.MainModule?.FileName, _pythonExe, StringComparison.OrdinalIgnoreCase))
                            p.Kill();
                    }
                    catch { }
                }
            }
        }
    }
}
