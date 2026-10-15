using System.Threading.Tasks;
using System.Windows;

namespace WunderLanguage.Host.Windows
{
    public partial class GameRegionPromptWindow : Window
    {
        private readonly System.Windows.Threading.DispatcherTimer _topmostTimer;

        public GameRegionPromptWindow()
        {
            InitializeComponent();
            _topmostTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _topmostTimer.Tick += (_, _) => { }; // Neutered: AppShell tick solely owns z-order.
            Loaded += (_, _) => _topmostTimer.Start();
            Closed += (_, _) => _topmostTimer.Stop();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
