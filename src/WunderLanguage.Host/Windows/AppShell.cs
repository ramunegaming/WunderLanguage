using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WunderLanguage.Host.Capture;
using WunderLanguage.Host.Ocr;
using WunderLanguage.Host.Settings;
using WunderLanguage.Host.Translation;

namespace WunderLanguage.Host.Windows
{
    // Owns the top bar, side panel, and bottom bar companion windows, keeps
    // them positioned around the calibrated game region, and feeds the side
    // panel with live pipeline output.
    public class AppShell
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern IntPtr BeginDeferWindowPos(int nNumWindows);
        [DllImport("user32.dll")]
        private static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        private const int GWLP_HWNDPARENT = -8;
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;

        private static IntPtr SetWindowOwner(IntPtr hWnd, IntPtr owner)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, GWLP_HWNDPARENT, owner);
            return new IntPtr(SetWindowLong32(hWnd, GWLP_HWNDPARENT, owner.ToInt32()));
        }

        // Pop-out feed (revert: revert popout-feed): WS_EX_NOACTIVATE on the
        // detached feed. Same read/modify/write shape as SetWindowOwner - the
        // flag rides alongside whatever else is already in the ex-style, and
        // clearing it on re-dock restores only the bit we added.
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static IntPtr GetExtendedStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, GWL_EXSTYLE);
            return new IntPtr(GetWindowLong32(hWnd, GWL_EXSTYLE));
        }

        private static IntPtr SetExtendedStyle(IntPtr hWnd, IntPtr exStyle)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, GWL_EXSTYLE, exStyle);
            return new IntPtr(SetWindowLong32(hWnd, GWL_EXSTYLE, exStyle.ToInt32()));
        }

        // Global hotkey (RegisterHotKey): the only trigger that works when an
        // exclusive/fullscreen game owns the foreground and the feed is on the
        // other monitor. Registered against the side panel's HWND - hidden or
        // not, a window with a message pump still gets WM_HOTKEY posted to it.
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyIdToggleDetach = 0x5741; // "WA"
        private const int HotkeyIdCopyAll = 0x5742;
        private const uint VK_C = 0x43;
        private const uint VK_D = 0x44;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOZORDER = 0x0004;
        private const int Margin = 0;
        private const int TopBarHeight = 64;
        private const int BottomBarHeight = 72;
        private const int SidePanelWidth = 380;

        private readonly IntPtr _emulatorHandle;
        private readonly AppSettings _settings;
        private readonly EmulatorWindowTracker _tracker;
        private readonly TopBarWindow _topBar;
        private readonly SidePanelWindow _sidePanel;
        private readonly BottomBarWindow _bottomBar;
        private readonly DispatcherTimer _zOrderTimer;
        private readonly DispatcherTimer _postRestoreTopmostTimer;
        private int _postRestoreTopmostTicksRemaining;
        private GameRegion _gameRegion;
        private CaptureController _captureController;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;
        private DateTime _lastDpiFailLog = DateTime.MinValue;

        // Dock scale comes from the EMULATOR's monitor, fresh every dock —
        // never the startup-cached topBar value. Under PerMonitorV2 this is
        // live truth (also self-heals cross-monitor drags).
        private void RefreshDockDpi()
        {
            uint dx = 0, dy = 0;
            IntPtr mon = IntPtr.Zero;
            int hr = -1;
            try
            {
                mon = MonitorFromWindow(_emulatorHandle, MONITOR_DEFAULTTONEAREST);
                if (mon != IntPtr.Zero) hr = GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out dx, out dy);
            }
            catch { }
            if (hr == 0 && dx >= 96 && dy >= 96)
            {
                double sx = dx / 96.0, sy = dy / 96.0;
                if (Math.Abs(sx - _dpiScaleX) > 0.001 || Math.Abs(sy - _dpiScaleY) > 0.001)
                    FileLogger.Log($"Dock DPI refreshed {_dpiScaleX:0.00}x{_dpiScaleY:0.00} -> {sx:0.00}x{sy:0.00}");
                _dpiScaleX = sx; _dpiScaleY = sy;
                return;
            }
            if (DateTime.UtcNow - _lastDpiFailLog > TimeSpan.FromSeconds(30))
            {
                _lastDpiFailLog = DateTime.UtcNow;
                FileLogger.Log($"Dock DPI API hr={hr} mon=0x{mon:X} dpi={dx}x{dy} — falling back to framework value.");
            }
            try
            {
                var dpi = VisualTreeHelper.GetDpi(_topBar);
                _dpiScaleX = dpi.DpiScaleX; _dpiScaleY = dpi.DpiScaleY;
            }
            catch { }
        }
        private IntPtr _topBarHandle;
        private IntPtr _sidePanelHandle;
        private IntPtr _bottomBarHandle;
        private bool? _lastShouldBeOnTop;
        private Window? _activePopup;

        // No OCR/translation should ever run against an uncalibrated
        // (i.e. whole-window, title-bar-and-all) region. Start() used to
        // start the capture loop immediately against the placeholder
        // region passed in from ConnectWindow, before the person had even
        // seen the calibration prompt - this flag gates capture so it can
        // only begin once a real, person-drawn region exists.
        private bool _hasCalibratedRegion;

        // Pop-out feed (revert: revert popout-feed). AppShell is the single
        // owner of this mode: it decides whether the bars exist, whether the
        // feed is repositioned/z-paired with the game, and where the feed sits.
        private bool _detached;
        private IntPtr _sidePanelPreDetachExStyle;
        private bool _hotkeysRegistered;
        private DateTime _lastFeedOpacityCheck = DateTime.MinValue;
        private readonly DeferredAction _feedPosSaveDefer;

        public AppShell(IntPtr emulatorHandle, GameRegion gameRegion, AppSettings settings)
        {
            _emulatorHandle = emulatorHandle;
            _gameRegion = gameRegion;
            _settings = settings;
            // Position save deferred until 1s after movement stops (revert:
            // revert feed-defer): DragMove pumps LocationChanged per pixel
            // and Save() re-encrypts the key + writes settings.json on the
            // UI thread - that was the live drag freeze.
            _feedPosSaveDefer = new DeferredAction(TimeSpan.FromSeconds(1), PersistFeedPosition);
            _topBar = new TopBarWindow();
            _sidePanel = new SidePanelWindow();
            _bottomBar = new BottomBarWindow();
            _bottomBar.PauseToggled += OnPauseToggled;
            _bottomBar.SettingsRequested += OnSettingsRequested;
            _topBar.ShotRequested += () => OnShotRequested(gameOnly: false);
            _topBar.ShotGameRequested += () => OnShotRequested(gameOnly: true);
            // Pop-out feed (revert: revert popout-feed): the detached panel
            // carries its own copies of the bar controls, so hiding the bars
            // never takes Pause/Settings/Shot away. Pause routes through
            // BottomBarWindow.TogglePause so the two buttons stay in sync.
            _sidePanel.PopOutRequested += () => SetDetached(true);
            _sidePanel.DockRequested += () => SetDetached(false);
            _sidePanel.PauseRequested += _bottomBar.TogglePause;
            _sidePanel.SettingsRequested += OnSettingsRequested;
            _sidePanel.ShotRequested += () => OnShotRequested(gameOnly: false);
            _bottomBar.PauseToggled += paused => _sidePanel.SetPauseState(paused);
            _sidePanel.LocationChanged += OnFeedLocationChanged;
            _sidePanel.SizeChanged += OnFeedSizeChanged;
            _tracker = new EmulatorWindowTracker(emulatorHandle);
            _tracker.BoundsChanged += OnBoundsChanged;
            _tracker.ForegroundChanged += OnForegroundChanged;
            _tracker.Minimized += OnEmulatorMinimized;
            _tracker.Restored += OnEmulatorRestored;
            // Teardown (vision batch: corpus stream + session fingerprint
            // only flush in Stop — previously never ran on app close,
            // silently truncating session .gz files. All closes funnel
            // here; controller.Stop is idempotent for repeats.
            _topBar.Closed += (_, _) => ShutdownCapture();
            _sidePanel.Closed += (_, _) =>
            {
                _feedPosSaveDefer.Flush();
                UnregisterDetachHotkeys();
                ShutdownCapture();
                // Taskbar feed (revert: revert taskbar-feed): popped out, the
                // feed carries the app's taskbar button - closing that button
                // must exit the way BottomBarWindow's close does, not strand
                // a hidden shell (the bars are down while detached). Docked
                // close keeps the old capture-stop-only semantics.
                if (_sidePanel.CloseShouldExit) Application.Current.Shutdown();
            };
            _bottomBar.Closed += (_, _) => ShutdownCapture();
            _captureController = BuildCaptureController();
            _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            // Dock affordance re-check rides the same tick (revert:
            // revert dock-hide): fullscreen coverage can flip without a
            // bounds event we catch.
            _zOrderTimer.Tick += (_, _) => { UpdateTopmost(); RefreshDockAffordance(); };
            _postRestoreTopmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _postRestoreTopmostTimer.Tick += (_, _) => PostRestoreTopmostTick();
        }

        private IntPtr GetActivePopupHandle()
        {
            if (_activePopup is null)
                return IntPtr.Zero;
            try
            {
                return new WindowInteropHelper(_activePopup).Handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private CaptureController BuildCaptureController()
        {
            var cache = new TranslationCache();
            // Hybrid: build DeepL whenever a key exists, regardless of the
            // selected provider - CAT-primary flagged lines fall back per-line.
            DeepLProvider? deepL = !string.IsNullOrWhiteSpace(_settings.DeepLApiKey)
                ? new DeepLProvider(_settings.DeepLApiKey!)
                : null;
            // W1 split (2026-10-17): offline translation lives in the
            // dedicated CAT process on :8001 (clean address space).
            // Revert: default endpoint.
            var offline = new OfflineTranslationProvider("http://127.0.0.1:8001");
            var providerPreference = _settings.TranslationProvider == "DeepL"
                ? TranslationProviderPreference.DeepL
                : TranslationProviderPreference.Offline;
            var translationManager = new TranslationManager(providerPreference, deepL, offline, cache);
            var ocrClient = new OcrClient();
            var controller = new CaptureController(_emulatorHandle, _gameRegion, ocrClient, translationManager,
                h => h != IntPtr.Zero && (h == _topBarHandle || h == _sidePanelHandle || h == _bottomBarHandle || h == GetActivePopupHandle()));
            controller.TextTranslated += (ja, en) => _sidePanel.ShowTranslation(ja, en);
            controller.MenuTranslated += (kind, ja, en) => _sidePanel.ShowMenu(kind, ja, en);
            controller.MenuUpdated += (oldJa, kind, ja, en) => _sidePanel.UpdateMenuIfMatches(oldJa, kind, ja, en);
            controller.BattleFramePublished += (topJa, topEn, monJa, monEn, actJa, actEn, rightJa, rightEn, itemJa, itemEn, weaponJa, weaponEn) => _sidePanel.ShowOrUpdateBattleFrame(topJa, topEn, monJa, monEn, actJa, actEn, rightJa, rightEn, itemJa, itemEn, weaponJa, weaponEn);
            controller.TextUpdated += (oldJa, ja, en) => _sidePanel.UpdateLastTranslationIfMatches(oldJa, ja, en);
            controller.WindowLost += OnCaptureWindowLost;
            // Terminal-state notice (2026-10-13): engine restarts stay
            // silent, but a SPENT budget must show on the panel — 30 min
            // of silent nothing reads as policy failure. Since 2026-10-23
            // the row is an error-code CHIP (compact, amber, clickable ->
            // what-happened/what-to-do popup; registry in
            // Diagnostics/ErrorCodes.cs owns all wording — EngineDown now
            // carries the CODE, role resolves to E2/E3 inside the
            // manager). ErrorCodeNotice posts the informational E1 (first
            // load-window death, retries running).
            // Revert: revert error-codes (EngineDown back to text + delete
            // ErrorCodeNotice subscription).
            Sidecar.SidecarProcessManager.EngineDown += code =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    _sidePanel.ShowError(code));
            Sidecar.SidecarProcessManager.ErrorCodeNotice += code =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    _sidePanel.ShowError(code));
            // Transport-death chip (item 2, 2026-10-30; revert:
            // revert transport-watchdog): E7's live line comes straight
            // from the capture loop's transport catch — WHEN it died +
            // WHAT caused it (endpoint error, count, sidecar PID,
            // restart status) — and a second fire refreshes the same
            // chip to 'recovered' once reads work again.
            controller.OcrTransportDied += live =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    _sidePanel.ShowError("E7", live));
            // Circuit-degraded chip (item 3, 2026-10-30; revert:
            // revert circuit-capture-continue): E8's live line carries
            // when the slowdown started + how many loops held + the
            // exact probe error + the reduced-cadence fact; a second
            // fire refreshes the same row to 'recovered'.
            controller.CircuitDegraded += live =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    _sidePanel.ShowError("E8", live));
            // Blank-frame chip (2026-11-11; revert: revert hdr-detect):
            // E9's live line carries when the flat streak started + how
            // many frames + hdr=on/off/unknown + the monitor name; a
            // second fire refreshes the same row to 'recovered'.
            controller.CaptureBlankFrames += live =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    _sidePanel.ShowError("E9", live));
            // Replay the last signal: it may have fired while nobody was
            // subscribed (subscriptions exist only from Connect onward),
            // and losing it would let a broken session start silently -
            // the exact failure class this system exists to kill.
            // Revert: revert error-codes.
            try
            {
                string? pending = Sidecar.SidecarProcessManager.PendingErrorSignal;
                if (pending != null)
                {
                    FileLogger.Log($"[error] replaying pending code {pending} at capture build");
                    _sidePanel.ShowError(pending);
                }
            }
            catch { }
            return controller;
        }

        // Fired when CaptureController gives up after repeatedly failing
        // to capture the emulator window and confirms the handle is
        // genuinely dead. Previously this failure mode just spun silently
        // in the background log forever with no visible sign anything was
        // wrong - translation would simply stop with no explanation.
        // 2026-10-23: the blocking MessageBox (focus steal, easy to miss
        // behind the game) is now the E4 chip — compact, amber, in the
        // feed where the user already looks; click opens the reconnect
        // steps. Revert: revert error-codes (restores the MessageBox).
        private void OnCaptureWindowLost()
        {
            FileLogger.Log("OnCaptureWindowLost: notifying and stopping.");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                _sidePanel.ShowError("E4"));
        }

        public void Start()
        {
            WindowCapture.FocusWindow(_emulatorHandle);
            // Pop-out feed: restore the stored position BEFORE the first show
            // so a returning user never sees the feed flash at 0,0.
            ApplyStoredFeedPosition();
            // Feed resize (revert: revert feed-resize): same pre-show
            // treatment as position - no natural-size flash on restart.
            if (_settings.FeedDetached) ApplyStoredFeedSize();
            _topBar.Show();
            _sidePanel.Show();
            _bottomBar.Show();
            _topBarHandle = new WindowInteropHelper(_topBar).Handle;
            _sidePanelHandle = new WindowInteropHelper(_sidePanel).Handle;
            _bottomBarHandle = new WindowInteropHelper(_bottomBar).Handle;
            FileLogger.Log(
                $"AppShell.Start: emulator=0x{_emulatorHandle:X}, topBar=0x{_topBarHandle:X}, " +
                $"sidePanel=0x{_sidePanelHandle:X}, bottomBar=0x{_bottomBarHandle:X}");
            var dpi = VisualTreeHelper.GetDpi(_topBar);
            _dpiScaleX = dpi.DpiScaleX;
            _dpiScaleY = dpi.DpiScaleY;
            _sidePanel.MenuDisplayMode = _settings.MenuDisplayMode;
            // Pop-out feed (revert: revert popout-feed): restore last mode
            // while the handles are known - the ex-style flip and the hotkey
            // registration both need _sidePanelHandle, and ForceTopmost below
            // must already be looking at the right mode rather than pairing
            // a feed that is about to be demoted out of the topmost band.
            if (_settings.FeedDetached)
                SetDetached(true, persist: false);
            RegisterDetachHotkeys();
            ForceTopmost("Start");
            BeginPostRestoreTopmostBurst();
            _tracker.Start();
            // Deliberately NOT starting _captureController here - it's
            // still holding the whole-window placeholder region at this
            // point (see ConnectWindow), and OCR must never run against
            // that. Capture only begins once RunCalibration succeeds, below.
            _zOrderTimer.Start();
        }

        private void UpdateTopmost()
        {
            IntPtr foreground = WindowCapture.GetForegroundWindowHandle();
            IntPtr popupHandle = GetActivePopupHandle();
            bool shouldBeOnTop =
                foreground == _emulatorHandle ||
                foreground == _topBarHandle ||
                foreground == _sidePanelHandle ||
                foreground == _bottomBarHandle ||
                (popupHandle != IntPtr.Zero && foreground == popupHandle);
            if (_lastShouldBeOnTop != shouldBeOnTop)
            {
                FileLogger.Log(
                    $"UpdateTopmost: transition {_lastShouldBeOnTop} -> {shouldBeOnTop}, " +
                    $"foreground=0x{foreground:X}");
                _lastShouldBeOnTop = shouldBeOnTop;
            }
            // Z-order pairing: every bar sits directly above the emulator, so the
            // four windows travel as one depth. Covered together, restored together.
            // Batched in one DeferWindowPos so all three land the same frame.
            PairGroupAboveEmulator();
            // Popup untouched: topmost band owns it (see TrackPopup).
        }

        private void PairGroupAboveEmulator()
        {
            if (_detached)
            {
                // The detached feed lives in the TOPMOST band, not in the
                // game's z-order neighborhood. Pairing it above the emulator
                // (SetWindowPos with a non-topmost insert-after) would drop it
                // back out of that band - straight under whatever is in front
                // of the game, which is the exact case the feed exists for.
                if (_sidePanelHandle != IntPtr.Zero)
                    SetTopmostLogged(_sidePanelHandle, "sidePanel(detached)");
                return;
            }
            if (_topBarHandle == IntPtr.Zero || _sidePanelHandle == IntPtr.Zero || _bottomBarHandle == IntPtr.Zero)
            {
                PairAboveEmulator(_topBarHandle, "topBar");
                PairAboveEmulator(_sidePanelHandle, "sidePanel");
                PairAboveEmulator(_bottomBarHandle, "bottomBar");
                return;
            }
            IntPtr hdwp = BeginDeferWindowPos(3);
            if (hdwp != IntPtr.Zero)
                hdwp = DeferWindowPos(hdwp, _topBarHandle, _emulatorHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (hdwp != IntPtr.Zero)
                hdwp = DeferWindowPos(hdwp, _sidePanelHandle, _emulatorHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (hdwp != IntPtr.Zero)
                hdwp = DeferWindowPos(hdwp, _bottomBarHandle, _emulatorHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (hdwp != IntPtr.Zero)
            {
                EndDeferWindowPos(hdwp);
            }
            else
            {
                if (DateTime.UtcNow - _lastPairFailLog > TimeSpan.FromSeconds(30))
                {
                    _lastPairFailLog = DateTime.UtcNow;
                    FileLogger.Log("PairGroupAboveEmulator: batch failed, falling back to individual calls.");
                }
                PairAboveEmulator(_topBarHandle, "topBar");
                PairAboveEmulator(_sidePanelHandle, "sidePanel");
                PairAboveEmulator(_bottomBarHandle, "bottomBar");
            }
        }

        private DateTime _lastPairFailLog = DateTime.MinValue;

        private void PairAboveEmulator(IntPtr handle, string name)
        {
            if (handle == IntPtr.Zero || handle == _emulatorHandle)
                return;
            bool ok = SetWindowPos(handle, _emulatorHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 0 && DateTime.UtcNow - _lastPairFailLog > TimeSpan.FromSeconds(30))
                {
                    _lastPairFailLog = DateTime.UtcNow;
                    FileLogger.Log($"SetWindowPos FAILED (pair above emulator) for {name} (0x{handle:X}): Win32 error {err}");
                }
            }
        }

        private void SetTopmostLogged(IntPtr handle, string name)
        {
            bool ok = SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                FileLogger.Log($"SetWindowPos FAILED (topmost) for {name} (0x{handle:X}): Win32 error {err}");
            }
        }

        private void SetNotTopmostLogged(IntPtr handle, string name, IntPtr foreground)
        {
            // Explicitly insert this panel behind whatever window just took
            // focus, rather than only stripping the topmost flag. Stripping
            // the flag alone doesn't guarantee the window drops behind a
            // specific normal window - it just leaves it at the top of the
            // non-topmost band, which is exactly why the emulator (a normal
            // window Windows manages on its own) would correctly fall behind
            // a newly-focused app while our panels stayed floating above it.
            bool ok = SetWindowPos(handle, foreground, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (ok)
                return;

            int err = Marshal.GetLastWin32Error();
            FileLogger.Log(
                $"SetWindowPos FAILED (insert behind foreground 0x{foreground:X}) for {name} (0x{handle:X}): " +
                $"Win32 error {err} - falling back to HWND_NOTOPMOST");

            // Fallback for the case this approach was originally removed
            // for: transient shell/DWM-owned overlays (Alt-Tab switcher,
            // Snap Layouts) run at a different privilege level, and
            // Windows' UIPI blocks z-order changes against those specific
            // windows. HWND_NOTOPMOST is always safe since it's a constant,
            // not another process's handle - it just doesn't guarantee
            // dropping behind a *specific* normal window, which is why we
            // try the direct approach first and only fall back here.
            bool fallbackOk = SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (!fallbackOk)
            {
                int fallbackErr = Marshal.GetLastWin32Error();
                FileLogger.Log($"SetWindowPos FAILED (fallback notopmost) for {name} (0x{handle:X}): Win32 error {fallbackErr}");
            }
        }

        private void ForceTopmost(string reason)
        {
            FileLogger.Log($"ForceTopmost ({reason})");
            if (_detached)
            {
                // Only the feed is on screen; it owns the topmost band.
                if (_sidePanelHandle != IntPtr.Zero)
                    SetTopmostLogged(_sidePanelHandle, "sidePanel(detached)");
                return;
            }
            PairAboveEmulator(_topBarHandle, "topBar");
            PairAboveEmulator(_sidePanelHandle, "sidePanel");
            PairAboveEmulator(_bottomBarHandle, "bottomBar");
            // Popup untouched: topmost band owns it (see TrackPopup).
        }

        private void BeginPostRestoreTopmostBurst()
        {
            _postRestoreTopmostTicksRemaining = 10;
            _postRestoreTopmostTimer.Start();
        }

        private void PostRestoreTopmostTick()
        {
            ForceTopmost("PostRestoreBurst");
            _postRestoreTopmostTicksRemaining--;
            if (_postRestoreTopmostTicksRemaining <= 0)
                _postRestoreTopmostTimer.Stop();
        }

        private void OnEmulatorMinimized()
        {
            // Pop-out feed: a detached feed is no longer part of the game's
            // chrome - it survives the game minimizing (the pipeline holds on
            // its own via the IsMinimized gate, and the feed has nothing to
            // show anyway until the game is back). Docked = today's behavior.
            FileLogger.Log($"OnEmulatorMinimized: hiding chrome (detached={_detached})");
            _topBar.Hide();
            _bottomBar.Hide();
            if (!_detached) _sidePanel.Hide();
            _activePopup?.Hide();
        }

        private void OnEmulatorRestored()
        {
            if (_detached)
            {
                FileLogger.Log("OnEmulatorRestored: feed was detached - bars stay hidden");
                _topBar.Hide();
                _bottomBar.Hide();
                _activePopup?.Show();
                if (_sidePanelHandle != IntPtr.Zero)
                    SetTopmostLogged(_sidePanelHandle, "sidePanel(restored-detached)");
                return;
            }
            FileLogger.Log("OnEmulatorRestored: showing all bars");
            _topBar.Show();
            _sidePanel.Show();
            _bottomBar.Show();
            _activePopup?.Show();
            ForceTopmost("OnEmulatorRestored");
            BeginPostRestoreTopmostBurst();
        }

        // TEMP troubleshooting aid: snapshot emulator + panels with timestamps,
        // no alt-tab needed. Remove post-Zelda.
        private void OnShotRequested(bool gameOnly)
        {
            // Diagnostics-gated: snapshots are dev artifacts (customers
            // accumulate nothing). Whole handler skipped when off.
            if (!Settings.Diag.Enabled) return;
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WunderLanguage", "troubleshoot");
                System.IO.Directory.CreateDirectory(dir);
                // Session labeling (agreed 2026-10-13): timestamp alone
                // can't tell sessions apart. Prefix the pack id so bundles
                // sort by game (ffvi_*, papermario_*, zelda_*); header logs
                // game+pack too. New files only, old bundles untouched.
                // Revert: drop the prefix + header line.
                string packTag = "unknown";
                try { packTag = Packs.PackRegistry.Current.GameId ?? "unknown"; } catch { }
                string stamp = packTag + "_" + DateTime.Now.ToString("HHmmss_fff");
                // Single stitched shot: bounding box over game region + all panels,
                // or game region only when gameOnly (Shot game button).
                // Revert: revert shot-game-button.
                var rects = new System.Collections.Generic.List<System.Drawing.Rectangle>();
                System.Drawing.Rectangle gameRect = System.Drawing.Rectangle.Empty;
                if (WindowCapture.TryGetWindowBounds(_emulatorHandle, out var winBounds))
                {
                    var rel = _gameRegion.GetScaledCropRect(winBounds);
                    gameRect = new System.Drawing.Rectangle(winBounds.Left + rel.Left, winBounds.Top + rel.Top, rel.Width, rel.Height);
                    rects.Add(gameRect);
                }
                string suffix = "_all";
                if (gameOnly)
                {
                    suffix = "_game";
                    rects.Clear();
                    if (gameRect != System.Drawing.Rectangle.Empty) rects.Add(gameRect);
                }
                else
                {
                foreach (var handle in new[] { _topBarHandle, _sidePanelHandle, _bottomBarHandle })
                {
                    try
                    {
                        if (WindowCapture.TryGetWindowBounds(handle, out var r) && r.Width > 0 && r.Height > 0)
                            rects.Add(new System.Drawing.Rectangle(r.Left, r.Top, r.Width, r.Height));
                    }
                    catch { }
                }
                }
                if (rects.Count > 0)
                {
                    int l = int.MaxValue, t = int.MaxValue, r2 = int.MinValue, b2 = int.MinValue;
                    foreach (var r in rects)
                    {
                        if (r.Left < l) l = r.Left;
                        if (r.Top < t) t = r.Top;
                        if (r.Right > r2) r2 = r.Right;
                        if (r.Bottom > b2) b2 = r.Bottom;
                    }
                    using var bmp = WindowCapture.CaptureScreenRegion(new System.Drawing.Rectangle(l, t, r2 - l, b2 - t));
                    bmp.Save(System.IO.Path.Combine(dir, $"{stamp}{suffix}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                try
                {
                    var keep = new System.Text.RegularExpressions.Regex(@"\[mine\]|\[confusion\]|\[words\]|POST /observe|breakthrough|word guide|suppress-contentless|title-guard|STABLE-DECISION|refocus-catchup|\[shot\]|\[translate-provider\]|\[translate\]|\[menu\]|translate-fire|\[blocks\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var lines = System.IO.File.ReadAllLines(FileLogger.Path_);
                    var tail = new System.Collections.Generic.List<string>();
                    foreach (var ln in lines)
                    {
                        try { if (keep.IsMatch(ln)) tail.Add(ln); } catch { }
                    }
                    int take = tail.Count > 150 ? 150 : tail.Count;
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"# WunderLanguage troubleshoot {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({stamp})");
                    try { sb.AppendLine($"# pack={Packs.PackRegistry.Current.GameId}"); } catch { }
                    for (int i = tail.Count - take; i < tail.Count; i++) sb.AppendLine(tail[i]);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"{stamp}_log.txt"), sb.ToString());
                }
                catch (Exception ex)
                {
                    FileLogger.Log($"[shot] log dump failed: {ex.Message}");
                }
                FileLogger.Log($"[shot] saved {stamp} to {dir}");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[shot] failed: {ex.Message}");
            }
        }

        // Instant z-pairing on focus change: closes the ms gap between the
        // clicked window and the bars that a 150ms tick alone can't.
        private void OnForegroundChanged(IntPtr fgHwnd)
        {
            try
            {
                IntPtr popupHandle = GetActivePopupHandle();
                if (fgHwnd == _emulatorHandle || fgHwnd == _topBarHandle
                    || fgHwnd == _sidePanelHandle || fgHwnd == _bottomBarHandle
                    || (popupHandle != IntPtr.Zero && fgHwnd == popupHandle))
                    PairGroupAboveEmulator();
            }
            catch { }
        }

        private void OnPauseToggled(bool paused)
        {
            if (paused)
                _captureController.Stop();
            else if (_hasCalibratedRegion)
                _captureController.Start();
        }

        private bool _captureStopped;
        private void ShutdownCapture()
        {
            if (_captureStopped) return;
            _captureStopped = true;
            try { _captureController.Stop(); } catch { }
        }

        private void OnSettingsRequested()
        {
            var popup = new SettingsPopup(_settings);
            popup.RecalibrateClicked += () => RunCalibration(showIntroPrompt: false);
            TrackPopup(popup);
            popup.ShowDialog();
            // Provider/key may have changed - rebuild so DeepL <-> Offline
            // takes effect immediately without recalibrating.
            if (_hasCalibratedRegion)
            {
                if (_settings.TranslationProvider == "DeepL" && string.IsNullOrWhiteSpace(_settings.DeepLApiKey))
                {
                    FileLogger.Log("Settings closed: DeepL selected with no key - keeping current pipeline.");
                    System.Windows.MessageBox.Show(
                        "DeepL needs an API key. Enter one or switch to Offline.",
                        "WunderLanguage");
                    return;
                }
                FileLogger.Log($"Settings closed: rebuilding pipeline provider={_settings.TranslationProvider}, keyPresent={!string.IsNullOrWhiteSpace(_settings.DeepLApiKey)}");
                _captureController.Stop();
                _captureController = BuildCaptureController();
                _sidePanel.MenuDisplayMode = _settings.MenuDisplayMode;
                _captureController.Start();
            }
        }

        // Keeps a currently-open popup (settings, calibration, the intro
        // prompt) synced with the emulator's minimize/restore state. From
        // the person's perspective this whole app - panels and any open
        // popup - should behave as one thing attached to the emulator:
        // minimize the emulator, everything tied to it minimizes; restore
        // it, everything comes back together, popup included.
        private void TrackPopup(Window popup)
        {
            // Layer +1 via ownership: popup owned by the emulator sits above it,
            // minimizes/covers with it, never topmost-band, never re-asserted.
            // (Untracked ConnectWindow settings is owned by Connect instead.)
            popup.Topmost = false;
            _activePopup = popup;
            try
            {
                // Handle is Zero until first show - without EnsureHandle the
                // owner was never set and the popup spawned behind the
                // emulator (its XAML Topmost was already stripped above).
                IntPtr ph = new WindowInteropHelper(popup).EnsureHandle();
                if (ph != IntPtr.Zero && ph != _emulatorHandle)
                {
                    SetWindowOwner(ph, _emulatorHandle);
                    SetWindowPos(ph, _sidePanelHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"TrackPopup owner failed: {ex.Message}");
            }
            popup.Closed += (_, _) =>
            {
                if (_activePopup == popup)
                    _activePopup = null;
            };
        }

        // Public entry point used two ways: right after connecting (with the
        // explanatory intro prompt, since the panels are freshly attached
        // and this is the person's first time seeing this step), and from
        // Settings -> Recalibrate later (skipping the intro, since they
        // already know what this does by then).
        public void RunCalibration(bool showIntroPrompt)
        {
            // Companion bars bury the calibration overlay wherever they
            // overlap it (their 150ms z-pairing beats its neutered timer),
            // making the window corners under them unreachable. Hide the bars
            // for the whole flow; restore after, honoring a minimized
            // emulator. try/finally so cancel paths restore too.
            bool barsHidden = false;
            try
            {
                _topBar.Hide(); _sidePanel.Hide(); _bottomBar.Hide();
                barsHidden = true;
                if (showIntroPrompt)
            {
                var prompt = new GameRegionPromptWindow();
                TrackPopup(prompt);
                bool? promptResult = prompt.ShowDialog();
                if (promptResult != true)
                    return;
            }

            if (!WindowCapture.TryGetWindowBounds(_emulatorHandle, out var currentWindowBounds))
                return;

            var calibration = new CalibrationWindow();
            TrackPopup(calibration);
            bool? result = calibration.ShowDialog();
            if (result != true || calibration.CalibratedPhysicalBounds is null)
                return;

            _gameRegion = new GameRegion(currentWindowBounds, calibration.CalibratedPhysicalBounds.Value);

            _captureController.Stop();
            _captureController = BuildCaptureController();
            _hasCalibratedRegion = true;
            _sidePanel.ClearHistory();
            _captureController.Start();

            if (WindowCapture.TryGetWindowBounds(_emulatorHandle, out var bounds))
                OnBoundsChanged(bounds);
            }
            finally
            {
                if (barsHidden && !WindowCapture.IsMinimized(_emulatorHandle))
                {
                    // The feed always comes back (it was hidden only so it
                    // couldn't bury the calibration corners); the bars only
                    // come back when they were part of the layout to begin
                    // with. Pop-out feed: revert popout-feed.
                    _sidePanel.Show();
                    if (!_detached)
                    {
                        _topBar.Show();
                        _bottomBar.Show();
                    }
                }
            }
        }

        private void OnBoundsChanged(Rectangle windowBounds)
        {
            RefreshDockDpi();
            int topBarHeightPx = (int)Math.Round(TopBarHeight * _dpiScaleY);
            int bottomBarHeightPx = (int)Math.Round(BottomBarHeight * _dpiScaleY);
            int sidePanelWidthPx = (int)Math.Round(SidePanelWidth * _dpiScaleX);
            int marginPx = (int)Math.Round(Margin * _dpiScaleX);
            const int ShadowCompensationDip = 3;
            int shadowCompensationPxX = Math.Max(0, (int)Math.Round(ShadowCompensationDip * _dpiScaleX) - 1);
            int shadowCompensationPxY = Math.Max(0, (int)Math.Round(ShadowCompensationDip * _dpiScaleY) - 1);
            int left = windowBounds.Left;
            int top = windowBounds.Top;
            int right = windowBounds.Right;
            int bottom = windowBounds.Bottom;
            int width = right - left;
            int height = bottom - top;
            int topBarTop = top - topBarHeightPx - marginPx + shadowCompensationPxY;
            int bottomBarBottom = bottom + marginPx - shadowCompensationPxY + bottomBarHeightPx;
            FileLogger.Log(
                $"OnBoundsChanged: windowBounds=(L{windowBounds.Left},T{windowBounds.Top},R{windowBounds.Right},B{windowBounds.Bottom}) " +
                $"derived left={left} top={top} right={right} bottom={bottom} width={width} height={height} " +
                $"dpiX={_dpiScaleX} dpiY={_dpiScaleY}");
            // Seam diagnostic (logging only): live shadow insets per edge so
            // the overlap amount is measured, never guessed again.
            try
            {
                if (WindowCapture.TryGetUserRect(_emulatorHandle, out var userEmu))
                    FileLogger.Log($"  seam shadow T/B/L/R={windowBounds.Top - userEmu.Top}/{userEmu.Bottom - windowBounds.Bottom}/{windowBounds.Left - userEmu.Left}/{userEmu.Right - windowBounds.Right}");
            }
            catch { }
            IntPtr hdwp = BeginDeferWindowPos(3);
            if (hdwp != IntPtr.Zero)
            {
                hdwp = DeferWindowPos(hdwp, _topBarHandle, IntPtr.Zero,
                    left, topBarTop, width, topBarHeightPx,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }
            if (hdwp != IntPtr.Zero)
            {
                // Pop-out feed: a detached feed is parked by the person, not by
                // the game window - re-snapping it to the game's right edge on
                // every bounds change would undo the whole feature. The bars
                // keep tracking (hidden, but positioned correctly the moment
                // they re-show).
                if (!_detached)
                    hdwp = DeferWindowPos(hdwp, _sidePanelHandle, IntPtr.Zero,
                        right + marginPx - shadowCompensationPxX, topBarTop, sidePanelWidthPx,
                        bottomBarBottom - topBarTop,
                        SWP_NOZORDER | SWP_NOACTIVATE);
            }
            if (hdwp != IntPtr.Zero)
            {
                hdwp = DeferWindowPos(hdwp, _bottomBarHandle, IntPtr.Zero,
                    left, bottom + marginPx - shadowCompensationPxY, width, bottomBarHeightPx,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }
            if (hdwp != IntPtr.Zero)
            {
                EndDeferWindowPos(hdwp);
            }
            else
            {
                FileLogger.Log("OnBoundsChanged: DeferWindowPos batch failed, falling back to individual SetWindowPos calls.");
                SetWindowPos(_topBarHandle, IntPtr.Zero,
                    left, topBarTop, width, topBarHeightPx,
                    SWP_NOZORDER | SWP_NOACTIVATE);
                if (!_detached)
                    SetWindowPos(_sidePanelHandle, IntPtr.Zero,
                        right + marginPx - shadowCompensationPxX, topBarTop, sidePanelWidthPx,
                        bottomBarBottom - topBarTop,
                        SWP_NOZORDER | SWP_NOACTIVATE);
                SetWindowPos(_bottomBarHandle, IntPtr.Zero,
                    left, bottom + marginPx - shadowCompensationPxY, width, bottomBarHeightPx,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }
            if (WindowCapture.TryGetWindowBounds(_topBarHandle, out var actualTopBar))
                FileLogger.Log($"  actual topBar rect: (L{actualTopBar.Left},T{actualTopBar.Top},R{actualTopBar.Right},B{actualTopBar.Bottom}) size={actualTopBar.Width}x{actualTopBar.Height}");
            if (WindowCapture.TryGetWindowBounds(_sidePanelHandle, out var actualSidePanel))
                FileLogger.Log($"  actual sidePanel rect: (L{actualSidePanel.Left},T{actualSidePanel.Top},R{actualSidePanel.Right},B{actualSidePanel.Bottom}) size={actualSidePanel.Width}x{actualSidePanel.Height}");
            if (WindowCapture.TryGetWindowBounds(_bottomBarHandle, out var actualBottomBar))
                FileLogger.Log($"  actual bottomBar rect: (L{actualBottomBar.Left},T{actualBottomBar.Top},R{actualBottomBar.Right},B{actualBottomBar.Bottom}) size={actualBottomBar.Width}x{actualBottomBar.Height}");
            if (_detached)
                RefreshFeedOpacity();
            RefreshDockAffordance();
        }

        // Dock affordance visibility (revert: revert dock-hide): while the
        // game covers its whole monitor, the Dock action would drop the bars
        // over a fullscreen game (the reported bug) - the panel hides the
        // affordances, "Pop out" always stays. Re-evaluated on every bounds
        // change AND on the 150ms tick: a mode switch or monitor re-plug can
        // flip coverage without a bounds event we catch, and the cost is two
        // Win32 calls. Coverage reads the OUTER rect (TryGetUserRect, the
        // SetWindowPos coordinate space): outer ⊇ visible, so a frameless
        // fullscreen game reports the monitor exactly and a thickframe
        // window that visually covers (maximized) still counts - the DWM
        // frame excludes the 8px invisible border WPF windows carry.
        private bool _gameCoversMonitor;
        private void RefreshDockAffordance()
        {
            try
            {
                bool covered = false;
                System.Drawing.Rectangle game = default, mon = default;
                if (_emulatorHandle != IntPtr.Zero &&
                    WindowCapture.TryGetUserRect(_emulatorHandle, out game) &&
                    WindowCapture.TryGetMonitorBounds(_emulatorHandle, out mon))
                {
                    covered = WindowCapture.GameCoversMonitor(game, mon);
                }
                if (covered != _gameCoversMonitor)
                {
                    _gameCoversMonitor = covered;
                    FileLogger.Log($"dockvis: game covers its monitor -> {covered} (game={game} monitor={mon})");
                }
                _sidePanel.SetGameCoversMonitor(covered);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"dockvis: check failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Pop-out feed (revert: revert popout-feed).
        //
        // Why it exists: exclusive/borderless fullscreen claims its monitor,
        // and the top/bottom bars get pushed off-screen while the feed lands
        // on the game's own edge. Detaching parks the feed as a standalone
        // window - typically a second monitor - that the fullscreen game has
        // no claim over.
        //
        // Three properties make it survive that case:
        //   1. HWND_TOPMOST (the band is global across monitors; nothing but
        //      another topmost window covers it, and the 150ms z-pairing is
        //      deliberately skipped so it can't be demoted back into the
        //      game's z-order neighbourhood).
        //   2. WS_EX_NOACTIVATE - clicking/scrolling the feed never takes the
        //      foreground away from the game, so games that minimize or pause
        //      on focus loss never notice.
        //   3. Capture keeps running off its own screen-BitBlt of the game's
        //      rect, which a feed on another monitor can't intersect.
        // ------------------------------------------------------------------

        private void ToggleDetach() => SetDetached(!_detached);

        private void SetDetached(bool detached, bool persist = true)
        {
            if (_detached == detached) return;
            _detached = detached;
            try
            {
                if (detached)
                {
                    _topBar.Hide();
                    _bottomBar.Hide();
                    ApplyDetachedPanelStyle();
                    _sidePanel.SetDetached(true);
                    // Feed resize (revert: revert feed-resize): every pop-out
                    // re-applies the stored size - docking force-resized the
                    // HWND to the dock rect along the way.
                    ApplyStoredFeedSize();
                    if (_sidePanelHandle != IntPtr.Zero)
                        SetTopmostLogged(_sidePanelHandle, "sidePanel(detach)");
                    RegisterCopyHotkey();
                    FileLogger.Log(
                        $"popout: feed DETACHED at ({_sidePanel.Left},{_sidePanel.Top}) - " +
                        "bars hidden, feed HWND_TOPMOST + WS_EX_NOACTIVATE");
                }
                else
                {
                    RestoreDockedPanelStyle();
                    _sidePanel.SetDetached(false);
                    UnregisterCopyHotkey();
                    _topBar.Show();
                    _bottomBar.Show();
                    // Re-snap now instead of waiting for a bounds event that may
                    // never come - the game window hasn't moved, so nothing is
                    // going to fire one on its own.
                    if (WindowCapture.TryGetWindowBounds(_emulatorHandle, out var bounds))
                        OnBoundsChanged(bounds);
                    PairGroupAboveEmulator();
                    FileLogger.Log("popout: feed DOCKED - bars restored, feed re-paired to game edge");
                }
                RefreshFeedOpacity();
                if (persist)
                {
                    _settings.FeedDetached = _detached;
                    _settings.FeedLeft = _sidePanel.Left;
                    _settings.FeedTop = _sidePanel.Top;
                    _settings.Save();
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: SetDetached({detached}) failed: {ex.Message}");
            }
        }

        // Read/modify/write so the flag rides alongside whatever else lives in
        // the ex-style; the restore only ever clears the bit we added.
        private void ApplyDetachedPanelStyle()
        {
            if (_sidePanelHandle == IntPtr.Zero) return;
            try
            {
                long before = GetExtendedStyle(_sidePanelHandle).ToInt64();
                _sidePanelPreDetachExStyle = new IntPtr(before);
                long after = before | WS_EX_NOACTIVATE;
                SetExtendedStyle(_sidePanelHandle, new IntPtr(after));
                FileLogger.Log($"popout: feed ex-style 0x{before:X} -> 0x{after:X} (WS_EX_NOACTIVATE)");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: WS_EX_NOACTIVATE set failed: {ex.Message}");
            }
        }

        private void RestoreDockedPanelStyle()
        {
            if (_sidePanelHandle == IntPtr.Zero) return;
            try
            {
                long now = GetExtendedStyle(_sidePanelHandle).ToInt64();
                long after = now & ~(long)WS_EX_NOACTIVATE;
                if (_sidePanelPreDetachExStyle != IntPtr.Zero)
                    after = _sidePanelPreDetachExStyle.ToInt64();
                SetExtendedStyle(_sidePanelHandle, new IntPtr(after));
                FileLogger.Log($"popout: feed ex-style 0x{now:X} -> 0x{after:X} (WS_EX_NOACTIVATE cleared)");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: WS_EX_NOACTIVATE clear failed: {ex.Message}");
            }
        }

        // Restore last-run geometry before the first Show so a returning user
        // never sees the feed flash at its default spot. 0,0 means "never
        // positioned" (the feed simply keeps WPF's cascade position).
        private void ApplyStoredFeedPosition()
        {
            try
            {
                if (_settings.FeedLeft == 0 && _settings.FeedTop == 0) return;
                _sidePanel.Left = _settings.FeedLeft;
                _sidePanel.Top = _settings.FeedTop;
                ClampToVirtualScreen();
                FileLogger.Log($"popout: restored feed position ({_sidePanel.Left},{_sidePanel.Top})");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: feed position restore failed: {ex.Message}");
            }
        }

        // Feed resize (revert: revert feed-resize): restore the last
        // user-chosen feed size on pop-out/startup. 0 = never resized ->
        // keep the natural size. DIP, same units as FeedLeft/FeedTop;
        // the 340x220 floor mirrors the XAML MinWidth/MinHeight.
        private void ApplyStoredFeedSize()
        {
            try
            {
                if (_settings.FeedWidth < 340 || _settings.FeedHeight < 220) return;
                _sidePanel.Width = _settings.FeedWidth;
                _sidePanel.Height = _settings.FeedHeight;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: feed size restore failed: {ex.Message}");
            }
        }

        // A monitor that's since been unplugged must not strand the feed where
        // it can't be grabbed again. SystemParameters.VirtualScreen* is DIP
        // divided by the STALE system DPI (144 ghost): (0,0,2986.7,960)
        // instead of the true (0,0,4480,1440) — the clamp drew its walls
        // ~20% inside the real screen. Raw PMv2 metrics ÷ the feed's OWN
        // measured scale (same measure-don't-report rule as Confirm()),
        // dock scale as fallback. Revert: revert calib-cover.
        private void ClampToVirtualScreen()
        {
            double sx = _dpiScaleX, sy = _dpiScaleY;
            try
            {
                if (_sidePanelHandle != IntPtr.Zero && _sidePanel.ActualWidth > 10 && _sidePanel.ActualHeight > 10 &&
                    WindowCapture.TryGetWindowBounds(_sidePanelHandle, out var fp) && fp.Width > 10 && fp.Height > 10)
                {
                    double mx = (double)fp.Width / _sidePanel.ActualWidth;
                    double my = (double)fp.Height / _sidePanel.ActualHeight;
                    if (mx > 0.5 && mx < 3.0 && my > 0.5 && my < 3.0) { sx = mx; sy = my; }
                }
            }
            catch { }
            var vs = WindowCapture.GetVirtualScreenRect();
            double vw = vs.Left / sx;
            double vt = vs.Top / sy;
            double vr = vw + vs.Width / sx;
            double vb = vt + vs.Height / sy;
            double w = _sidePanel.ActualWidth > 0 ? _sidePanel.ActualWidth : SidePanelWidth;
            double h = _sidePanel.ActualHeight > 0 ? _sidePanel.ActualHeight : 420;
            if (_sidePanel.Left + w < vw) _sidePanel.Left = vw;
            if (_sidePanel.Top + h < vt) _sidePanel.Top = vt;
            if (_sidePanel.Left > vr - 40) _sidePanel.Left = vr - 40;
            if (_sidePanel.Top > vb - 40) _sidePanel.Top = vb - 40;
        }

        // Detached = the feed may be sitting on the game itself (single monitor)
        // or on its own monitor. Over the game it drops to partial opacity so
        // text reads through it; on its own monitor it's fully opaque. Both
        // rects come from the same Win32 call so the units always agree.
        // Revert: revert popout-feed.
        private void RefreshFeedOpacity()
        {
            try
            {
                if (!_detached)
                {
                    if (Math.Abs(_sidePanel.Opacity - 1.0) > 0.01) _sidePanel.Opacity = 1.0;
                    return;
                }
                if (_sidePanelHandle == IntPtr.Zero) return;
                bool overGame =
                    WindowCapture.TryGetWindowBounds(_sidePanelHandle, out var feedRect) &&
                    WindowCapture.TryGetWindowBounds(_emulatorHandle, out var gameRect) &&
                    feedRect.IntersectsWith(gameRect);
                double wanted = overGame ? 0.75 : 1.0;
                if (Math.Abs(_sidePanel.Opacity - wanted) > 0.01)
                {
                    _sidePanel.Opacity = wanted;
                    FileLogger.Log($"popout: feed opacity -> {wanted} (overlaps game={overGame})");
                }
            }
            catch { }
        }

        // DragMove pumps a LocationChanged per pixel of travel, so both
        // chores here are throttled to keep the drag pump cheap (the live
        // drag freeze: 2 DWM calls + a DPAPI/2-write settings save per
        // pixel). Opacity at most ~4Hz; the position SAVE is deferred until
        // 1s after movement stops - the fields below update per pixel for
        // free, so any Save() from elsewhere still persists fresh coords.
        // Revert: revert feed-defer.
        private void OnFeedLocationChanged(object? sender, EventArgs e)
        {
            if (!_detached) return;
            _settings.FeedLeft = _sidePanel.Left;
            _settings.FeedTop = _sidePanel.Top;
            if ((DateTime.UtcNow - _lastFeedOpacityCheck) >= TimeSpan.FromMilliseconds(250))
            {
                _lastFeedOpacityCheck = DateTime.UtcNow;
                RefreshFeedOpacity();
            }
            _feedPosSaveDefer.Schedule();
        }

        // The deferred half of the position save: runs once, 1s after
        // movement stops (or on feed close via Flush). Revert: revert feed-defer.
        private void PersistFeedPosition()
        {
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: feed position save failed: {ex.Message}");
            }
        }

        // Persist the detached feed's user resize (revert: revert
        // feed-resize): docked resizes are programmatic (OnBoundsChanged
        // SetWindowPos against the game rect) and are guarded out by
        // _detached; the save rides the same 1s debounce as the position.
        private void OnFeedSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (!_detached) return;
            _settings.FeedWidth = e.NewSize.Width;
            _settings.FeedHeight = e.NewSize.Height;
            _feedPosSaveDefer.Schedule();
        }

        // Ctrl+Shift+D is the only trigger that works when a fullscreen game
        // owns the foreground and the feed is out of reach on another monitor.
        // MOD_NOREPEAT so a held combo is exactly one toggle. Failure is
        // fail-open: the button still works, we just log it.
        private void RegisterDetachHotkeys()
        {
            if (_hotkeysRegistered) return;
            if (_sidePanelHandle == IntPtr.Zero) return;
            try
            {
                var src = HwndSource.FromHwnd(_sidePanelHandle);
                if (src == null)
                {
                    FileLogger.Log("popout: hotkey registration skipped (no HwndSource for sidePanel)");
                    return;
                }
                src.AddHook(WndProc);
                _hotkeysRegistered = true;
                if (RegisterHotKey(_sidePanelHandle, HotkeyIdToggleDetach, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_D))
                    FileLogger.Log("popout: hotkey Ctrl+Shift+D registered");
                else
                    FileLogger.Log($"popout: hotkey Ctrl+Shift+D FAILED (Win32 {Marshal.GetLastWin32Error()}) - pop-out button only");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: hotkey registration failed: {ex.Message}");
            }
        }

        private void UnregisterDetachHotkeys()
        {
            if (!_hotkeysRegistered) return;
            _hotkeysRegistered = false;
            try
            {
                if (_sidePanelHandle != IntPtr.Zero)
                {
                    UnregisterHotKey(_sidePanelHandle, HotkeyIdToggleDetach);
                    UnregisterHotKey(_sidePanelHandle, HotkeyIdCopyAll);
                    HwndSource.FromHwnd(_sidePanelHandle)?.RemoveHook(WndProc);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"popout: hotkey unregistration failed: {ex.Message}");
            }
        }

        // Copy-all follows the mode. While detached the panel has
        // WS_EX_NOACTIVATE so its own Ctrl+Shift+C PreviewKeyDown never sees
        // the key - this global registration is what keeps the shortcut alive
        // in exactly that state, and it goes away again on re-dock so docked
        // behavior is byte-for-byte what it was before.
        private void RegisterCopyHotkey()
        {
            if (_sidePanelHandle == IntPtr.Zero) return;
            if (RegisterHotKey(_sidePanelHandle, HotkeyIdCopyAll, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_C))
                FileLogger.Log("popout: hotkey Ctrl+Shift+C registered (detached copy-all)");
            else
                FileLogger.Log($"popout: hotkey Ctrl+Shift+C FAILED (Win32 {Marshal.GetLastWin32Error()})");
        }

        private void UnregisterCopyHotkey()
        {
            if (_sidePanelHandle == IntPtr.Zero) return;
            UnregisterHotKey(_sidePanelHandle, HotkeyIdCopyAll);
        }

        // HwndSource hooks run on the UI thread, so the toggle can touch
        // windows directly with no dispatcher hop.
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;
            int id = wParam.ToInt32();
            if (id == HotkeyIdToggleDetach)
            {
                handled = true;
                ToggleDetach();
            }
            else if (id == HotkeyIdCopyAll)
            {
                handled = true;
                _sidePanel.CopyAllHistoryHotkey();
            }
            return IntPtr.Zero;
        }
    }
}