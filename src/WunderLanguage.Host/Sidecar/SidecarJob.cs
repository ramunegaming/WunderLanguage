using System.Runtime.InteropServices;

namespace WunderLanguage.Host.Sidecar
{
    // Survival batches 1+2+5 (WI1 Ch3 Jobs; WI2 Ch10 WER-adjacent
    // detect): the sidecar python.exe belongs to an OS job object with
    // KILL_ON_JOB_CLOSE plus a hard CPU-rate cap, AND the job reports to
    // an I/O completion port — a dirty sidecar death lands as an exact
    // [job] event line (abnormal-exit vs clean vs limit) instead of
    // opaque 503s. Watcher is a background thread on a 2s poll; no new
    // per-minute log volume (lines only on transitions).
    // Revert: delete this file + the call sites in SidecarProcessManager
    // (field, Assign/SetRateCap, Dispose).
    public sealed class SidecarJob : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetInformationJobObject")]
        private static extern bool SetJobCpuRate(IntPtr job, int infoClass,
            ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info, int size);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetInformationJobObject")]
        private static extern bool SetJobCompletionPort(IntPtr job, int infoClass,
            ref JOBOBJECT_ASSOCIATE_COMPLETION_PORT info, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateIoCompletionPort(IntPtr fileHandle, IntPtr existing,
            UIntPtr completionKey, uint threads);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetQueuedCompletionStatus(IntPtr port, out uint bytes,
            out UIntPtr key, out IntPtr overlapped, uint timeoutMs);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long JobTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        private const int JobObjectExtendedLimitInformation = 9;
        private const int JobObjectCpuRateControlInformation = 15;
        private const int JobObjectAssociateCompletionPortInformation = 7;

        // winnt.h JOB_OBJECT_MSG_*: arrives in dwNumberOfBytesTransferred,
        // lpOverlapped carries the PID for process messages (cited MSDN
        // JOBOBJECT_ASSOCIATE_COMPLETION_PORT docs).
        private static string JobEventName(uint msg) => msg switch
        {
            1 => "end-of-job-time",
            2 => "end-of-process-time",
            3 => "active-process-limit",
            4 => "active-process-zero",
            5 => "new-process",
            6 => "exit-process",
            7 => "ABNORMAL-exit-process",
            8 => "process-memory-limit",
            9 => "job-memory-limit",
            10 => "notification-limit",
            11 => "job-cycle-time-limit",
            _ => $"unknown-{msg}",
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT
        {
            public UIntPtr CompletionKey;
            public IntPtr CompletionPort;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            public uint ControlFlags;
            public uint CpuRate; // percent * 100 (1..10000) for hard cap
        }

        private const uint CPU_ENABLE = 0x1;
        private const uint CPU_HARD_CAP = 0x4;

        // Enforceable ceiling on the job's CPU share (percent of all CPUs per
        // 600ms interval). 50 = at most ~6 of 12 logical CPUs here; smaller
        // boxes still keep half the machine for the game. Best-effort:
        // returns the applied percent, 0 when the OS refused.
        public int SetRateCap(int percent)
        {
            try
            {
                if (_handle == IntPtr.Zero) return 0;
                if (percent < 5) percent = 5;
                if (percent > 100) percent = 100;
                var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
                {
                    ControlFlags = CPU_ENABLE | CPU_HARD_CAP,
                    CpuRate = (uint)(percent * 100),
                };
                if (SetJobCpuRate(_handle, JobObjectCpuRateControlInformation,
                    ref info, Marshal.SizeOf(info)))
                    return percent;
                try { FileLogger.Log($"[job] ratecap failed err={Marshal.GetLastWin32Error()}"); } catch { }
            }
            catch { }
            return 0;
        }

        public void Ensure()
        {
            if (_handle != IntPtr.Zero) return;
            try
            {
                _handle = CreateJobObject(IntPtr.Zero, null);
                if (_handle == IntPtr.Zero || _handle == new IntPtr(-1))
                {
                    _handle = IntPtr.Zero;
                    return;
                }
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                SetInformationJobObject(_handle, JobObjectExtendedLimitInformation,
                    ref info, Marshal.SizeOf(info));
                // Completion-port reporting (WI1 Ch3 "Jobs can also be
                // associated with an I/O completion port"). Best-effort:
                // a watcher thread logs transitions; failure just means
                // death stays opaque (status quo ante).
                try
                {
                    _port = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 0);
                    if (_port != IntPtr.Zero && _port != new IntPtr(-1))
                    {
                        var assoc = new JOBOBJECT_ASSOCIATE_COMPLETION_PORT
                        {
                            CompletionKey = UIntPtr.Zero,
                            CompletionPort = _port,
                        };
                        if (SetJobCompletionPort(_handle, JobObjectAssociateCompletionPortInformation,
                            ref assoc, Marshal.SizeOf(assoc)))
                            StartWatcher();
                        else
                        {
                            try { CloseHandle(_port); } catch { }
                            _port = IntPtr.Zero;
                        }
                    }
                    else _port = IntPtr.Zero;
                }
                catch { _port = IntPtr.Zero; }
            }
            catch { _handle = IntPtr.Zero; }
        }

        public bool Assign(System.Diagnostics.Process process)
        {
            try
            {
                if (_handle == IntPtr.Zero) return false;
                return AssignProcessToJobObject(_handle, process.Handle);
            }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_port != IntPtr.Zero)
                {
                    CloseHandle(_port);
                    _port = IntPtr.Zero;
                }
            }
            catch { }
            try
            {
                if (_handle != IntPtr.Zero)
                {
                    CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }
            catch { }
        }

        private IntPtr _port = IntPtr.Zero;

        // Fired on active-process-zero only (msg 4): the tree is fully
        // dead. Single exit/abnormal lines are NOT triggers — short-lived
        // import probes die that way on healthy startups (cited 2026-10-06).
        // The subscriber (SidecarProcessManager) decides: restart, unless
        // the death was requested via Stop().
        public event Action? TreeDied;

        // Background drain: 2s poll so Dispose converges without a wake
        // packet. Logs transitions only — new/exit/zero on clean shutdown,
        // ABNORMAL or limit lines are the ones that matter.
        private void StartWatcher()
        {
            try
            {
                var port = _port;
                _ = Task.Run(() =>
                {
                    try
                    {
                        while (!_disposed)
                        {
                            bool ok;
                            uint msg = 0;
                            IntPtr pidPtr = IntPtr.Zero;
                            try
                            {
                                ok = GetQueuedCompletionStatus(port, out msg,
                                    out UIntPtr _, out pidPtr, 2000);
                            }
                            catch { return; }
                            if (!ok) continue;
                            try { FileLogger.Log($"[job] event={JobEventName(msg)} pid={pidPtr.ToInt64()}"); } catch { }
                            if (msg == 4)
                            {
                                try { TreeDied?.Invoke(); } catch { }
                            }
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
