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
        private readonly List<string> _recentOutput = new();

        public async Task StartAsync(IProgress<string>? statusProgress = null)
        {
            FileLogger.Log("SidecarProcessManager.StartAsync called");
            statusProgress?.Report("Checking translation engine...");

            string? sidecarDir = FindSidecarDirectory();
            string? runningVersion = await GetRunningVersionAsync();
            string? diskVersion = GetDiskVersion(sidecarDir);
            if (runningVersion is not null && runningVersion == diskVersion)
            {
                FileLogger.Log($"Reusing healthy sidecar version={runningVersion} - matches disk.");
                statusProgress?.Report("Translation engine already running.");
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

            string pythonExe = Path.Combine(sidecarDir, "venv", "Scripts", "python.exe");
            FileLogger.Log($"Python exe path: {pythonExe} (exists: {File.Exists(pythonExe)})");
            if (!File.Exists(pythonExe))
                throw new InvalidOperationException(
                    $"Python venv not found at {pythonExe}. Run the ocr-service setup steps first.");

            statusProgress?.Report("Starting translation engine...");

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

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                FileLogger.Log($"[sidecar stdout] {e.Data}");
                lock (_recentOutput) _recentOutput.Add(e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                FileLogger.Log($"[sidecar stderr] {e.Data}");
                lock (_recentOutput) _recentOutput.Add(e.Data);
            };

            if (!_process.Start())
                throw new InvalidOperationException("Failed to start the OCR service process.");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            FileLogger.Log($"Sidecar process started, PID: {_process.Id}");

            await WaitForHealthyAsync(statusProgress);
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
                        FileLogger.Log("Sidecar health check succeeded.");
                        return;
                    }
                }
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

        public void Stop()
        {
            try
            {
                if (_process is { HasExited: false })
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort on shutdown.
            }
        }
    }
}
