using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Station.ViewModels;
using System;

namespace Station.Views
{
    public sealed partial class DevicesPage : Page
    {
        public DevicesViewModel ViewModel { get; }

        private Border? _activeFilterBorder;

        public DevicesPage()
        {
            this.InitializeComponent();
            ViewModel = new DevicesViewModel();
            this.DataContext = ViewModel;
            _activeFilterBorder = FilterAllBorder;
        }

        private async void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Station.Dialogs.AddNodeDialog(ViewModel);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Lỗi",
                    Content = $"Không thể mở dialog thêm thiết bị: {ex.Message}",
                    CloseButtonText = "Đóng",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private void StatusFilter_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not Border clicked) return;

            ViewModel.SelectedStatus = clicked.Tag?.ToString() ?? "Tất cả trạng thái";

            if (_activeFilterBorder != null)
                ApplyFilterPillStyle(_activeFilterBorder, active: false);

            ApplyFilterPillStyle(clicked, active: true);
            _activeFilterBorder = clicked;
        }

        private void ApplyFilterPillStyle(Border border, bool active)
        {
            border.Style = (Style)Application.Current.Resources[
                active ? "FilterPillActiveStyle" : "FilterPillStyle"];

            var textBrush = (SolidColorBrush)Application.Current.Resources[
                active ? "DkBlueBrush" : "DkTextSecondaryBrush"];

            if (border.Child is StackPanel sp)
            {
                foreach (var child in sp.Children)
                    if (child is TextBlock tb) tb.Foreground = textBrush;
            }
        }

        private async void DeviceCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is NodeItemViewModel node)
            {
                var dialog = new Station.Dialogs.NodeDetailDialog(node);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
        }

        private async void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NodeItemViewModel node)
            {
                var dialog = new Station.Dialogs.NodeDetailDialog(node);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NodeItemViewModel node)
            {
                var dialog = new Station.Dialogs.EditNodeDialog(node, ViewModel);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NodeItemViewModel node)
            {
                var confirm = new ContentDialog
                {
                    Title = "Xóa thiết bị",
                    Content = $"Xác nhận xóa node \"{node.NodeName}\"?\nTất cả {node.Sensors.Count} cảm biến sẽ bị xóa khỏi danh sách.",
                    PrimaryButtonText = "Xóa",
                    CloseButtonText = "Hủy",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark
                };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                    ViewModel.DeleteNode(node);
            }
        }

        private async void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NodeItemViewModel node)
            {
                var confirm = new ContentDialog
                {
                    Title = "Khởi động lại thiết bị",
                    Content = $"Xác nhận khởi động lại nút \"{node.NodeName}\"?\nThiết bị sẽ ngắt kết nối trong vài giây.",
                    PrimaryButtonText = "Khởi động lại",
                    CloseButtonText = "Hủy",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark
                };
                var result = await confirm.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    System.Diagnostics.Debug.WriteLine($"[DevicesPage] Restart: {node.NodeName}");
            }
        }

        private void DeviceCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b)
            {
                b.BorderBrush = (SolidColorBrush)Application.Current.Resources["DkBlueBorderBrush"];
                b.BorderThickness = new Thickness(1.5);
            }
        }

        private void DeviceCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b)
            {
                b.BorderBrush = (SolidColorBrush)Application.Current.Resources["DkBorderBrush"];
                b.BorderThickness = new Thickness(1);
            }
        }
    }
}
