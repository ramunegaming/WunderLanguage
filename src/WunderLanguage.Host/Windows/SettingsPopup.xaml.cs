using System.Windows;
using System.Windows.Threading;
using WunderLanguage.Host.Settings;

namespace WunderLanguage.Host.Windows
{
    public partial class SettingsPopup : Window
    {
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _topmostTimer;

        // True when a key is stored (DPAPI) and the user hasn't asked to replace it.
        // The box is only shown when a key needs entering - never prefilled.
        private bool _hasSavedKey;

        public event Action? RecalibrateClicked;

        public SettingsPopup(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _hasSavedKey = !string.IsNullOrEmpty(_settings.DeepLApiKey);

            // Anything not DeepL (including old "Argos" settings files) -> Offline.
            if (_settings.TranslationProvider == "DeepL")
                DeepLRadio.IsChecked = true;
            else
                OfflineRadio.IsChecked = true;

            if (_settings.MenuDisplayMode == "Ascii")
                MenuAsciiRadio.IsChecked = true;
            else
                MenuTextRadio.IsChecked = true;

            UpdateKeyPanelVisibility();

            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _topmostTimer.Tick += (_, _) => { }; // Neutered: AppShell tick solely owns z-order.
            Loaded += (_, _) => _topmostTimer.Start();
            Closed += (_, _) => _topmostTimer.Stop();
        }

        private void ProviderChanged(object sender, RoutedEventArgs e)
        {
            UpdateKeyPanelVisibility();
        }

        private void UpdateKeyPanelVisibility()
        {
            if (DeepLKeyPanel is null || RemoveKeyButton is null || KeySavedText is null || DeepLRadio is null)
                return;

            bool isDeepL = DeepLRadio.IsChecked == true;
            if (!isDeepL)
            {
                DeepLKeyPanel.Visibility = Visibility.Collapsed;
                RemoveKeyButton.Visibility = Visibility.Collapsed;
                KeySavedText.Visibility = Visibility.Collapsed;
                return;
            }

            if (_hasSavedKey)
            {
                DeepLKeyPanel.Visibility = Visibility.Collapsed;
                RemoveKeyButton.Visibility = Visibility.Visible;
                KeySavedText.Visibility = Visibility.Visible;
            }
            else
            {
                DeepLKeyPanel.Visibility = Visibility.Visible;
                RemoveKeyButton.Visibility = Visibility.Collapsed;
                KeySavedText.Visibility = Visibility.Collapsed;
            }
        }

        private void RemoveKey_Click(object sender, RoutedEventArgs e)
        {
            _hasSavedKey = false;
            ApiKeyBox.Password = string.Empty;
            UpdateKeyPanelVisibility();
            ApiKeyBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            bool isDeepL = DeepLRadio.IsChecked == true;
            _settings.TranslationProvider = isDeepL ? "DeepL" : "Offline";
            if (isDeepL && !_hasSavedKey)
            {
                // New entry, or X was pressed: empty box means delete.
                _settings.DeepLApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password;
            }
            // Offline selected, or DeepL with unchanged saved key: leave stored key untouched.
            _settings.MenuDisplayMode = (MenuAsciiRadio.IsChecked == true) ? "Ascii" : "Text";
            _settings.Save();
            Close();
        }

        private void Recalibrate_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            RecalibrateClicked?.Invoke();
            Show();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}