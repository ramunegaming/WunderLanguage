using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace WunderLanguage.Host.Sidecar
{
    // Launches and owns the Python OCR/translation service as a background
    // process with no visible window.
    public class SidecarProcessManager
    {
        private Process? _process;
        private string? _pythonExe;
        private readonly List<string> _recentOutput = new();
        // OS-owned group (WI1 Ch3): kill-on-close job. Lives as long as the
        // sidecar; closing it reaps the whole python tree on dirty host death.
        private readonly SidecarJob _job = new();

        public async Task StartAsync(IProgress<string>? statusProgress = null)
        {
            FileLogger.Log("SidecarProcessManager.StartAsync called");
            statusProgress?.Report("Checking translation engine...");

            string? sidecarDir = FindSidecarDirectory();
            _pythonExe = sidecarDir is null ? null : Path.Combine(sidecarDir, "venv", "Scripts", "python.exe");
            string? runningVersion = await GetRunningVersionAsync();
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
                FileLogger.Log($"Stale sidecar detected running={runningVersion} disk={diskVersion} - killing python.exe.");
                foreach (var p in Process.GetProcessesByName("python"))
                {
                    try { p.Kill(); } catch { }
                }
                await Task.Delay(2000);
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
                    Arguments = "-m uvicorn main:app --host 127.0.0.1 --port 8000",
                    WorkingDirectory = sidecarDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (cpuFallback)
                    psi.Environment["WUNDER_NO_CAT"] = "1";

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
                FileLogger.Log($"Sidecar process started, PID: {_process.Id} (OCR-only fallback: {cpuFallback})");

                try
                {
                    await WaitForHealthyAsync(statusProgress);
                    FileLogger.Log($"[startup] stage=sidecar-spawn-healthy ms={spawnSw.ElapsedMilliseconds} fallback={cpuFallback}");
                    StartCatWatcher();
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
                    var response = await http.GetAsync("http://127.0.0.1:8000/health");
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
        // Bounded (~3min cap), cancelled on Stop. Revert: delete + 2 calls.
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
                        for (int i = 0; i < 90 && !token.IsCancellationRequested; i++)
                        {
                            await Task.Delay(2000, token);
                            string cat = "unknown";
                            try
                            {
                                var resp = await http.GetAsync("http://127.0.0.1:8000/health", token);
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

        private static async Task<string?> GetRunningVersionAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                var response = await http.GetAsync("http://127.0.0.1:8000/health");
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

        public void Stop()
        {
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
