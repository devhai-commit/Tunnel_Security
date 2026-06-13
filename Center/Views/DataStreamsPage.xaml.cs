using Center.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Center.Views
{
    public sealed partial class DataStreamsPage : Page
    {
        // Use shared ViewModel from App so live SignalR data flows here
        public DataStreamsViewModel ViewModel => App.DataStreams;

        public DataStreamsPage()
        {
            InitializeComponent();
        }

        private void Pause_Click(object s, RoutedEventArgs e)
        {
            ViewModel.IsPaused = !ViewModel.IsPaused;
            (s as ToggleButton)!.Content = ViewModel.IsPaused ? "&#x25B6; Resume" : "&#x23F8; Pause";
        }

        private void Clear_Click(object s, RoutedEventArgs e) => ViewModel.Clear();

        private void StationFilter_Changed(object s, SelectionChangedEventArgs e)
        {
            var item = (s as ComboBox)?.SelectedItem?.ToString() ?? "All";
            ViewModel.StationFilter = item;
        }
    }
}
