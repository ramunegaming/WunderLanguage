using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WunderLanguage.Host.Sidecar
{
    // Survival batch 1 (WI1 Ch3 Jobs): the sidecar python.exe belongs to an
    // OS job object with KILL_ON_JOB_CLOSE. Closing the job handle kills the
    // whole tree even if the host dies dirty (Task Manager kill, debugger
    // stop skips OnExit AND ProcessExit). Revert: delete this file + the
    // three call sites in SidecarProcessManager (field, Assign, Dispose).
    public sealed class SidecarJob : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);

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
