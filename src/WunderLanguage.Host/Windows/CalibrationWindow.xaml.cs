using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;

namespace WunderLanguage.Host.Windows
{
    public partial class CalibrationWindow : Window
    {
        private Point? _dragStart;
        private Rect? _selection;
        private readonly double _dpiScaleX;
        private readonly double _dpiScaleY;

        public System.Drawing.Rectangle? CalibratedPhysicalBounds { get; private set; }

        private readonly System.Windows.Threading.DispatcherTimer _topmostTimer;

        public CalibrationWindow()
        {
            InitializeComponent();

            // PMv2 raw metrics as the initial DIP guess (scale 1.0 —
            // deliberately overshoots so every screen edge is inside the
            // overlay from the first frame); the Loaded measure-and-
            // correct loop below converges to exact physical coverage.
            // The old SystemParameters.VirtualScreen* values are DIP
            // divided by the STALE system DPI (144 ghost): (0,0,2986.7,
            // 960) here, which rendered the overlay 3733x1200 over a
            // 4480x1440 virtual screen — bottom/right unreachable.
            // Revert: revert calib-cover.
            var vs0 = Capture.WindowCapture.GetVirtualScreenRect();
            Left = vs0.Left;
            Top = vs0.Top;
            Width = vs0.Width;
            Height = vs0.Height;

            var dpi = VisualTreeHelper.GetDpi(this);
            _dpiScaleX = dpi.DpiScaleX;
            _dpiScaleY = dpi.DpiScaleY;

            KeyDown += CalibrationWindow_KeyDown;

            // The panels reassert their own topmost status continuously,
            // forever, every 150ms - a one-time burst here would eventually
            // get outlasted by that (e.g. during a drag or a minimize/
            // restore that takes longer than the burst window). Running
            // continuously for as long as this window is open, faster than
            // the panels' own interval, means this always wins the race.
            _topmostTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _topmostTimer.Tick += (_, _) => { }; // Neutered: AppShell tick solely owns z-order.

            // Measure-and-correct: WPF converts our DIP rect to physical px
            // with whatever DPI it currently assigns this (dual-monitor,
            // mixed-scale) window — unknowable up front, so we measure the
            // real frame against the raw virtual screen and nudge. <=3
            // passes, 2px tolerance, fail-open (never block calibration on
            // a measurement). Idempotent: converged = timer stops, and a
            // re-open converges on pass 1. Revert: revert calib-cover.
            _coverTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _coverTimer.Tick += (_, _) => CoverageTick();

            Loaded += (_, _) => { _topmostTimer.Start(); StartCoverageCorrection(); };
            Closed += (_, _) => { _topmostTimer.Stop(); _coverTimer.Stop(); };
        }

        private int _coverPasses;
        private readonly System.Windows.Threading.DispatcherTimer _coverTimer;

        private void StartCoverageCorrection()
        {
            _coverPasses = 0;
            _coverTimer.Start();
        }

        private void CoverageTick()
        {
            _coverPasses++;
            bool stop = false;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero && ActualWidth > 10 && ActualHeight > 10 &&
                    Capture.WindowCapture.TryGetUserRect(hwnd, out var phys) && phys.Width > 10 && phys.Height > 10)
                {
                    var vs = Capture.WindowCapture.GetVirtualScreenRect();
                    double sx = (double)phys.Width / ActualWidth;
                    double sy = (double)phys.Height / ActualHeight;
                    if (sx > 0.5 && sx < 3.0 && sy > 0.5 && sy < 3.0)
                    {
                        int dl = phys.Left - vs.Left, dt = phys.Top - vs.Top;
                        int dw = phys.Width - vs.Width, dh = phys.Height - vs.Height;
                        bool converged = Math.Abs(dl) <= 2 && Math.Abs(dt) <= 2 && Math.Abs(dw) <= 2 && Math.Abs(dh) <= 2;
                        if (!converged)
                        {
                            Left -= dl / sx;
                            Top -= dt / sy;
                            Width -= dw / sx;
                            Height -= dh / sy;
                        }
                        FileLogger.Log($"[calibrate] overlay coverage pass={_coverPasses} phys={phys} want={vs} scale={sx:0.000}x{sy:0.000} converged={converged}");
                        stop = converged;
                    }
                    else
                    {
                        FileLogger.Log($"[calibrate] overlay coverage pass={_coverPasses} implausible scale {sx:0.000}x{sy:0.000} - leaving overlay as-is");
                    }
                }
                else
                {
                    FileLogger.Log($"[calibrate] overlay coverage pass={_coverPasses} hwnd/rect unmeasurable - leaving overlay as-is");
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[calibrate] overlay coverage correction failed: {ex.Message}");
                stop = true; // fail-open
            }
            if (stop || _coverPasses >= 3) _coverTimer.Stop();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(OverlayCanvas);
            // Capture the mouse for the drag: without this, fast movement
            // (or crossing window edges) stops MouseMove delivery and the
            // rect freezes short of where the cursor actually went — e.g.
            // the bottom edge becomes unreachable.
            try { Mouse.Capture(OverlayCanvas, CaptureMode.Element); } catch { }
            SelectionRect.Visibility = Visibility.Visible;
            ConfirmPanel.Visibility = Visibility.Collapsed;
            UpdateSelectionVisual(_dragStart.Value, _dragStart.Value);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
                return;

            UpdateSelectionVisual(_dragStart.Value, e.GetPosition(OverlayCanvas));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragStart is null)
                return;

            try { OverlayCanvas.ReleaseMouseCapture(); } catch { }
            var end = e.GetPosition(OverlayCanvas);
            var rect = new Rect(_dragStart.Value, end);
            _dragStart = null;

            if (rect.Width < 10 || rect.Height < 10)
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                return; // too small - likely an accidental click, ignore it
            }

            _selection = rect;
            ConfirmPanel.Visibility = Visibility.Visible;
        }

        private void UpdateSelectionVisual(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X);
            double y = Math.Min(a.Y, b.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = Math.Abs(a.X - b.X);
            SelectionRect.Height = Math.Abs(a.Y - b.Y);
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            _selection = null;
            SelectionRect.Visibility = Visibility.Collapsed;
            ConfirmPanel.Visibility = Visibility.Collapsed;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

        private void CalibrationWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Confirm();
            else if (e.Key == Key.Escape)
                Redo_Click(sender, e);
        }

        private void Confirm()
        {
            if (_selection is null)
                return;

            double screenLeftDip = Left + _selection.Value.Left;
            double screenTopDip = Top + _selection.Value.Top;

            // True scale MEASURED from our own overlay window (HWND physical
            // px ÷ DIP size), never reported: DPI APIs disagreed with reality
            // for whole sessions after display churn (stale-144 ghost), which
            // poisoned saved regions 1.2x too big (desktop garbage in OCR).
            // Falls back to construction values if unmeasurable.
            double scaleX = _dpiScaleX, scaleY = _dpiScaleY;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero && ActualWidth > 10 && ActualHeight > 10 &&
                    Capture.WindowCapture.TryGetUserRect(hwnd, out var phys) && phys.Width > 10 && phys.Height > 10)
                {
                    double mx = (double)phys.Width / ActualWidth, my = (double)phys.Height / ActualHeight;
                    if (mx > 0.5 && mx < 3.0 && my > 0.5 && my < 3.0)
                    {
                        scaleX = mx; scaleY = my;
                        FileLogger.Log($"[calibrate] measured scale {scaleX:0.000}x{scaleY:0.000} (ctor was {_dpiScaleX:0.00}x{_dpiScaleY:0.00})");
                    }
                }
            }
            catch { }

            CalibratedPhysicalBounds = new System.Drawing.Rectangle(
                (int)(screenLeftDip * scaleX),
                (int)(screenTopDip * scaleY),
                (int)(_selection.Value.Width * scaleX),
                (int)(_selection.Value.Height * scaleY));

            DialogResult = true;
            Close();
        }

        // Offline coverage gate (2026-11-11): --calibcover shows the real
        // overlay, pumps the dispatcher (same pattern as --captureswap),
        // then asserts the overlay's physical frame covers the PMv2 raw
        // virtual screen (GetSystemMetrics 76..79) within 2px on every
        // edge. No emulator, no sidecar, no GPU; exit 0 only on full
        // coverage. Revert: revert calib-cover (delete this method + the
        // --calibcover block in App.xaml.cs).
        public static bool RunCoverageGate()
        {
            CalibrationWindow? w = null;
            try
            {
                w = new CalibrationWindow();
                w.Show();
                w.Activate();
                // BeginInvoke(Background) + PushFrame flushes the dispatcher
                // queue; the sleep gives DWM time to actually paint.
                var deadline = DateTime.UtcNow.AddMilliseconds(700);
                while (DateTime.UtcNow < deadline)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    System.Threading.Thread.Sleep(10);
                }
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                var vs = Capture.WindowCapture.GetVirtualScreenRect();
                if (hwnd == IntPtr.Zero || !Capture.WindowCapture.TryGetUserRect(hwnd, out var phys))
                {
                    FileLogger.Log("[calibcover] overlay hwnd/rect unmeasurable");
                    return false;
                }
                int dl = phys.Left - vs.Left;
                int dt = phys.Top - vs.Top;
                int dr = phys.Right - vs.Right;
                int db = phys.Bottom - vs.Bottom;
                bool ok = Math.Abs(dl) <= 2 && Math.Abs(dt) <= 2 && Math.Abs(dr) <= 2 && Math.Abs(db) <= 2;
                FileLogger.Log($"[calibcover] overlay phys={phys} virtual={vs} delta L{dl} T{dt} R{dr} B{db} ok={ok}");
                return ok;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[calibcover] ERROR: {ex.Message}");
                return false;
            }
            finally
            {
                try { w?.Close(); } catch { }
            }
        }
    }
}