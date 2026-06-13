using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Station.ViewModels;

public partial class SnapshotGalleryViewModel : ObservableObject
{
    private readonly string _apiBaseUrl;
    private readonly DispatcherQueue? _dispatcher;

    [ObservableProperty]
    private string selectedCameraId = string.Empty;

    [ObservableProperty]
    private DateTime fromDate = DateTime.Today.AddDays(-1);

    [ObservableProperty]
    private DateTime toDate = DateTime.Today.AddDays(1);

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private int totalSnapshots;

    [ObservableProperty]
    private int currentPage = 1;

    [ObservableProperty]
    private int pageSize = 20;

    [ObservableProperty]
    private SnapshotItem? selectedSnapshot;

    public ObservableCollection<SnapshotItem> Snapshots { get; } = new();
    public ObservableCollection<string> CameraIds { get; } = new();

    public SnapshotGalleryViewModel()
    {
        _apiBaseUrl = Environment.GetEnvironmentVariable("BACKEND_BASE_URL") ?? "http://localhost:5280";
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _ = LoadCamerasAsync();
    }

    [RelayCommand]
    private async Task LoadSnapshotsAsync()
    {
        if (string.IsNullOrEmpty(SelectedCameraId)) return;

        IsLoading = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var from = FromDate.ToString("o");
            var to = ToDate.ToString("o");
            var url = $"{_apiBaseUrl}/api/cameras/{SelectedCameraId}/snapshots?from={from}&to={to}&page={CurrentPage}&pageSize={PageSize}";

            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            TotalSnapshots = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;

            _dispatcher?.TryEnqueue(() =>
            {
                Snapshots.Clear();
                if (root.TryGetProperty("snapshots", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        Snapshots.Add(new SnapshotItem
                        {
                            Id = item.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                            CameraId = SelectedCameraId,
                            Timestamp = item.TryGetProperty("timestamp", out var ts) ? ts.GetDateTime() : DateTime.MinValue,
                            ThumbnailUrl = $"{_apiBaseUrl}/api/cameras/{SelectedCameraId}/snapshots/{item.GetProperty("id").GetInt64()}/thumbnail",
                            FullImageUrl = $"{_apiBaseUrl}/api/cameras/{SelectedCameraId}/snapshots/{item.GetProperty("id").GetInt64()}/image",
                            DetectionType = item.TryGetProperty("detectionType", out var dt) ? dt.GetString() : null,
                            Confidence = item.TryGetProperty("confidence", out var cf) && cf.ValueKind != JsonValueKind.Null ? cf.GetDouble() : null
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SnapshotGallery] Load failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage * PageSize < TotalSnapshots)
        {
            CurrentPage++;
            _ = LoadSnapshotsAsync();
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            _ = LoadSnapshotsAsync();
        }
    }

    [RelayCommand]
    private void SelectSnapshot(SnapshotItem? item)
    {
        SelectedSnapshot = item;
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync($"{_apiBaseUrl}/api/cameras");
            using var doc = JsonDocument.Parse(json);

            _dispatcher?.TryEnqueue(() =>
            {
                CameraIds.Clear();
                foreach (var cam in doc.RootElement.EnumerateArray())
                {
                    var id = cam.TryGetProperty("id", out var el) ? el.GetString() : null;
                    if (id != null) CameraIds.Add(id);
                }
                if (CameraIds.Count > 0) SelectedCameraId = CameraIds[0];
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SnapshotGallery] LoadCameras failed: {ex.Message}");
        }
    }
}

public class SnapshotItem
{
    public long Id { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string FullImageUrl { get; set; } = string.Empty;
    public string? DetectionType { get; set; }
    public double? Confidence { get; set; }
}
