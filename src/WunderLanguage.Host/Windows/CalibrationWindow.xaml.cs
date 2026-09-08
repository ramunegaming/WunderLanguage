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
            _topmostTimer.Tick += (_, _) => Win32TopmostHelper.ForceTopmost(this);
            Loaded += (_, _) => _topmostTimer.Start();
            Closed += (_, _) => _topmostTimer.Stop();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(OverlayCanvas);
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

            CalibratedPhysicalBounds = new System.Drawing.Rectangle(
                (int)(screenLeftDip * _dpiScaleX),
                (int)(screenTopDip * _dpiScaleY),
                (int)(_selection.Value.Width * _dpiScaleX),
                (int)(_selection.Value.Height * _dpiScaleY));

            DialogResult = true;
            Close();
        }
    }
}