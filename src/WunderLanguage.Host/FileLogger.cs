using System.IO;

namespace WunderLanguage.Host
{
    public static class FileLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WunderLanguage", "app.log");
        // Secondary persistent log (app2.log): armed by the presence of a
        // keep-log.armed sentinel file (toggled externally — create to arm,
        // delete to disarm; no UI, no settings churn). While armed, every
        // line is mirrored here and the file is NEVER truncated — it
        // survives relaunches so a previous session stays available.
        // app.log keeps its wipe-per-launch behavior either way.
        // Replay mode never mirrors (would pollute evidence with 2k-line
        // synthetic runs). 32MB rotation cap honors the original
        // wipe rationale (bounded storage). Revert: delete from here to
        // the end of the Keep block + the mirror call in Log.
        private static readonly string KeepPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WunderLanguage", "app2.log");
        private static readonly string KeepSentinel = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WunderLanguage", "keep-log.armed");
        private const long KeepMaxBytes = 32L * 1024 * 1024;
        public static bool ReplayMode = false;
        private static bool _keepBannerThisRun = false;
        private static bool _keepArmedCache = false;
        private static DateTime _keepArmedCheckedAt = DateTime.MinValue;
        private static bool IsKeepArmed()
        {
            // Cached 10s: arming mid-run takes effect without a restart,
            // and the check costs nothing on the hot path.
            if ((DateTime.UtcNow - _keepArmedCheckedAt).TotalSeconds < 10)
                return _keepArmedCache;
            try { _keepArmedCache = File.Exists(KeepSentinel); }
            catch { _keepArmedCache = false; }
            _keepArmedCheckedAt = DateTime.UtcNow;
            return _keepArmedCache;
        }

        // Each run gets its own clean log instead of growing forever -
        // the first write in a process's lifetime replaces the file,
        // every write after that appends normally.
        private static bool _initializedThisRun = false;
        private static readonly object _lock = new();

        public static void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);

                lock (_lock)
                {
                    if (!_initializedThisRun)
                    {
                        File.WriteAllText(LogPath, string.Empty);
                        _initializedThisRun = true;
                    }

                    File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                    if (!ReplayMode && IsKeepArmed())
                    {
                        try
                        {
                            if (!_keepBannerThisRun)
                            {
                                _keepBannerThisRun = true;
                                var fi = new FileInfo(KeepPath);
                                if (fi.Exists && fi.Length > KeepMaxBytes)
                                {
                                    try { File.Delete(KeepPath + ".prev"); } catch { }
                                    try { File.Move(KeepPath, KeepPath + ".prev"); } catch { }
                                }
                                File.AppendAllText(KeepPath, $"\n===== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} (keep armed) =====\n");
                            }
                            File.AppendAllText(KeepPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Logging must never itself be the thing that crashes the app.
            }
        }

        public static string Path_ => LogPath;
    }
}
