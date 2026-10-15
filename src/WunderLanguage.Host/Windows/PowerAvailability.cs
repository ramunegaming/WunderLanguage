using System.Runtime.InteropServices;

namespace WunderLanguage.Host.Windows
{
    // Survival batch 4 (WI1 Ch5 power manager): a session-scoped System
    // availability request so idle-sleep can't kill a live play session.
    // Held in CaptureController.Start, released in Stop — pause, recalibrate
    // and shutdown all release. System-only (the game owns the display);
    // never Away-mode; never blocks user-initiated sleep/lid/hibernate,
    // only idle-initiation. Battery-Saver opts out (bad citizenship to
    // block sleep when the OS is conserving). Refcounted for Stop/Start
    // churn on recalibrate. Revert: delete this file + the Hold/Release
    // calls in CaptureController.
    public static class PowerAvailability
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct REASON_CONTEXT
        {
            public uint Version;
            public uint Flags;
            public REASON_STRING Reason;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct REASON_STRING
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string SimpleReasonString;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PowerSetRequest(IntPtr handle, int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PowerClearRequest(IntPtr handle, int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private const int PowerRequestSystemRequired = 0;
        private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

        private static IntPtr _handle = IntPtr.Zero;
        private static int _holders;
        private static readonly object _gate = new();

        public static void Hold()
        {
            lock (_gate)
            {
                _holders++;
                if (_holders > 1) return;
                try
                {
                    // Battery-Saver opts out: blocking sleep while the OS is
                    // conserving is the opposite of survival citizenship.
                    bool saver = false;
                    string ac = "?";
                    try
                    {
                        if (GetSystemPowerStatus(out var st))
                        {
                            ac = st.ACLineStatus == 1 ? "ac" : st.ACLineStatus == 0 ? "dc" : "?";
                            saver = st.SystemStatusFlag == 1;
                        }
                    }
                    catch { }
                    if (saver)
                    {
                        FileLogger.Log("[power] hold skipped (battery saver on)");
                        return;
                    }
                    var ctx = new REASON_CONTEXT
                    {
                        Version = 0,
                        Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                        Reason = new REASON_STRING { SimpleReasonString = "WunderLanguage live translation session" },
                    };
                    _handle = PowerCreateRequest(ref ctx);
                    if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
                    {
                        _handle = IntPtr.Zero;
                        FileLogger.Log("[power] create failed");
                        return;
                    }
                    if (PowerSetRequest(_handle, PowerRequestSystemRequired))
                        FileLogger.Log($"[power] held (ac={ac})");
                    else
                        FileLogger.Log("[power] set failed");
                }
                catch { }
            }
        }

        public static void Release()
        {
            lock (_gate)
            {
                if (_holders > 0) _holders--;
                if (_holders > 0) return;
                try
                {
                    if (_handle != IntPtr.Zero)
                    {
                        try { PowerClearRequest(_handle, PowerRequestSystemRequired); } catch { }
                        try { CloseHandle(_handle); } catch { }
                        _handle = IntPtr.Zero;
                        FileLogger.Log("[power] released");
                    }
                }
                catch { }
            }
        }
    }
}
