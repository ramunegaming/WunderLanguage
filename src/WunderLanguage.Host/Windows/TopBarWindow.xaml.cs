using System.Windows;

namespace WunderLanguage.Host.Windows
{
    public partial class TopBarWindow : Window
    {
        // TEMP troubleshooting aid. Remove post-Zelda.
        public event Action? ShotRequested;
        public event Action? ShotGameRequested;

        public TopBarWindow()
        {
            InitializeComponent();
        }

        private void ShotGameButton_Click(object sender, RoutedEventArgs e)
        {
            ShotGameRequested?.Invoke();
        }

        private void ShotProgButton_Click(object sender, RoutedEventArgs e)
        {
            ShotRequested?.Invoke();
        }
    }
}
