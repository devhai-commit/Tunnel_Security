using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Station.Dialogs;
using Station.Models;
using Station.ViewModels;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

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

            ViewModel.AlertDialogRequested += OnAlertDialogRequested;
            ViewModel.SlotsRebuilt += OnSlotsRebuilt;
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

        private void CameraListItem_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not CameraStreamViewModel camera) return;

            args.Data.SetText(camera.CameraId);
            args.Data.RequestedOperation = DataPackageOperation.Copy;

            // Dim the source row while it's being dragged; DropCompleted restores it.
            // Animated by the Grid.OpacityTransition declared on this same element in XAML.
            sender.Opacity = 0.4;
        }

        private void CameraListItem_DropCompleted(UIElement sender, DropCompletedEventArgs args)
        {
            sender.Opacity = 1.0;
        }

        private static readonly SolidColorBrush _rowHoverBrush = new(Windows.UI.Color.FromArgb(18, 255, 255, 255));
        private static readonly SolidColorBrush _rowIdleBrush = new(Windows.UI.Color.FromArgb(0, 255, 255, 255));

        private void CameraListItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid) grid.Background = _rowHoverBrush;
        }

        private void CameraListItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid) grid.Background = _rowIdleBrush;
        }

        private void Slot_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Hiển thị camera ở đây";
            e.DragUIOverride.IsGlyphVisible = false;
        }

        private void Slot_DragEnter(object sender, DragEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is CameraSlotViewModel slot && slot.IsEmpty)
                slot.IsDragHighlighted = true;
        }

        private void Slot_DragLeave(object sender, DragEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is CameraSlotViewModel slot)
                slot.IsDragHighlighted = false;
        }

        private async void Slot_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not CameraSlotViewModel slot) return;
            slot.IsDragHighlighted = false;

            if (!e.DataView.Contains(StandardDataFormats.Text)) return;

            string cameraId = await e.DataView.GetTextAsync();
            int index = ViewModel.Slots.IndexOf(slot);
            ViewModel.AssignCameraToSlot(index, cameraId);
        }

        // Keyboard equivalent of the drag grips below: the grip Grid is a tab stop
        // (IsTabStop in XAML), so arrow keys resize the focused slot without a pointer.
        private void RightGrip_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not CameraSlotViewModel slot) return;

            if (e.Key == VirtualKey.Right) { ViewModel.ExpandColumn(slot); e.Handled = true; }
            else if (e.Key == VirtualKey.Left) { ViewModel.CollapseColumn(slot); e.Handled = true; }
        }

        private void BottomGrip_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not CameraSlotViewModel slot) return;

            if (e.Key == VirtualKey.Down) { ViewModel.ExpandRow(slot); e.Handled = true; }
            else if (e.Key == VirtualKey.Up) { ViewModel.CollapseRow(slot); e.Handled = true; }
        }

        private void ResizeGrip_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Rectangle bar)
                bar.Opacity = 1.0;
        }

        private void ResizeGrip_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Rectangle bar)
                bar.Opacity = 0.5;
        }

        // ── Fixed camera grid: position each slot's generated container on the
        // ── Grid panel (Grid.Row/Column/RowSpan/ColumnSpan), since the panel itself
        // ── has no way to express "this item spans 2 cells" declaratively.

        private Grid? CameraGridPanel => CameraItemsControl.ItemsPanelRoot as Grid;

        private void CameraItemsControl_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToSlots();
            RebuildCameraGrid();
        }

        private void OnSlotsRebuilt()
        {
            SubscribeToSlots();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RebuildCameraGrid);
        }

        private void SubscribeToSlots()
        {
            foreach (var slot in ViewModel.Slots)
                slot.PropertyChanged += Slot_PropertyChanged;
        }

        private void Slot_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not CameraSlotViewModel slot) return;
            if (e.PropertyName is nameof(CameraSlotViewModel.RowSpan)
                or nameof(CameraSlotViewModel.ColumnSpan)
                or nameof(CameraSlotViewModel.IsHiddenBySpan))
            {
                PositionSlot(slot);
            }
        }

        private void RebuildCameraGrid()
        {
            var panel = CameraGridPanel;
            if (panel == null) return;

            if (panel.RowDefinitions.Count != ViewModel.GridRows)
            {
                panel.RowDefinitions.Clear();
                for (int i = 0; i < ViewModel.GridRows; i++)
                    panel.RowDefinitions.Add(new RowDefinition());
            }
            if (panel.ColumnDefinitions.Count != ViewModel.GridColumns)
            {
                panel.ColumnDefinitions.Clear();
                for (int i = 0; i < ViewModel.GridColumns; i++)
                    panel.ColumnDefinitions.Add(new ColumnDefinition());
            }

            foreach (var slot in ViewModel.Slots)
                PositionSlot(slot);
        }

        private void PositionSlot(CameraSlotViewModel slot)
        {
            if (CameraItemsControl.ContainerFromItem(slot) is not FrameworkElement container) return;

            Grid.SetRow(container, slot.Row);
            Grid.SetColumn(container, slot.Column);
            Grid.SetRowSpan(container, Math.Max(1, slot.RowSpan));
            Grid.SetColumnSpan(container, Math.Max(1, slot.ColumnSpan));
            container.Visibility = slot.IsHiddenBySpan ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── Resize grips: drag past half a cell to grow the span to 2 on that axis,
        // ── drag back to shrink it to 1. ManipulationDelta accumulates the raw pointer
        // ── translation; the decision is made once on ManipulationCompleted.

        private CameraSlotViewModel? _resizingColumnSlot;
        private double _columnDragTotal;
        private CameraSlotViewModel? _resizingRowSlot;
        private double _rowDragTotal;

        private void RightGrip_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _resizingColumnSlot = ((FrameworkElement)sender).DataContext as CameraSlotViewModel;
            _columnDragTotal = 0;
        }

        private void RightGrip_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            _columnDragTotal += e.Delta.Translation.X;
        }

        private void RightGrip_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_resizingColumnSlot is not CameraSlotViewModel slot) return;

            double cellWidth = (CameraGridPanel?.ActualWidth ?? 0) / Math.Max(1, ViewModel.GridColumns);
            double threshold = cellWidth > 0 ? cellWidth / 2 : 60;

            if (_columnDragTotal > threshold)
                ViewModel.ExpandColumn(slot);
            else if (_columnDragTotal < -threshold)
                ViewModel.CollapseColumn(slot);

            _resizingColumnSlot = null;
        }

        private void BottomGrip_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _resizingRowSlot = ((FrameworkElement)sender).DataContext as CameraSlotViewModel;
            _rowDragTotal = 0;
        }

        private void BottomGrip_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            _rowDragTotal += e.Delta.Translation.Y;
        }

        private void BottomGrip_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (_resizingRowSlot is not CameraSlotViewModel slot) return;

            double cellHeight = (CameraGridPanel?.ActualHeight ?? 0) / Math.Max(1, ViewModel.GridRows);
            double threshold = cellHeight > 0 ? cellHeight / 2 : 60;

            if (_rowDragTotal > threshold)
                ViewModel.ExpandRow(slot);
            else if (_rowDragTotal < -threshold)
                ViewModel.CollapseRow(slot);

            _resizingRowSlot = null;
        }

        private bool _sidebarExpanded = true;

        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            _sidebarExpanded = !_sidebarExpanded;

            if (_sidebarExpanded)
            {
                SidebarColumn.Width = new GridLength(280);
                SidebarHeaderGrid.Padding = new Thickness(16, 13, 10, 13);
                SidebarCollapsibleContent.Visibility = Visibility.Visible;
                SidebarTitlePanel.Visibility = Visibility.Visible;
                SidebarToggleIcon.Glyph = "";
                ToolTipService.SetToolTip(SidebarToggleButton, "Thu gọn danh sách");
            }
            else
            {
                SidebarColumn.Width = new GridLength(48);
                SidebarHeaderGrid.Padding = new Thickness(8, 13, 8, 13);
                SidebarCollapsibleContent.Visibility = Visibility.Collapsed;
                SidebarTitlePanel.Visibility = Visibility.Collapsed;
                SidebarToggleIcon.Glyph = "";
                ToolTipService.SetToolTip(SidebarToggleButton, "Mở rộng danh sách");
            }
        }

        private void VideoGridContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RebuildCameraGrid();
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

        private void FullscreenResolution_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is string res && ViewModel.FocusedCamera != null)
                ViewModel.FocusedCamera.Resolution = res;
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
