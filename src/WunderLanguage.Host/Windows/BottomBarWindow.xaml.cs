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
            TogglePause();
        }

        // Single source of truth for pause state: the detached feed's header
        // Pause button (revert: revert popout-feed) routes through here so the
        // two buttons can never disagree, and PauseToggled still fires exactly
        // once per toggle for the capture controller.
        public void TogglePause()
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