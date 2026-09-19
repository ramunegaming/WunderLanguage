using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
namespace WunderLanguage.Host.Capture
{
    public class EmulatorWindowTracker
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        private delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int OBJID_WINDOW = 0;
        private const int CHILDID_SELF = 0;
        private readonly IntPtr _hWnd;
        private readonly DispatcherTimer _fallbackTimer;
        private Rectangle _lastBounds = Rectangle.Empty;
        private bool _lastMinimized;
        private IntPtr _hook;
        private IntPtr _foregroundHook;
        // Must be kept alive as a field for as long as the hook is
        // installed - SetWinEventHook does not pin the managed delegate, so
        // if this were a local variable it could be garbage collected while
        // Windows still holds a native pointer to it, causing a crash the
        // next time an event fires.
        private WinEventDelegate? _winEventDelegate;
        public event Action<Rectangle>? BoundsChanged;
        public event Action<IntPtr>? ForegroundChanged;
        public event Action? Minimized;
        public event Action? Restored;
        public EmulatorWindowTracker(IntPtr hWnd)
        {
            _hWnd = hWnd;
            // Kept as a low-frequency safety net only. The real-time
            // tracking during drags/moves is handled by the WinEventHook
            // below; this timer exists purely so the app keeps working
            // (just less smoothly) in the rare case the hook fails to
            // install, and to catch anything the hook might miss.
            _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _fallbackTimer.Tick += (_, _) => CheckState();
        }
        public void Start()
        {
            CheckState();
            _fallbackTimer.Start();
            InstallHook();
        }
        public void Stop()
        {
            _fallbackTimer.Stop();
            RemoveHook();
        }
        // Registers for EVENT_OBJECT_LOCATIONCHANGE notifications scoped to
        // the emulator's own process, so Windows tells us the instant the
        // window moves/resizes/minimizes/restores - essentially every frame
        // of a drag, rather than us polling and catching up after the fact.
        // Scoping the hook to the emulator's process id (rather than hooking
        // system-wide) also means our own companion windows' movements never
        // reach this callback, so there's no risk of feedback from our own
        // SetWindowPos calls elsewhere in the app.
        private void InstallHook()
        {
            try
            {
                GetWindowThreadProcessId(_hWnd, out uint processId);
                if (processId == 0)
                {
                    FileLogger.Log("EmulatorWindowTracker: could not resolve emulator process id, relying on fallback timer only.");
                    return;
                }
                _winEventDelegate = OnWinEvent;
                _hook = SetWinEventHook(
                    EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero, _winEventDelegate, processId, 0, WINEVENT_OUTOFCONTEXT);
                if (_hook == IntPtr.Zero)
                    FileLogger.Log("EmulatorWindowTracker: SetWinEventHook failed, relying on fallback timer only.");
                _foregroundHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
                if (_foregroundHook == IntPtr.Zero)
                    FileLogger.Log("EmulatorWindowTracker: foreground hook failed, z-pairing stays timer-only.");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"EmulatorWindowTracker: exception installing hook, relying on fallback timer only. {ex.Message}");
            }
        }
        private void RemoveHook()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _winEventDelegate = null;
        }
        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                try { ForegroundChanged?.Invoke(hwnd); } catch { }
                return;
            }
            // Filter to the specific top-level emulator window only - the
            // hook is scoped to its process, but other windows/controls
            // belonging to that same process (e.g. dialogs) could otherwise
            // also trigger this callback.
            if (hwnd != _hWnd || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
                return;
            CheckState();
        }
        private void CheckState()
        {
            bool isMinimized = WindowCapture.IsMinimized(_hWnd);
            if (isMinimized != _lastMinimized)
            {
                _lastMinimized = isMinimized;
                if (isMinimized)
                {
                    Minimized?.Invoke();
                }
                else
                {
                    // Force the next bounds check below to fire BoundsChanged
                    // even if the window happens to be back at the same spot,
                    // so the companion windows always reposition on restore.
                    _lastBounds = Rectangle.Empty;
                    Restored?.Invoke();
                }
            }
            if (isMinimized)
                return; // nothing to track while minimized
            if (!WindowCapture.TryGetWindowBounds(_hWnd, out var bounds))
                return; // window might be momentarily unavailable - just try again next event/tick
            if (bounds != _lastBounds)
            {
                _lastBounds = bounds;
                BoundsChanged?.Invoke(bounds);
            }
        }
    }
}