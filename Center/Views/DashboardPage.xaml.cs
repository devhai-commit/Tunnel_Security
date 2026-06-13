using Center.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Center.Views
{
    public sealed partial class DashboardPage : Page
    {
        // Use shared ViewModel from App so SignalR updates are reflected here
        public DashboardViewModel ViewModel => App.Dashboard;

        public DashboardPage()
        {
            InitializeComponent();
        }

        private void ViewAllStations_Click(object s, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Frame.Navigate(typeof(StationsPage));
        }
    }
}
