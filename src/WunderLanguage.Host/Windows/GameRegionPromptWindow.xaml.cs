using System.Threading.Tasks;
using System.Windows;

namespace WunderLanguage.Host.Windows
{
    public partial class GameRegionPromptWindow : Window
    {
        public GameRegionPromptWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => ReassertTopmostBurst();
        }

        private async void ReassertTopmostBurst()
        {
            for (int i = 0; i < 5; i++)
            {
                Win32TopmostHelper.ForceTopmost(this);
                await Task.Delay(100);
            }
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
