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

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

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
            Loaded += (_, _) => _topmostTimer.Start();
            Closed += (_, _) => _topmostTimer.Stop();
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
    }
}