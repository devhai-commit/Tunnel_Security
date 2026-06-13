using Center.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Center.Views
{
    public sealed partial class AlertsPage : Page
    {
        // Use shared ViewModel so real-time alerts from SignalR appear here
        public AlertsViewModel ViewModel => App.Alerts;

        public AlertsPage()
        {
            InitializeComponent();
        }

        private void SeverityFilter_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SeverityFilter = (sender as Button)?.Tag?.ToString() ?? "All";
        }
    }
}
