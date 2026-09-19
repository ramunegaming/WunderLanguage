using System.Windows;

namespace WunderLanguage.Host.Windows
{
    public partial class BottomBarWindow : Window
    {
        private bool _paused;

        public event Action<bool>? PauseToggled;
        public event Action? SettingsRequested;

        public BottomBarWindow()
        {
            InitializeComponent();
            // Sole taskbar presence: right-click Close here must exit the app,
            // not strand it headless (shutdown is explicit).
            Closed += (_, _) => Application.Current.Shutdown();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _paused = !_paused;
            PauseButton.Content = _paused ? "Resume" : "Pause";
            PauseToggled?.Invoke(_paused);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // No longer creates SettingsPopup directly - AppShell owns it now,
            // so it can track the popup and keep it in sync with the
            // emulator's minimize/restore state.
            SettingsRequested?.Invoke();
        }
    }
}