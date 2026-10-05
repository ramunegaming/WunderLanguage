using System.Runtime.InteropServices;

namespace WunderLanguage.Host.Sidecar
{
    // Survival batches 1+2 (WI1 Ch3 Jobs, Ch3/Ch4 CPU rate control): the
    // sidecar python.exe belongs to an OS job object with KILL_ON_JOB_CLOSE
    // plus a hard CPU-rate cap. The cap is an enforceable backstop behind
    // the BelowNormal hint: burst spikes can never starve the game, while
    // orchestration (GPU-bound OCR/CAT) never needs more than the cap.
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
                if (_handle != IntPtr.Zero)
                {
                    CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }
            catch { }
        }
    }
}
