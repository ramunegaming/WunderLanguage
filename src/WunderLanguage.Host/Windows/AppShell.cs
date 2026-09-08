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

        public AppShell(IntPtr emulatorHandle, GameRegion gameRegion, AppSettings settings)
        {
            _emulatorHandle = emulatorHandle;
            _gameRegion = gameRegion;
            _settings = settings;
            _topBar = new TopBarWindow();
            _sidePanel = new SidePanelWindow();
            _bottomBar = new BottomBarWindow();
            _bottomBar.PauseToggled += OnPauseToggled;
            _bottomBar.SettingsRequested += OnSettingsRequested;
            _tracker = new EmulatorWindowTracker(emulatorHandle);
            _tracker.BoundsChanged += OnBoundsChanged;
            _tracker.Minimized += OnEmulatorMinimized;
            _tracker.Restored += OnEmulatorRestored;
            _captureController = BuildCaptureController();
            _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _zOrderTimer.Tick += (_, _) => UpdateTopmost();
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
            DeepLProvider? deepL = _settings.TranslationProvider == "DeepL" && !string.IsNullOrWhiteSpace(_settings.DeepLApiKey)
                ? new DeepLProvider(_settings.DeepLApiKey!)
                : null;
            var offline = new OfflineTranslationProvider();
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
            controller.TextUpdated += (oldJa, ja, en) => _sidePanel.UpdateLastTranslationIfMatches(oldJa, ja, en);
            controller.WindowLost += OnCaptureWindowLost;
            return controller;
        }

        // Fired when CaptureController gives up after repeatedly failing
        // to capture the emulator window and confirms the handle is
        // genuinely dead. Previously this failure mode just spun silently
        // in the background log forever with no visible sign anything was
        // wrong - translation would simply stop with no explanation.
        private void OnCaptureWindowLost()
        {
            FileLogger.Log("OnCaptureWindowLost: notifying and stopping.");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    "Lost the connection to the emulator window - it may have closed or crashed. " +
                    "Translation has stopped. Reconnect from the Connect window to resume.",
                    "WunderLanguage",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            });
        }

        public void Start()
        {
            WindowCapture.FocusWindow(_emulatorHandle);
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
            ForceTopmost("Start");
            BeginPostRestoreTopmostBurst();
            _sidePanel.MenuDisplayMode = _settings.MenuDisplayMode;
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
            if (shouldBeOnTop)
            {
                SetTopmostLogged(_topBarHandle, "topBar");
                SetTopmostLogged(_sidePanelHandle, "sidePanel");
                SetTopmostLogged(_bottomBarHandle, "bottomBar");
                if (popupHandle != IntPtr.Zero)
                    SetTopmostLogged(popupHandle, "activePopup");
            }
            else
            {
                SetNotTopmostLogged(_topBarHandle, "topBar", foreground);
                SetNotTopmostLogged(_sidePanelHandle, "sidePanel", foreground);
                SetNotTopmostLogged(_bottomBarHandle, "bottomBar", foreground);
                if (popupHandle != IntPtr.Zero)
                    SetNotTopmostLogged(popupHandle, "activePopup", foreground);
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
            SetTopmostLogged(_topBarHandle, "topBar");
            SetTopmostLogged(_sidePanelHandle, "sidePanel");
            SetTopmostLogged(_bottomBarHandle, "bottomBar");
            IntPtr popupHandle = GetActivePopupHandle();
            if (popupHandle != IntPtr.Zero)
                SetTopmostLogged(popupHandle, "activePopup");
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
            FileLogger.Log("OnEmulatorMinimized: hiding all bars");
            _topBar.Hide();
            _sidePanel.Hide();
            _bottomBar.Hide();
            _activePopup?.Hide();
        }

        private void OnEmulatorRestored()
        {
            FileLogger.Log("OnEmulatorRestored: showing all bars");
            _topBar.Show();
            _sidePanel.Show();
            _bottomBar.Show();
            _activePopup?.Show();
            ForceTopmost("OnEmulatorRestored");
            BeginPostRestoreTopmostBurst();
        }

        private void OnPauseToggled(bool paused)
        {
            if (paused)
                _captureController.Stop();
            else if (_hasCalibratedRegion)
                _captureController.Start();
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
            _activePopup = popup;
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

        private void OnBoundsChanged(Rectangle windowBounds)
        {
            int topBarHeightPx = (int)Math.Round(TopBarHeight * _dpiScaleY);
            int bottomBarHeightPx = (int)Math.Round(BottomBarHeight * _dpiScaleY);
            int sidePanelWidthPx = (int)Math.Round(SidePanelWidth * _dpiScaleX);
            int marginPx = (int)Math.Round(Margin * _dpiScaleX);
            const int ShadowCompensationDip = 2;
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
            IntPtr hdwp = BeginDeferWindowPos(3);
            if (hdwp != IntPtr.Zero)
            {
                hdwp = DeferWindowPos(hdwp, _topBarHandle, IntPtr.Zero,
                    left, topBarTop, width, topBarHeightPx,
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }
            if (hdwp != IntPtr.Zero)
            {
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
        }
    }
}