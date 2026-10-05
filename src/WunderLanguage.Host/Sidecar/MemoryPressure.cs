using System.Runtime.InteropServices;

namespace WunderLanguage.Host.Sidecar
{
    // Survival batch 3 (WI1 Ch5 working sets / memory notifications / page
    // priority): the host listens for the OS low-memory signal and sheds
    // sidecar pressure before the OS kills anything. Tier 1 (this batch):
    // drop the sidecar's memory priority to VERY_LOW so its trimmed pages
    // are reclaimed first, and voluntarily empty its working set so the
    // balance-set manager skips the game. Tier 2 (later): restart the
    // sidecar with WUNDER_NO_CAT on sustained Low. Revert: delete this file
    // + the two call sites in App.xaml.cs (Sample/Shed in pressure tick).
    public static class MemoryPressure
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateMemoryResourceNotification(int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryMemoryResourceNotification(IntPtr handle,
            [MarshalAs(UnmanagedType.Bool)] out bool state);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass,
            ref MEMORY_PRIORITY_INFORMATION info, int size);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_PRIORITY_INFORMATION
        {
            public uint MemoryPriority;
        }

        private const int LowNotification = 0;
        private const int HighNotification = 1;
        private const int ProcessMemoryPriority = 0;
        private const uint MEMORY_PRIORITY_VERY_LOW = 1;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_SET_QUOTA = 0x0100;

        private static IntPtr _lowHandle = IntPtr.Zero;
        private static IntPtr _highHandle = IntPtr.Zero;
        private static bool _armed;

        private static void Ensure()
        {
            if (_armed) return;
            _armed = true;
            try
            {
                _lowHandle = CreateMemoryResourceNotification(LowNotification);
                _highHandle = CreateMemoryResourceNotification(HighNotification);
                FileLogger.Log($"[memory] watcher armed low={_lowHandle != IntPtr.Zero} high={_highHandle != IntPtr.Zero}");
            }
            catch { }
        }

        // "low" (shed now) | "high" (plenty free — the healthy steady
        // state) | "ok" (between thresholds) | "unknown".
        public static string Sample()
        {
            Ensure();
            try
            {
                bool low = false, high = false;
                if (_lowHandle != IntPtr.Zero)
                    QueryMemoryResourceNotification(_lowHandle, out low);
                if (_highHandle != IntPtr.Zero)
                    QueryMemoryResourceNotification(_highHandle, out high);
                if (low) return "low";
                if (high) return "high";
                return "ok";
            }
            catch { return "unknown"; }
        }

        // Tier-1 shed: deprioritize + trim the sidecar's pages. Best-effort,
        // returns what was done for the log (" pri trim" / " pri" / "-").
        public static string Shed(int pid)
        {
            if (pid == 0) return "-";
            string done = "";
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_INFORMATION | PROCESS_SET_QUOTA,
                    false, pid);
                if (h == IntPtr.Zero) return "openfail";
                try
                {
                    var pri = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = MEMORY_PRIORITY_VERY_LOW };
                    if (SetProcessInformation(h, ProcessMemoryPriority, ref pri, Marshal.SizeOf(pri)))
                        done += "pri";
                }
                catch { }
                try
                {
                    if (EmptyWorkingSet(h))
                        done += (done.Length > 0 ? "+" : "") + "trim";
                }
                catch { }
            }
            catch { }
            finally
            {
                try { if (h != IntPtr.Zero) CloseHandle(h); } catch { }
            }
            return done.Length > 0 ? done : "-";
        }
    }
}
