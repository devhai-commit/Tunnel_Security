using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Station.Models;
using Station.Services;
using Windows.UI;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.Generic;
using LiveChartsCore.Kernel.Sketches;

namespace Station.ViewModels
{
    public enum AlertSidebarMode
    {
        Summary,
        History,
        Processing
    }

    public partial class AlertsViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSidebarSummaryVisible))]
        [NotifyPropertyChangedFor(nameof(IsSidebarHistoryVisible))]
        [NotifyPropertyChangedFor(nameof(IsSidebarProcessingVisible))]
        private AlertSidebarMode _sidebarMode = AlertSidebarMode.Summary;

        public bool IsSidebarSummaryVisible => SidebarMode == AlertSidebarMode.Summary;
        public bool IsSidebarHistoryVisible => SidebarMode == AlertSidebarMode.History;
        public bool IsSidebarProcessingVisible => SidebarMode == AlertSidebarMode.Processing;

        // Commands to switch modes
        [RelayCommand]
        private void ShowSummary() => SidebarMode = AlertSidebarMode.Summary;

        [RelayCommand]
        private void ShowHistory() => SidebarMode = AlertSidebarMode.History;

        [RelayCommand]
        private void ShowProcessing() => SidebarMode = AlertSidebarMode.Processing;
        #region Filter Properties

        private string _selectedLine = "Tất cả tuyến";
        public string SelectedLine
        {
            get => _selectedLine;
            set { SetProperty(ref _selectedLine, value); ApplyFilters(); }
        }

        private string _selectedNode = "Tất cả nút";
        public string SelectedNode
        {
            get => _selectedNode;
            set { SetProperty(ref _selectedNode, value); ApplyFilters(); }
        }

        private string _selectedSeverity = "Tất cả mức độ";
        public string SelectedSeverity
        {
            get => _selectedSeverity;
            set { SetProperty(ref _selectedSeverity, value); ApplyFilters(); }
        }

        private string _selectedStatus = "Tất cả trạng thái";
        public string SelectedStatus
        {
            get => _selectedStatus;
            set { SetProperty(ref _selectedStatus, value); ApplyFilters(); }
        }

        private string _selectedCategory = "Tất cả loại";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { SetProperty(ref _selectedCategory, value); ApplyFilters(); }
        }

        private string _selectedPeriod = "Hôm nay";
        public string SelectedPeriod
        {
            get => _selectedPeriod;
            set { SetProperty(ref _selectedPeriod, value); ApplyFilters(); CalculateStatistics(); }
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set { SetProperty(ref _searchQuery, value); ApplyFilters(); }
        }

        #endregion

        #region Collections

        public ObservableCollection<string> Lines { get; } = new();
        public ObservableCollection<string> Nodes { get; } = new();
        public ObservableCollection<string> Severities { get; } = new();
        public ObservableCollection<string> Statuses { get; } = new();
        public ObservableCollection<string> Categories { get; } = new();
        public ObservableCollection<string> Periods { get; } = new();

        public ObservableCollection<AlertItemViewModel> AllAlerts { get; } = new();
        public ObservableCollection<AlertItemViewModel> FilteredAlerts { get; } = new();

        #endregion

        #region Statistics

        private int _totalAlerts;
        public int TotalAlerts { get => _totalAlerts; set => SetProperty(ref _totalAlerts, value); }

        private int _filteredCount;
        public int FilteredCount { get => _filteredCount; set => SetProperty(ref _filteredCount, value); }

        private int _unprocessedCount;
        public int UnprocessedCount { get => _unprocessedCount; set => SetProperty(ref _unprocessedCount, value); }

        private int _acknowledgedCount;
        public int AcknowledgedCount { get => _acknowledgedCount; set => SetProperty(ref _acknowledgedCount, value); }

        private int _inProgressCount;
        public int InProgressCount { get => _inProgressCount; set => SetProperty(ref _inProgressCount, value); }

        private int _resolvedCount;
        public int ResolvedCount { get => _resolvedCount; set => SetProperty(ref _resolvedCount, value); }

        private int _criticalCount;
        public int CriticalCount { get => _criticalCount; set => SetProperty(ref _criticalCount, value); }

        private int _highCount;
        public int HighCount { get => _highCount; set => SetProperty(ref _highCount, value); }

        private int _mediumCount;
        public int MediumCount { get => _mediumCount; set => SetProperty(ref _mediumCount, value); }

        private int _lowCount;
        public int LowCount { get => _lowCount; set => SetProperty(ref _lowCount, value); }

        // Warning = High + Medium (matches HTML 3-card layout)
        public int WarningCount => HighCount + MediumCount;

        private int _todayCount;
        public int TodayCount { get => _todayCount; set => SetProperty(ref _todayCount, value); }

        private int _weekCount;
        public int WeekCount { get => _weekCount; set => SetProperty(ref _weekCount, value); }

        private int _monthCount;
        public int MonthCount { get => _monthCount; set => SetProperty(ref _monthCount, value); }

        #endregion

        #region Selected Alert

        private AlertItemViewModel? _selectedAlert;
        public AlertItemViewModel? SelectedAlert
        {
            get => _selectedAlert;
            set => SetProperty(ref _selectedAlert, value);
        }

        #endregion

        #region Charts

        public ISeries[] AlertTrendSeries { get; private set; } = Array.Empty<ISeries>();
        public IEnumerable<ICartesianAxis> AlertTrendXAxes { get; private set; } = Array.Empty<Axis>();
        public IEnumerable<ICartesianAxis> AlertTrendYAxes { get; private set; } = Array.Empty<Axis>();

        public ISeries[] SeverityPieSeries { get; private set; } = Array.Empty<ISeries>();

        #endregion

        // API mode support
        private ApiSensorClient? _apiClient;
        private bool _useApiMode;

        public AlertsViewModel() : this(useApi: false)
        {
        }

        public AlertsViewModel(bool useApi)
        {
            _useApiMode = useApi;

            if (_useApiMode)
            {
                InitializeApiMode();
            }
            else
            {
                InitializeLocalMode();
            }

            InitializeFilters();
            LoadMockAlerts();
            ApplyFilters();
            CalculateStatistics();
            InitializeCharts();
        }

        private async void InitializeApiMode()
        {
            try
            {
                _apiClient = new ApiSensorClient();
                _apiClient.SensorUpdated += OnApiSensorUpdated;
                await _apiClient.ConnectAsync();
                System.Diagnostics.Debug.WriteLine("[AlertsViewModel] Connected to API for real-time updates");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlertsViewModel] Failed to connect to API: {ex.Message}");
                // Fall back to local mode
                InitializeLocalMode();
            }
        }

        private void InitializeLocalMode()
        {
            // Subscribe to local MockDataService (existing behavior)
            DataServiceLocator.Current.AlertGenerated += OnLocalAlertGenerated;
        }

        private void OnApiSensorUpdated(object? sender, ApiSensorUpdate e)
        {
            // Check thresholds and generate alert if needed
            // This requires threshold info - for now we just track the updates
            System.Diagnostics.Debug.WriteLine($"[AlertsViewModel] API Sensor update: {e.Name} = {e.CurrentValue} {e.Unit}");
        }

        private void OnLocalAlertGenerated(object? sender, AlertGeneratedEventArgs e)
        {
            // Handle local alert generation (existing behavior)
        }

        private void InitializeFilters()
        {
            // Lines
            Lines.Add("Tất cả tuyến");
            Lines.Add("Cống Xuân Thủy");
            Lines.Add("Cống Cầu Giấy");
            Lines.Add("Cống Trần Thái Tông");
            Lines.Add("Cống Duy Tân");
            Lines.Add("Cống Phạm Văn Đồng");
            Lines.Add("Cống Nguyễn Phong Sắc");

            // Nodes - will update based on selected line
            Nodes.Add("Tất cả nút");

            // Severities
            Severities.Add("Tất cả mức độ");
            Severities.Add("Nghiêm trọng");
            Severities.Add("Cao");
            Severities.Add("Trung bình");
            Severities.Add("Thấp");

            // Statuses
            Statuses.Add("Tất cả trạng thái");
            Statuses.Add("Chưa xử lý");
            Statuses.Add("Đã xác nhận");
            Statuses.Add("Đang xử lý");
            Statuses.Add("Đã giải quyết");
            Statuses.Add("Đã đóng");

            // Categories
            Categories.Add("Tất cả loại");
            Categories.Add("Radar");
            Categories.Add("Hồng ngoại");
            Categories.Add("Nhiệt độ");
            Categories.Add("Độ ẩm");
            Categories.Add("Ánh sáng");
            Categories.Add("Gia tốc");
            Categories.Add("Xâm nhập");
            Categories.Add("Thiết bị");
            Categories.Add("Kết nối");

            // Periods
            Periods.Add("Hôm nay");
            Periods.Add("7 ngày qua");
            Periods.Add("30 ngày qua");
            Periods.Add("Tất cả");
        }

        private void LoadMockAlerts()
        {
            var mockAlerts = new List<AlertItemViewModel>
            {
                // Critical alerts
                // new()
                // {
                //     Id = "ALR-001",
                //     Title = "Rung động bất thường tại Nút 2-03",
                //     Description = "Gia tốc kế ghi nhận 3.8 m/s².",
                //     Category = AlertCategory.Accelerometer,
                //     Severity = AlertSeverity.High,
                //     State = AlertState.Unprocessed,
                //     LineId = "LINE-02", LineName = "Tuyến cống Nghĩa Đô",
                //     NodeId = "NODE-L2-03", NodeName = "Nút 2-03",
                //     SensorId = "ACC-L2-N03", SensorName = "Gia tốc kế Nút 2-03",
                //     SensorType = "Accelerometer", SensorValue = 3.8, SensorUnit = "m/s²", Threshold = 3.0,
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-12)
                // },
                // new()
                // {
                //     Id = "ALR-002",
                //     Title = "Radar phát hiện người tại Nút 1-01",
                //     Description = "Radar ghi nhận xác suất hiện diện 78%. Cần xác minh.",
                //     Category = AlertCategory.Radar,
                //     Severity = AlertSeverity.High,
                //     State = AlertState.Unprocessed,
                //     LineId = "LINE-01", LineName = "Tuyến cống Hoàng Quốc Việt",
                //     NodeId = "NODE-L1-01", NodeName = "Nút 1-01",
                //     SensorId = "RAD-L1-N01", SensorName = "Radar Nút 1-01",
                //     SensorType = "Radar", SensorValue = 78, SensorUnit = "%", Threshold = 60,
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-5)
                // },

                // // High alerts
                // new()
                // {
                //     Id = "ALR-003",
                //     Title = "Nhiệt độ cao bất thường",
                //     Description = "Nhiệt độ tại cống XT-2 tăng cao bất thường (45°C > 40°C), có thể do cháy hoặc sự cố.",
                //     Category = AlertCategory.Temperature,
                //     Severity = AlertSeverity.High,
                //     State = AlertState.InProgress,
                //     LineId = "L1", LineName = "Cống Xuân Thủy",
                //     NodeId = "XT-2", NodeName = "Cống Xuân Thủy 2",
                //     SensorId = "S-XT2-TEMP", SensorName = "Temperature Sensor",
                //     SensorType = "Temperature", SensorValue = 45, SensorUnit = "°C", Threshold = 40,
                //     CameraId = "CAM-XT-3",
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-25),
                //     AcknowledgedAt = DateTimeOffset.Now.AddMinutes(-20),
                //     AcknowledgedBy = "Trần Văn B",
                //     Notes = new List<AlertNote>
                //     {
                //         new() { Content = "Đã cử đội kỹ thuật kiểm tra", Author = "Trần Văn B", CreatedAt = DateTimeOffset.Now.AddMinutes(-18) }
                //     },
                //     Lng = 105.8025, Lat = 21.0375
                // },
                // new()
                // {
                //     Id = "ALR-004",
                //     Title = "Phát hiện xâm nhập",
                //     Description = "Camera phát hiện đối tượng lạ tại khu vực hố ga CG-2.",
                //     Category = AlertCategory.Intrusion,
                //     Severity = AlertSeverity.High,
                //     State = AlertState.Unprocessed,
                //     LineId = "L2", LineName = "Cống Cầu Giấy",
                //     NodeId = "CG-2", NodeName = "Cống Cầu Giấy 2",
                //     CameraId = "CAM-CG-2",
                //     SnapshotPath = "/snapshots/intrusion_cg2_001.jpg",
                //     VideoClipPath = "/videos/intrusion_cg2_001.mp4",
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-8),
                //     Lng = 105.7985, Lat = 21.0445
                // },

                // // Medium alerts
                // new()
                // {
                //     Id = "ALR-005",
                //     Title = "Độ ẩm tăng cao",
                //     Description = "Độ ẩm tại PVD-1 vượt ngưỡng cảnh báo (92% > 85%).",
                //     Category = AlertCategory.Humidity,
                //     Severity = AlertSeverity.Medium,
                //     State = AlertState.Resolved,
                //     LineId = "L5", LineName = "Cống Phạm Văn Đồng",
                //     NodeId = "PVD-1", NodeName = "Cống Phạm Văn Đồng 1",
                //     SensorId = "S-PVD1-HUM", SensorName = "Humidity Sensor",
                //     SensorType = "Humidity", SensorValue = 92, SensorUnit = "%", Threshold = 85,
                //     CreatedAt = DateTimeOffset.Now.AddHours(-2),
                //     AcknowledgedAt = DateTimeOffset.Now.AddHours(-1).AddMinutes(-50),
                //     ResolvedAt = DateTimeOffset.Now.AddHours(-1),
                //     ResolvedBy = "Lê Văn C",
                //     Notes = new List<AlertNote>
                //     {
                //         new() { Content = "Kiểm tra - do mưa lớn tối qua", Author = "Lê Văn C", CreatedAt = DateTimeOffset.Now.AddHours(-1).AddMinutes(-30) },
                //         new() { Content = "Độ ẩm đã giảm về mức bình thường", Author = "Lê Văn C", CreatedAt = DateTimeOffset.Now.AddHours(-1) }
                //     },
                //     Lng = 105.8030, Lat = 21.0445
                // },
                // new()
                // {
                //     Id = "ALR-006",
                //     Title = "Rung động bất thường tại Nút 2-03",
                //     Description = "Gia tốc kế ghi nhận 3.8 m/s².",
                //     Category = AlertCategory.Accelerometer,
                //     Severity = AlertSeverity.High,
                //     State = AlertState.Unprocessed,
                //     LineId = "LINE-02", LineName = "Tuyến cống Nghĩa Đô",
                //     NodeId = "NODE-L2-03", NodeName = "Nút 2-03",
                //     SensorId = "ACC-L2-N03", SensorName = "Gia tốc kế Nút 2-03",
                //     SensorType = "Accelerometer", SensorValue = 3.8, SensorUnit = "m/s²", Threshold = 3.0,
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-35)
                // },
                // new()
                // {
                //     Id = "ALR-007",
                //     Title = "Cảm biến hồng ngoại kích hoạt tại Nút 3-02",
                //     Description = "PIR phát hiện chuyển động nhiệt trong khu vực hạn chế.",
                //     Category = AlertCategory.Infrared,
                //     Severity = AlertSeverity.Medium,
                //     State = AlertState.Acknowledged,
                //     LineId = "LINE-03", LineName = "Tuyến cống Xuân La",
                //     NodeId = "NODE-L3-02", NodeName = "Nút 3-02",
                //     SensorId = "PIR-L3-N02", SensorName = "Hồng ngoại Nút 3-02",
                //     SensorType = "Infrared", SensorValue = 65, SensorUnit = "%", Threshold = 60,
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-28)
                // },

                // // Low alerts
                // new()
                // {
                //     Id = "ALR-008",
                //     Title = "Pin yếu - Nút NPS-1",
                //     Description = "Pin của nút NPS-1 còn 15%, cần thay thế sớm.",
                //     Category = AlertCategory.Equipment,
                //     Severity = AlertSeverity.Low,
                //     State = AlertState.Closed,
                //     LineId = "L6", LineName = "Cống Nguyễn Phong Sắc",
                //     NodeId = "NPS-1", NodeName = "Cống Nguyễn Phong Sắc 1",
                //     CreatedAt = DateTimeOffset.Now.AddDays(-1),
                //     AcknowledgedAt = DateTimeOffset.Now.AddDays(-1).AddHours(2),
                //     ResolvedAt = DateTimeOffset.Now.AddHours(-5),
                //     ClosedAt = DateTimeOffset.Now.AddHours(-4),
                //     ClosedBy = "Admin",
                //     Notes = new List<AlertNote>
                //     {
                //         new() { Content = "Đã thay pin mới", Author = "Kỹ thuật viên", CreatedAt = DateTimeOffset.Now.AddHours(-5) }
                //     },
                //     Lng = 105.7920, Lat = 21.0325
                // },
                // new()
                // {
                //     Id = "ALR-009",
                //     Title = "Mất kết nối tạm thời",
                //     Description = "Nút DT-1 mất kết nối trong 5 phút.",
                //     Category = AlertCategory.Connection,
                //     Severity = AlertSeverity.Low,
                //     State = AlertState.Resolved,
                //     LineId = "L4", LineName = "Cống Duy Tân",
                //     NodeId = "DT-1", NodeName = "Cống Duy Tân 1",
                //     CreatedAt = DateTimeOffset.Now.AddHours(-3),
                //     ResolvedAt = DateTimeOffset.Now.AddHours(-2).AddMinutes(-55),
                //     Notes = new List<AlertNote>
                //     {
                //         new() { Content = "Kết nối đã tự phục hồi", Author = "System", CreatedAt = DateTimeOffset.Now.AddHours(-2).AddMinutes(-55) }
                //     },
                //     Lng = 105.7892, Lat = 21.0345
                // },
                // new()
                // {
                //     Id = "ALR-010",
                //     Title = "Nhiệt độ tăng nhẹ",
                //     Description = "Nhiệt độ tại XT-1 tăng nhẹ trên mức bình thường (32°C > 30°C).",
                //     Category = AlertCategory.Temperature,
                //     Severity = AlertSeverity.Low,
                //     State = AlertState.Unprocessed,
                //     LineId = "L1", LineName = "Cống Xuân Thủy",
                //     NodeId = "XT-1", NodeName = "Cống Xuân Thủy 1",
                //     SensorId = "S-XT1-TEMP", SensorName = "Temperature Sensor",
                //     SensorType = "Temperature", SensorValue = 32, SensorUnit = "°C", Threshold = 30,
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-50),
                //     Lng = 105.8005, Lat = 21.0371
                // },

                // // More alerts for statistics
                // new()
                // {
                //     Id = "ALR-011",
                //     Title = "Radar phát hiện người tại Nút 1-01",
                //     Description = "Radar ghi nhận xác suất hiện diện 78%. Cần xác minh.",
                //     Category = AlertCategory.Radar,
                //     Severity = AlertSeverity.High,
                //     State = AlertState.InProgress,
                //     LineId = "LINE-01", LineName = "Tuyến cống Hoàng Quốc Việt",
                //     NodeId = "NODE-L1-01", NodeName = "Nút 1-01",
                //     SensorId = "RAD-L1-N01", SensorName = "Radar Nút 1-01",
                //     SensorType = "Radar", SensorValue = 78, SensorUnit = "%", Threshold = 60,
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-15),
                //     AcknowledgedAt = DateTimeOffset.Now.AddMinutes(-12),
                //     AcknowledgedBy = "Nguyễn Văn A"
                // },
                // new()
                // {
                //     Id = "ALR-012",
                //     Title = "Áp suất bất thường",
                //     Description = "Áp suất tại PVD-4 vượt ngưỡng cảnh báo.",
                //     Category = AlertCategory.Other,
                //     Severity = AlertSeverity.Medium,
                //     State = AlertState.Unprocessed,
                //     LineId = "L5", LineName = "Cống Phạm Văn Đồng",
                //     NodeId = "PVD-4", NodeName = "Cống Phạm Văn Đồng 4",
                //     SensorId = "S-PVD4-PRS", SensorName = "Pressure Sensor",
                //     SensorType = "Pressure", SensorValue = 2.8, SensorUnit = "bar", Threshold = 2.5,
                //     CameraId = "CAM-PVD-4",
                //     CreatedAt = DateTimeOffset.Now.AddMinutes(-28),
                //     Lng = 105.8095, Lat = 21.0525
                // }
            };

            foreach (var alert in mockAlerts)
            {
                alert.InitializeSelectedStatus();
                AllAlerts.Add(alert);
            }
        }

        private void ApplyFilters()
        {
            FilteredAlerts.Clear();

            var filtered = AllAlerts.AsEnumerable();

            // Filter by period
            var now = DateTimeOffset.Now;
            filtered = SelectedPeriod switch
            {
                "Hôm nay" => filtered.Where(a => a.CreatedAt.Date == now.Date),
                "7 ngày qua" => filtered.Where(a => a.CreatedAt >= now.AddDays(-7)),
                "30 ngày qua" => filtered.Where(a => a.CreatedAt >= now.AddDays(-30)),
                _ => filtered
            };

            // Filter by line
            if (SelectedLine != "Tất cả tuyến")
                filtered = filtered.Where(a => a.LineName == SelectedLine);

            // Filter by node
            if (SelectedNode != "Tất cả nút")
                filtered = filtered.Where(a => a.NodeName == SelectedNode);

            // Filter by severity
            if (SelectedSeverity != "Tất cả mức độ")
            {
                filtered = SelectedSeverity switch
                {
                    "Nghiêm trọng" => filtered.Where(a => a.Severity == AlertSeverity.Critical),
                    "Cao" => filtered.Where(a => a.Severity == AlertSeverity.High),
                    "Trung bình" => filtered.Where(a => a.Severity == AlertSeverity.Medium),
                    "Thấp" => filtered.Where(a => a.Severity == AlertSeverity.Low),
                    _ => filtered
                };
            }

            // Filter by status
            if (SelectedStatus != "Tất cả trạng thái")
            {
                filtered = SelectedStatus switch
                {
                    "Chưa xử lý" => filtered.Where(a => a.State == AlertState.Unprocessed),
                    "Đã xác nhận" => filtered.Where(a => a.State == AlertState.Acknowledged),
                    "Đang xử lý" => filtered.Where(a => a.State == AlertState.InProgress),
                    "Đã giải quyết" => filtered.Where(a => a.State == AlertState.Resolved),
                    "Đã đóng" => filtered.Where(a => a.State == AlertState.Closed),
                    _ => filtered
                };
            }

            // Filter by category
            if (SelectedCategory != "Tất cả loại")
            {
                filtered = SelectedCategory switch
                {
                    "Radar"      => filtered.Where(a => a.Category == AlertCategory.Radar),
                    "Hồng ngoại" => filtered.Where(a => a.Category == AlertCategory.Infrared),
                    "Nhiệt độ"   => filtered.Where(a => a.Category == AlertCategory.Temperature),
                    "Độ ẩm"      => filtered.Where(a => a.Category == AlertCategory.Humidity),
                    "Ánh sáng"   => filtered.Where(a => a.Category == AlertCategory.Light),
                    "Gia tốc"    => filtered.Where(a => a.Category == AlertCategory.Accelerometer),
                    "Xâm nhập"   => filtered.Where(a => a.Category == AlertCategory.Intrusion),
                    "Thiết bị"   => filtered.Where(a => a.Category == AlertCategory.Equipment),
                    "Kết nối"    => filtered.Where(a => a.Category == AlertCategory.Connection),
                    _ => filtered
                };
            }

            // Search query
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var query = SearchQuery.ToLower();
                filtered = filtered.Where(a =>
                    a.Title.ToLower().Contains(query) ||
                    a.Description.ToLower().Contains(query) ||
                    a.NodeName.ToLower().Contains(query) ||
                    a.LineName.ToLower().Contains(query));
            }

            // Order by severity (critical first) then by time (newest first)
            filtered = filtered
                .OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.CreatedAt);

            foreach (var alert in filtered)
                FilteredAlerts.Add(alert);

            FilteredCount = FilteredAlerts.Count;
            TotalAlerts = AllAlerts.Count;
        }

        private void CalculateStatistics()
        {
            var now = DateTimeOffset.Now;

            // Count by state
            UnprocessedCount = AllAlerts.Count(a => a.State == AlertState.Unprocessed);
            AcknowledgedCount = AllAlerts.Count(a => a.State == AlertState.Acknowledged);
            InProgressCount = AllAlerts.Count(a => a.State == AlertState.InProgress);
            ResolvedCount = AllAlerts.Count(a => a.State == AlertState.Resolved);

            // Count by severity
            CriticalCount = AllAlerts.Count(a => a.Severity == AlertSeverity.Critical);
            HighCount = AllAlerts.Count(a => a.Severity == AlertSeverity.High);
            MediumCount = AllAlerts.Count(a => a.Severity == AlertSeverity.Medium);
            LowCount = AllAlerts.Count(a => a.Severity == AlertSeverity.Low);
            OnPropertyChanged(nameof(WarningCount));

            // Count by period
            TodayCount = AllAlerts.Count(a => a.CreatedAt.Date == now.Date);
            WeekCount = AllAlerts.Count(a => a.CreatedAt >= now.AddDays(-7));
            MonthCount = AllAlerts.Count(a => a.CreatedAt >= now.AddDays(-30));
        }

        private void InitializeCharts()
        {
            // Alert trend - last 7 days
            var now = DateTimeOffset.Now;
            var trendData = new List<double>();
            var labels = new List<string>();

            for (int i = 6; i >= 0; i--)
            {
                var date = now.AddDays(-i).Date;
                var count = AllAlerts.Count(a => a.CreatedAt.Date == date);
                trendData.Add(count > 0 ? count : new Random().Next(2, 8)); // Mock data
                labels.Add(date.ToString("dd/MM"));
            }

            AlertTrendSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = trendData,
                    Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(40)),
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                    GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                    GeometryStroke = new SolidColorPaint(SKColors.White, 2),
                    GeometrySize = 8,
                    LineSmoothness = 0.3
                }
            };

            AlertTrendXAxes = new Axis[]
            {
                new Axis
                {
                    IsVisible = false // Hide X axis for sparkline effect
                }
            };

            AlertTrendYAxes = new Axis[]
            {
                new Axis
                {
                    IsVisible = false, // Hide Y axis for sparkline effect
                    MinLimit = 0
                }
            };

            // Severity pie chart
            SeverityPieSeries = new ISeries[]
            {
                new PieSeries<int>
                {
                    Values = new[] { CriticalCount > 0 ? CriticalCount : 2 },
                    Name = "Nghiêm trọng",
                    Fill = new SolidColorPaint(new SKColor(239, 68, 68)),
                    Pushout = 5
                },
                new PieSeries<int>
                {
                    Values = new[] { HighCount > 0 ? HighCount : 4 },
                    Name = "Cao",
                    Fill = new SolidColorPaint(new SKColor(249, 115, 22))
                },
                new PieSeries<int>
                {
                    Values = new[] { MediumCount > 0 ? MediumCount : 5 },
                    Name = "Trung bình",
                    Fill = new SolidColorPaint(new SKColor(234, 179, 8))
                },
                new PieSeries<int>
                {
                    Values = new[] { LowCount > 0 ? LowCount : 3 },
                    Name = "Thấp",
                    Fill = new SolidColorPaint(new SKColor(34, 197, 94))
                }
            };
        }

        #region Commands

        [RelayCommand]
        private void SelectAlert(AlertItemViewModel alert)
        {
            SelectedAlert = alert;
        }

        [RelayCommand]
        private void AcknowledgeAlert(AlertItemViewModel alert)
        {
            if (alert.State == AlertState.Unprocessed)
            {
                alert.State = AlertState.Acknowledged;
                alert.AcknowledgedAt = DateTimeOffset.Now;
                alert.AcknowledgedBy = "Current User"; // TODO: Get from auth
                alert.InitializeSelectedStatus();
                CalculateStatistics();
            }
        }

        [RelayCommand]
        private void StartProcessing(AlertItemViewModel alert)
        {
            if (alert.State == AlertState.Acknowledged || alert.State == AlertState.Unprocessed)
            {
                alert.State = AlertState.InProgress;
                alert.InitializeSelectedStatus();
                CalculateStatistics();
            }
        }

        [RelayCommand]
        private void ResolveAlert(AlertItemViewModel alert)
        {
            if (alert.State != AlertState.Resolved && alert.State != AlertState.Closed)
            {
                alert.State = AlertState.Resolved;
                alert.ResolvedAt = DateTimeOffset.Now;
                alert.ResolvedBy = "Current User";
                alert.InitializeSelectedStatus();
                CalculateStatistics();
            }
        }

        [RelayCommand]
        private void CloseAlert(AlertItemViewModel alert)
        {
            if (alert.State == AlertState.Resolved)
            {
                alert.State = AlertState.Closed;
                alert.ClosedAt = DateTimeOffset.Now;
                alert.ClosedBy = "Current User";
                alert.InitializeSelectedStatus();
                CalculateStatistics();
            }
        }

        [RelayCommand]
        private void AddNote(string noteContent)
        {
            if (SelectedAlert != null && !string.IsNullOrWhiteSpace(noteContent))
            {
                SelectedAlert.Notes.Add(new AlertNote
                {
                    Content = noteContent,
                    Author = "Current User",
                    CreatedAt = DateTimeOffset.Now
                });
                OnPropertyChanged(nameof(SelectedAlert));
            }
        }

        [RelayCommand]
        private void RefreshAlerts()
        {
            // TODO: Call API to refresh alerts
            ApplyFilters();
            CalculateStatistics();
        }

        [RelayCommand]
        private void CloseSidebar()
        {
            SelectedAlert = null;
        }

        [RelayCommand]
        private void ClearFilters()
        {
            SelectedLine = "Tất cả tuyến";
            SelectedNode = "Tất cả nút";
            SelectedSeverity = "Tất cả mức độ";
            SelectedStatus = "Tất cả trạng thái";
            SelectedCategory = "Tất cả loại";
            SelectedPeriod = "Hôm nay";
            SearchQuery = string.Empty;
        }

        /// <summary>Called from code-behind on UI thread when MockDataService fires AlertGenerated.</summary>
        public void AddLiveAlert(Station.Models.Alert alert)
        {
            var vm = new AlertItemViewModel
            {
                Id = alert.Id,
                Title = alert.Title,
                Description = alert.Description,
                Severity = alert.Severity,
                State = AlertState.Unprocessed,
                LineId = alert.LineId,
                LineName = string.IsNullOrEmpty(alert.LineName) ? alert.NodeId : alert.LineName,
                NodeId = alert.NodeId,
                NodeName = string.IsNullOrEmpty(alert.NodeName) ? alert.NodeId : alert.NodeName,
                SensorId = alert.SensorId,
                SensorName = alert.SensorName,
                SensorType = alert.SensorType,
                SensorValue = alert.SensorValue,
                SensorUnit = alert.SensorUnit,
                Threshold = alert.Threshold,
                CameraId = alert.CameraId,
                CreatedAt = alert.CreatedAt,
                Lng = alert.Lng,
                Lat = alert.Lat
            };
            vm.InitializeSelectedStatus();
            AllAlerts.Insert(0, vm);
            ApplyFilters();
            CalculateStatistics();
        }

        #endregion
    }

    #region AlertItemViewModel

    public partial class AlertItemViewModel : ObservableObject
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AlertCategory Category { get; set; }
        public AlertSeverity Severity { get; set; }

        private AlertState _state;
        public AlertState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(StateText));
                    OnPropertyChanged(nameof(StateIcon));
                    OnPropertyChanged(nameof(StateColor));
                    OnPropertyChanged(nameof(StateBgColor));
                    OnPropertyChanged(nameof(StateDarkBgBrush));
                    OnPropertyChanged(nameof(StateDarkBorderBrush));
                    OnPropertyChanged(nameof(CanAcknowledge));
                    OnPropertyChanged(nameof(CanProcess));
                    OnPropertyChanged(nameof(CanResolve));
                    OnPropertyChanged(nameof(CanClose));
                    UpdateSelectedStatusFromState();
                }
            }
        }

        // Location
        public string LineId { get; set; } = string.Empty;
        public string LineName { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;

        // Sensor data
        public string? SensorId { get; set; }
        public string? SensorName { get; set; }
        public string? SensorType { get; set; }
        public double? SensorValue { get; set; }
        public string? SensorUnit { get; set; }
        public double? Threshold { get; set; }

        // Camera
        public string? CameraId { get; set; }
        public string? SnapshotPath { get; set; }
        public string? VideoClipPath { get; set; }

        // Timestamps
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }

        // Processing
        public string? AcknowledgedBy { get; set; }
        public string? ResolvedBy { get; set; }
        public string? ClosedBy { get; set; }
        public string? Note { get; set; }
        public List<AlertNote> Notes { get; set; } = new();

        // Coordinates
        public double? Lng { get; set; }
        public double? Lat { get; set; }

        // Status selection
        private AlertStatusOption? _selectedStatus;
        public AlertStatusOption? SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                if (value != null && _selectedStatus?.State != value.State)
                {
                    if (SetProperty(ref _selectedStatus, value))
                    {
                        _state = value.State;
                        OnPropertyChanged(nameof(State));
                        OnPropertyChanged(nameof(StateText));
                        OnPropertyChanged(nameof(StateIcon));
                        OnPropertyChanged(nameof(StateColor));
                        OnPropertyChanged(nameof(StateBgColor));
                    }
                }
            }
        }

        public ObservableCollection<AlertStatusOption> AvailableStatuses { get; } = new();

        public AlertItemViewModel()
        {
            InitializeAvailableStatuses();
        }

        private void InitializeAvailableStatuses()
        {
            AvailableStatuses.Add(new AlertStatusOption { State = AlertState.Unprocessed, Text = "Chưa xử lý", Icon = "\uE711" });
            AvailableStatuses.Add(new AlertStatusOption { State = AlertState.Acknowledged, Text = "Đã xác nhận", Icon = "\uE73E" });
            AvailableStatuses.Add(new AlertStatusOption { State = AlertState.InProgress, Text = "Đang xử lý", Icon = "\uE768" });
            AvailableStatuses.Add(new AlertStatusOption { State = AlertState.Resolved, Text = "Đã giải quyết", Icon = "\uE930" });
            AvailableStatuses.Add(new AlertStatusOption { State = AlertState.Closed, Text = "Đã đóng", Icon = "\uE8BB" });
        }

        public void InitializeSelectedStatus()
        {
            _selectedStatus = AvailableStatuses.FirstOrDefault(s => s.State == _state);
            OnPropertyChanged(nameof(SelectedStatus));
        }

        private void UpdateSelectedStatusFromState()
        {
            if (AvailableStatuses.Any())
            {
                _selectedStatus = AvailableStatuses.FirstOrDefault(s => s.State == _state);
                OnPropertyChanged(nameof(SelectedStatus));
            }
        }

        #region Computed Properties

        public string TimeAgo
        {
            get
            {
                var diff = DateTimeOffset.Now - CreatedAt;
                if (diff.TotalMinutes < 1) return "Vừa xong";
                if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} phút trước";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} giờ trước";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} ngày trước";
                return CreatedAt.ToString("dd/MM/yyyy HH:mm");
            }
        }

        public string LocationText => $"{LineName} • {NodeName}";

        public string SensorValueText => SensorValue.HasValue && Threshold.HasValue
            ? $"{SensorValue:F1} {SensorUnit} (ngưỡng: {Threshold:F1})"
            : "N/A";

        public bool HasCamera => !string.IsNullOrEmpty(CameraId);
        public bool HasSensor => !string.IsNullOrEmpty(SensorId);
        public bool HasNotes => Notes.Any();

        public bool CanAcknowledge => State == AlertState.Unprocessed;
        public bool CanProcess => State == AlertState.Acknowledged || State == AlertState.Unprocessed;
        public bool CanResolve => State == AlertState.InProgress || State == AlertState.Acknowledged;
        public bool CanClose => State == AlertState.Resolved;

        #endregion

        #region Colors & Icons

        public string CategoryIcon => Category switch
        {
            AlertCategory.Radar         => "\uE701",
            AlertCategory.Infrared      => "\uE7C1",
            AlertCategory.Temperature   => "\uE9CA", // Thermometer
            AlertCategory.Humidity      => "\uE81E", // Drop
            AlertCategory.Light         => "\uE706",
            AlertCategory.Accelerometer => "\uEDA4",
            AlertCategory.Intrusion     => "\uE785",  // Shield
            AlertCategory.Equipment     => "\uE950",  // Device
            AlertCategory.Connection    => "\uE839",  // Wifi
            _ => "\uE7BA"
        };

        public string CategoryText => Category switch
        {
            AlertCategory.Radar         => "Radar",
            AlertCategory.Infrared      => "Hồng ngoại",
            AlertCategory.Temperature   => "Nhiệt độ",
            AlertCategory.Humidity      => "Độ ẩm",
            AlertCategory.Light         => "Ánh sáng",
            AlertCategory.Accelerometer => "Gia tốc",
            AlertCategory.Intrusion     => "Xâm nhập",
            AlertCategory.Equipment     => "Thiết bị",
            AlertCategory.Connection    => "Kết nối",
            _ => "Khác"
        };

        public string SeverityIcon => Severity switch
        {
            AlertSeverity.Critical => "\uEB90",
            AlertSeverity.High => "\uE783",
            AlertSeverity.Medium => "\uE7BA",
            AlertSeverity.Low => "\uE946",
            _ => "\uE7BA"
        };

        public string SeverityText => Severity switch
        {
            AlertSeverity.Critical => "Nghiêm trọng",
            AlertSeverity.High => "Cao",
            AlertSeverity.Medium => "Trung bình",
            AlertSeverity.Low => "Thấp",
            _ => "Không xác định"
        };

        public SolidColorBrush SeverityColor => Severity switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Color.FromArgb(255, 220, 38, 38)),   // Red-600
            AlertSeverity.High => new SolidColorBrush(Color.FromArgb(255, 234, 88, 12)),      // Orange-600
            AlertSeverity.Medium => new SolidColorBrush(Color.FromArgb(255, 202, 138, 4)),    // Yellow-600
            AlertSeverity.Low => new SolidColorBrush(Color.FromArgb(255, 22, 163, 74)),       // Green-600
            _ => new SolidColorBrush(Color.FromArgb(255, 107, 114, 128))
        };

        public SolidColorBrush SeverityBgColor => Severity switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Color.FromArgb(255, 254, 226, 226)), // Red-100
            AlertSeverity.High => new SolidColorBrush(Color.FromArgb(255, 255, 237, 213)),    // Orange-100
            AlertSeverity.Medium => new SolidColorBrush(Color.FromArgb(255, 254, 249, 195)), // Yellow-100
            AlertSeverity.Low => new SolidColorBrush(Color.FromArgb(255, 220, 252, 231)),    // Green-100
            _ => new SolidColorBrush(Color.FromArgb(255, 243, 244, 246))
        };

        public SolidColorBrush SeverityTextColor => Severity switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Color.FromArgb(255, 153, 27, 27)),  // Red-800
            AlertSeverity.High => new SolidColorBrush(Color.FromArgb(255, 154, 52, 18)),     // Orange-800
            AlertSeverity.Medium => new SolidColorBrush(Color.FromArgb(255, 133, 77, 14)),   // Yellow-800
            AlertSeverity.Low => new SolidColorBrush(Color.FromArgb(255, 22, 101, 52)),      // Green-800
            _ => new SolidColorBrush(Color.FromArgb(255, 55, 65, 81))
        };

        public string StateIcon => State switch
        {
            AlertState.Unprocessed => "\uE711",
            AlertState.Acknowledged => "\uE73E",
            AlertState.InProgress => "\uE768",
            AlertState.Resolved => "\uE930",
            AlertState.Closed => "\uE8BB",
            _ => "\uE946"
        };

        public string StateText => State switch
        {
            AlertState.Unprocessed => "Chưa xử lý",
            AlertState.Acknowledged => "Đã xác nhận",
            AlertState.InProgress => "Đang xử lý",
            AlertState.Resolved => "Đã giải quyết",
            AlertState.Closed => "Đã đóng",
            _ => "Không xác định"
        };

        public SolidColorBrush StateColor => State switch
        {
            AlertState.Unprocessed => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),   // Red
            AlertState.Acknowledged => new SolidColorBrush(Color.FromArgb(255, 59, 130, 246)), // Blue
            AlertState.InProgress => new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),  // Amber
            AlertState.Resolved => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),     // Green
            AlertState.Closed => new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),     // Gray
            _ => new SolidColorBrush(Color.FromArgb(255, 107, 114, 128))
        };

        public SolidColorBrush StateBgColor => State switch
        {
            AlertState.Unprocessed => new SolidColorBrush(Color.FromArgb(255, 254, 226, 226)),
            AlertState.Acknowledged => new SolidColorBrush(Color.FromArgb(255, 219, 234, 254)),
            AlertState.InProgress => new SolidColorBrush(Color.FromArgb(255, 254, 243, 199)),
            AlertState.Resolved => new SolidColorBrush(Color.FromArgb(255, 220, 252, 231)),
            AlertState.Closed => new SolidColorBrush(Color.FromArgb(255, 243, 244, 246)),
            _ => new SolidColorBrush(Color.FromArgb(255, 243, 244, 246))
        };

        // ── Dark-mode tinted colors (matching alert.html) ───────────────────

        public SolidColorBrush SeverityDarkBgBrush => Severity switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Color.FromArgb(26, 239, 68, 68)),
            AlertSeverity.High     => new SolidColorBrush(Color.FromArgb(26, 249, 115, 22)),
            AlertSeverity.Medium   => new SolidColorBrush(Color.FromArgb(26, 234, 179, 8)),
            AlertSeverity.Low      => new SolidColorBrush(Color.FromArgb(26, 59, 130, 246)),
            _                      => new SolidColorBrush(Color.FromArgb(26, 107, 114, 128))
        };

        public SolidColorBrush SeverityDarkBorderBrush => Severity switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Color.FromArgb(51, 239, 68, 68)),
            AlertSeverity.High     => new SolidColorBrush(Color.FromArgb(51, 249, 115, 22)),
            AlertSeverity.Medium   => new SolidColorBrush(Color.FromArgb(51, 234, 179, 8)),
            AlertSeverity.Low      => new SolidColorBrush(Color.FromArgb(51, 59, 130, 246)),
            _                      => new SolidColorBrush(Color.FromArgb(51, 107, 114, 128))
        };

        public SolidColorBrush SeverityDarkTextBrush => Severity switch
        {
            AlertSeverity.Critical => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),
            AlertSeverity.High     => new SolidColorBrush(Color.FromArgb(255, 249, 115, 22)),
            AlertSeverity.Medium   => new SolidColorBrush(Color.FromArgb(255, 234, 179, 8)),
            AlertSeverity.Low      => new SolidColorBrush(Color.FromArgb(255, 59, 130, 246)),
            _                      => new SolidColorBrush(Color.FromArgb(255, 107, 114, 128))
        };

        public SolidColorBrush StateDarkBgBrush => State switch
        {
            AlertState.Unprocessed  => new SolidColorBrush(Color.FromArgb(13, 239, 68, 68)),
            AlertState.Acknowledged => new SolidColorBrush(Color.FromArgb(13, 59, 130, 246)),
            AlertState.InProgress   => new SolidColorBrush(Color.FromArgb(13, 245, 158, 11)),
            AlertState.Resolved     => new SolidColorBrush(Color.FromArgb(13, 34, 197, 94)),
            AlertState.Closed       => new SolidColorBrush(Color.FromArgb(13, 107, 114, 128)),
            _                       => new SolidColorBrush(Color.FromArgb(13, 107, 114, 128))
        };

        public SolidColorBrush StateDarkBorderBrush => State switch
        {
            AlertState.Unprocessed  => new SolidColorBrush(Color.FromArgb(26, 239, 68, 68)),
            AlertState.Acknowledged => new SolidColorBrush(Color.FromArgb(26, 59, 130, 246)),
            AlertState.InProgress   => new SolidColorBrush(Color.FromArgb(26, 245, 158, 11)),
            AlertState.Resolved     => new SolidColorBrush(Color.FromArgb(26, 34, 197, 94)),
            AlertState.Closed       => new SolidColorBrush(Color.FromArgb(26, 107, 114, 128)),
            _                       => new SolidColorBrush(Color.FromArgb(26, 107, 114, 128))
        };

        public string CreatedAtFormatted => CreatedAt.ToString("dd/MM/yyyy - HH:mm");

        #endregion
    }

    #endregion

    #region Supporting Classes

    public class AlertStatusOption
    {
        public AlertState State { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    #endregion
}
