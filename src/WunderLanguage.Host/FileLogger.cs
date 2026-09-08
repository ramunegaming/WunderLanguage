using System.IO;

namespace WunderLanguage.Host
{
    public static class FileLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WunderLanguage", "app.log");

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
