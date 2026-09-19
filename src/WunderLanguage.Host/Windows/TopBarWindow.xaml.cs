using System.Windows;

namespace WunderLanguage.Host.Windows
{
    public partial class TopBarWindow : Window
    {
        // TEMP troubleshooting aid. Remove post-Zelda.
        public event Action? ShotRequested;

        public TopBarWindow()
        {
            InitializeComponent();
        }

        private void ShotButton_Click(object sender, RoutedEventArgs e)
        {
            ShotRequested?.Invoke();
        }
    }
}
