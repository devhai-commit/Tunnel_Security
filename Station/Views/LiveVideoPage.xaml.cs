using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Station.Dialogs;
using Station.Models;
using Station.ViewModels;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace Station.Views
{
    public sealed partial class LiveVideoPage : Page
    {
        public LiveVideoViewModel ViewModel { get; }

        public LiveVideoPage()
        {
            this.InitializeComponent();
            ViewModel = new LiveVideoViewModel();
            this.DataContext = ViewModel;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.AlertDialogRequested += OnAlertDialogRequested;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LiveVideoViewModel.GridColumns))
                UpdateCameraItemSize();
        }

        private async void OnAlertDialogRequested(CameraStreamViewModel camera)
        {
            var alert = new Alert
            {
                Title = camera.AlertTitle,
                Description = camera.AlertDescription,
                Severity = camera.AlertSeverityLevel,
                CameraId = camera.CameraId,
                NodeId = camera.CameraId,
                NodeName = camera.CameraName,
                CreatedAt = camera.AlertTime
            };

            var dialog = new AlertVideoDialog(alert)
            {
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var camera in ViewModel.CameraStreams)
                camera.IsSelected = true;
            ViewModel.UpdateActiveCameras();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var camera in ViewModel.CameraStreams)
                camera.IsSelected = false;
            ViewModel.UpdateActiveCameras();
        }

        private void VideoGridContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCameraItemSize();
        }

        private void UpdateCameraItemSize()
        {
            if (VideoGridContainer == null || ViewModel == null) return;

            double containerWidth = VideoGridContainer.ActualWidth;
            if (containerWidth <= 0) return;

            double availableWidth = containerWidth - 24;

            int columns = ViewModel.GridColumns;
            if (columns < 1) columns = 1;

            double newWidth = Math.Floor(availableWidth / columns);
            double newHeight = Math.Floor(newWidth * 0.75);

            if (newWidth < 200) newWidth = 200;
            if (newHeight < 150) newHeight = 150;

            ViewModel.CameraItemWidth = newWidth;
            ViewModel.CameraItemHeight = newHeight;
        }

        private static readonly HttpClient _http = new();

        private static string ApiBase =>
            Environment.GetEnvironmentVariable("BACKEND_BASE_URL") ?? "http://localhost:5280";

        private async void ChooseVideo_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not CameraStreamViewModel camera) return;

            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).m_window!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            foreach (var ext in new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".m4v" })
                picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                var body = new StringContent(
                    JsonSerializer.Serialize(new { filePath = file.Path }),
                    Encoding.UTF8, "application/json");

                var resp = await _http.PutAsync(
                    $"{ApiBase}/api/cameras/{camera.CameraId}/video-source", body);

                if (resp.IsSuccessStatusCode)
                    camera.LocalVideoPath = file.Path; // drives the footer chip only
                else
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[LiveVideo] SetVideoSource failed: {err}");
                    await ShowErrorDialog(file.Path, err);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveVideo] SetVideoSource error: {ex.Message}");
            }
        }

        private async void ClearVideo_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not CameraStreamViewModel camera) return;

            try
            {
                await _http.DeleteAsync($"{ApiBase}/api/cameras/{camera.CameraId}/video-source");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveVideo] ClearVideoSource error: {ex.Message}");
            }
            finally
            {
                camera.LocalVideoPath = null;
            }
        }

        private async System.Threading.Tasks.Task ShowErrorDialog(string filePath, string detail)
        {
            var dlg = new ContentDialog
            {
                Title             = "Không thể giả lập stream",
                Content           = $"File: {System.IO.Path.GetFileName(filePath)}\n\n{detail}\n\nĐảm bảo FFMpeg đã được cài đặt và có trong PATH.",
                CloseButtonText   = "Đóng",
                XamlRoot          = XamlRoot
            };
            await dlg.ShowAsync();
        }
    }
}
