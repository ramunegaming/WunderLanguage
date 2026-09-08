using System.Linq;
using System.Windows;
using WunderLanguage.Host.Capture;
using WunderLanguage.Host.Settings;

namespace WunderLanguage.Host.Windows
{
    public partial class ConnectWindow : Window
    {
        private readonly AppSettings _settings;
        private List<WindowInfo> _windows = new();

        private bool _connected;

        public ConnectWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            RefreshWindowList();
            Closed += (_, _) =>
            {
                if (!_connected)
                    Application.Current.Shutdown();
            };
        }

        private void RefreshWindowList()
        {
            _windows = WindowCapture.ListVisibleWindows();
            WindowList.ItemsSource = _windows
                .Select(w => w.IsMinimized ? $"{w.Title} (minimized)" : w.Title)
                .ToList();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var popup = new SettingsPopup(_settings) { Owner = this };
            popup.ShowDialog();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowList.SelectedIndex < 0)
            {
                MessageBox.Show("Pick a window first.", "WunderLanguage");
                return;
            }

            if (_settings.TranslationProvider == "DeepL" && string.IsNullOrWhiteSpace(_settings.DeepLApiKey))
            {
                MessageBox.Show(
                    "Add a DeepL API key in Settings first, or switch to Offline model (CAT-Translate 3.3b).",
                    "WunderLanguage");
                return;
            }

            var selected = _windows[WindowList.SelectedIndex];
            WindowCapture.RestoreIfMinimized(selected.Handle);

            if (!WindowCapture.TryGetWindowBounds(selected.Handle, out var windowBounds))
            {
                MessageBox.Show("Could not read that window's position. Try again.", "WunderLanguage");
                return;
            }

            // Start with a placeholder region covering the whole window so
            // the companion panels attach and become visible immediately -
            // that gives a clear visual reference for where to drag the
            // real calibration box next, rather than calibrating blind
            // against just the bare emulator with nothing attached yet.
            var placeholderRegion = new GameRegion(windowBounds, windowBounds);
            var shell = new AppShell(selected.Handle, placeholderRegion, _settings);
            shell.Start();

            _connected = true;
            Close();

            shell.RunCalibration(showIntroPrompt: true);
        }
    }
}
