using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Station.Models;
using Station.Services;

namespace Station.ViewModels
{
    public partial class LiveVideoViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Timer _blinkTimer;
        private bool _blinkState = true;
        private readonly IDataService _mockData = DataServiceLocator.Current;

        // Current layout
        [ObservableProperty]
        private CameraGridLayout _currentLayout = CameraGridLayout.TwoByTwo;

        [ObservableProperty]
        private int _gridColumns = 2;

        [ObservableProperty]
        private int _gridRows = 2;

        // Camera streams
        public ObservableCollection<CameraStreamViewModel> CameraStreams { get; } = new();

        // Statistics
        [ObservableProperty]
        private int _activeCameras = 0;

        private double _cameraItemWidth = 420;
        public double CameraItemWidth
        {
            get => _cameraItemWidth;
            set => SetProperty(ref _cameraItemWidth, value);
        }

        private double _cameraItemHeight = 340;
        public double CameraItemHeight
        {
            get => _cameraItemHeight;
            set => SetProperty(ref _cameraItemHeight, value);
        }

        [ObservableProperty]
        private int _totalCameras = 0;

        [ObservableProperty]
        private string _selectedLayoutText = "2×2 (4 cameras)";

        // Alert state
        [ObservableProperty]
        private bool _hasActiveAlerts = false;

        [ObservableProperty]
        private int _activeAlertCount = 0;

        [ObservableProperty]
        private bool _alertBannerHighlight = true;

        // Event for requesting dialog display (raised to View)
        public event Action<CameraStreamViewModel>? AlertDialogRequested;

        public LiveVideoViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            LoadCameraStreams();
            ChangeLayout(CameraGridLayout.TwoByTwo);

            // Blink timer: 600ms toggle for alert border animation
            _blinkTimer = new Timer(600);
            _blinkTimer.Elapsed += OnBlinkTick;
            _blinkTimer.AutoReset = true;
            _blinkTimer.Start();

            _mockData.AlertGenerated += OnMockAlertGenerated;
            _mockData.Start();
        }

        private void OnMockAlertGenerated(object? sender, AlertGeneratedEventArgs e)
        {
            if (e.TriggeredByCameraId == null) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                var cam = CameraStreams.FirstOrDefault(c => c.CameraId == e.TriggeredByCameraId);
                if (cam == null || !cam.IsOnline) return;

                var simCam = _mockData.Cameras.FirstOrDefault(c => c.CameraId == e.TriggeredByCameraId);
                string location = simCam?.Location ?? cam.CameraName;

                cam.TriggerAlert(e.Alert.Title, e.Alert.Description, e.Alert.Severity, location);
                RefreshAlertStats();
            });
        }

        private void OnBlinkTick(object? sender, ElapsedEventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!HasActiveAlerts) return;
                _blinkState = !_blinkState;
                AlertBannerHighlight = _blinkState;
                foreach (var cam in CameraStreams.Where(c => c.HasAlert))
                    cam.AlertBorderOpacity = _blinkState ? 1.0 : 0.15;
            });
        }

        private void RefreshAlertStats()
        {
            ActiveAlertCount = CameraStreams.Count(c => c.HasAlert);
            HasActiveAlerts = ActiveAlertCount > 0;
        }

        private void LoadCameraStreams()
        {
            CameraStreams.Clear();

            var apiBaseUrl = Environment.GetEnvironmentVariable("BACKEND_BASE_URL") ?? "http://localhost:5280";
            int camIdx = 0;
            foreach (var simCam in _mockData.Cameras)
            {
                int idx = ++camIdx;
                CameraStreams.Add(new CameraStreamViewModel
                {
                    CameraId = simCam.CameraId,
                    CameraName = $"{simCam.CameraName} · {simCam.Location}",
                    StreamUrl = simCam.StreamUrl ?? $"{apiBaseUrl}/api/cameras/{simCam.CameraId}/stream",
                    Resolution = "320×240",
                    IrStatus = "ON",
                    HdrStatus = "AUTO",
                    IsOnline = simCam.IsOnline,
                    IsRecording = simCam.IsOnline && idx <= 10,
                    Fps = 30,
                    Bitrate = 2.5
                });
            }

            TotalCameras = CameraStreams.Count;
            UpdateActiveCameras();
        }

        public void UpdateActiveCameras()
        {
            ActiveCameras = CameraStreams.Count(c => c.IsSelected && c.IsOnline && c.IsStreamEnabled);
        }

        [RelayCommand]
        private void ShowAlertVideo(CameraStreamViewModel? camera)
        {
            if (camera == null || !camera.HasAlert) return;
            AlertDialogRequested?.Invoke(camera);
        }

        [RelayCommand]
        private void ShowMostCriticalAlert()
        {
            var critical = CameraStreams
                .Where(c => c.HasAlert)
                .OrderByDescending(c => (int)c.AlertSeverityLevel)
                .FirstOrDefault();
            if (critical != null)
                AlertDialogRequested?.Invoke(critical);
        }

        [RelayCommand]
        private void ChangeLayout(object? parameter)
        {
            int count = 4;
            if (parameter is int c) count = c;
            else if (parameter is string s && int.TryParse(s, out int parsed)) count = parsed;

            switch (count)
            {
                case 1:
                    CurrentLayout = CameraGridLayout.Single;
                    GridColumns = 1; GridRows = 1;
                    SelectedLayoutText = "1×1 (1 camera)";
                    break;
                case 4:
                    CurrentLayout = CameraGridLayout.TwoByTwo;
                    GridColumns = 2; GridRows = 2;
                    SelectedLayoutText = "2×2 (4 cameras)";
                    break;
                case 9:
                    CurrentLayout = CameraGridLayout.ThreeByThree;
                    GridColumns = 3; GridRows = 3;
                    SelectedLayoutText = "3×3 (9 cameras)";
                    break;
                case 16:
                    CurrentLayout = CameraGridLayout.FourByFour;
                    GridColumns = 4; GridRows = 4;
                    SelectedLayoutText = "4×4 (16 cameras)";
                    break;
                default:
                    CurrentLayout = CameraGridLayout.TwoByTwo;
                    GridColumns = 2; GridRows = 2;
                    SelectedLayoutText = "2×2 (4 cameras)";
                    break;
            }

            for (int i = 0; i < CameraStreams.Count; i++)
                CameraStreams[i].IsSelected = (i < count);
            UpdateActiveCameras();
        }

        [RelayCommand]
        private void RefreshStreams()
        {
            LoadCameraStreams();
        }

        [RelayCommand]
        private void TakeSnapshot(CameraStreamViewModel? camera)
        {
            if (camera == null) return;
            System.Diagnostics.Debug.WriteLine($"Snapshot taken for {camera.CameraName}");
        }

        [RelayCommand]
        private void ToggleRecording(CameraStreamViewModel? camera)
        {
            if (camera == null) return;
            camera.IsRecording = !camera.IsRecording;
        }

        [RelayCommand]
        private void ToggleStream(CameraStreamViewModel? camera)
        {
            if (camera == null) return;
            camera.IsStreamEnabled = !camera.IsStreamEnabled;
            UpdateActiveCameras();
        }

        [RelayCommand]
        private void ShowCameraSettings(CameraStreamViewModel? camera) { }

        [ObservableProperty]
        private CameraStreamViewModel? _focusedCamera;

        [ObservableProperty]
        private bool _isCameraFocused = false;

        [RelayCommand]
        private void FocusCamera(CameraStreamViewModel? camera)
        {
            FocusedCamera = camera;
            IsCameraFocused = camera != null;
        }

        [RelayCommand]
        private void ExitFocus()
        {
            FocusedCamera = null;
            IsCameraFocused = false;
        }
    }

    public partial class CameraStreamViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _cameraId = string.Empty;

        [ObservableProperty]
        private string _cameraName = string.Empty;

        [ObservableProperty]
        private string _streamUrl = string.Empty;

        [ObservableProperty]
        private string _resolution = "1280×720";

        [ObservableProperty]
        private string _irStatus = "AUTO";

        [ObservableProperty]
        private string _hdrStatus = "AUTO";

        [ObservableProperty]
        private bool _isOnline;

        [ObservableProperty]
        private bool _isRecording;

        [ObservableProperty]
        private int _fps;

        [ObservableProperty]
        private double _bitrate;

        // Stream enabled/disabled toggle — when false, no frames are requested from backend.
        [ObservableProperty]
        private bool _isStreamEnabled = true;

        partial void OnIsStreamEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(StreamToggleIcon));
            OnPropertyChanged(nameof(StreamToggleTooltip));
            OnPropertyChanged(nameof(StreamToggleColor));
        }

        public string StreamToggleIcon => _isStreamEnabled ? "" : "";
        public string StreamToggleTooltip => _isStreamEnabled ? "Tạm dừng nhận stream" : "Bật nhận stream";
        public SolidColorBrush StreamToggleColor => _isStreamEnabled
            ? new SolidColorBrush(Colors.Gray)
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36));

        // Path to a local video file — set by FileOpenPicker in code-behind.
        // When non-null, CameraVideoControl plays the file instead of the MJPEG stream.
        [ObservableProperty]
        private string? _localVideoPath;

        // Alert state
        [ObservableProperty]
        private bool _hasAlert = false;

        [ObservableProperty]
        private double _alertBorderOpacity = 1.0;

        [ObservableProperty]
        private string _alertTitle = string.Empty;

        [ObservableProperty]
        private string _alertDescription = string.Empty;

        [ObservableProperty]
        private AlertSeverity _alertSeverityLevel = AlertSeverity.Low;

        [ObservableProperty]
        private string _alertLocation = string.Empty;

        [ObservableProperty]
        private DateTimeOffset _alertTime = DateTimeOffset.Now;

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // Computed display helpers

        public string StatusText => _isOnline ? "Online" : "Offline";

        public SolidColorBrush StatusColor => _isOnline
            ? new SolidColorBrush(Colors.Green)
            : new SolidColorBrush(Colors.Gray);

        public string RecordingIcon  => _isRecording ? "" : "";

        public SolidColorBrush RecordingColor => _isRecording
            ? new SolidColorBrush(Colors.Red)
            : new SolidColorBrush(Colors.Gray);

        public string BitrateDisplay => $"{_bitrate:F1} Mbps";

        /// Short filename shown in the footer chip when a local video is loaded.
        public string LocalVideoFileName =>
            string.IsNullOrEmpty(_localVideoPath) ? string.Empty : Path.GetFileName(_localVideoPath);

        /// True when a local video file is loaded — used to toggle the footer chip visibility.
        public bool HasLocalVideo => !string.IsNullOrEmpty(_localVideoPath);

        partial void OnLocalVideoPathChanged(string? value)
        {
            OnPropertyChanged(nameof(LocalVideoFileName));
            OnPropertyChanged(nameof(HasLocalVideo));
        }

        public string AlertSeverityText => _alertSeverityLevel switch
        {
            AlertSeverity.Critical => "KHẨN CẤP",
            AlertSeverity.High     => "NGUY HIỂM",
            AlertSeverity.Medium   => "TRUNG BÌNH",
            _                      => "THẤP"
        };

        public SolidColorBrush AlertSeverityBrush => _alertSeverityLevel switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
            AlertSeverity.High     => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 115, 22)),
            AlertSeverity.Medium   => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)),
            _                      => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94))
        };

        public string AlertTimeDisplay => _alertTime.ToString("HH:mm:ss dd/MM/yyyy");

        public void TriggerAlert(string title, string description, AlertSeverity severity, string location)
        {
            AlertTitle = title;
            AlertDescription = description;
            AlertSeverityLevel = severity;
            AlertLocation = location;
            AlertTime = DateTimeOffset.Now;
            AlertBorderOpacity = 1.0;
            HasAlert = true;

            OnPropertyChanged(nameof(AlertSeverityText));
            OnPropertyChanged(nameof(AlertSeverityBrush));
            OnPropertyChanged(nameof(AlertTimeDisplay));
        }

        public void DismissAlert()
        {
            HasAlert = false;
            AlertBorderOpacity = 0.0;
            AlertTitle = string.Empty;
            AlertDescription = string.Empty;
        }

        [RelayCommand]
        private void SetResolution(string? res)
        {
            if (!string.IsNullOrEmpty(res)) Resolution = res;
        }
    }
}
