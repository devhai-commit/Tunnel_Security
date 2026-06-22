using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Station.Models;
using Station.Services;

namespace Station.ViewModels
{
    public partial class MonitoringDashboardViewModel : ObservableObject
    {
        [ObservableProperty] private string currentTime = DateTime.Now.ToString("HH:mm:ss");
        [ObservableProperty] private string currentDate = DateTime.Now.ToString("dd/MM/yyyy");
        [ObservableProperty] private int totalNodes;
        [ObservableProperty] private int activeNodes;
        [ObservableProperty] private int offlineNodes;
        [ObservableProperty] private int todayAlerts;
        [ObservableProperty] private int criticalAlerts;
        [ObservableProperty] private int warningAlerts;
        [ObservableProperty] private double averageTemperature;
        [ObservableProperty] private double maxTemperature;
        [ObservableProperty] private string systemStatus = "Hoạt động bình thường";
        [ObservableProperty] private string networkLatency = "12ms";
        [ObservableProperty] private double cpuUsage = 45.2;
        [ObservableProperty] private double memoryUsage = 68.7;

        // ── Chart series (set once, never reassigned) ─────────────────
        public ISeries[] AlertDistributionSeries { get; private set; } = Array.Empty<ISeries>();
        public ISeries[] TemperatureSeries { get; private set; } = Array.Empty<ISeries>();
        public ISeries[] HumiditySeries    { get; private set; } = Array.Empty<ISeries>();
        public ISeries[] VibrationSeries   { get; private set; } = Array.Empty<ISeries>();
        public ISeries[] WaterLevelSeries  { get; private set; } = Array.Empty<ISeries>();
        public ISeries[] LightSeries       { get; private set; } = Array.Empty<ISeries>();

        // Hidden axes for mini charts
        public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> HiddenXAxis { get; set; }
            = Array.Empty<LiveChartsCore.Kernel.Sketches.ICartesianAxis>();
        public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> HiddenYAxis { get; set; }
            = Array.Empty<LiveChartsCore.Kernel.Sketches.ICartesianAxis>();

        // Alert type counts
        [ObservableProperty] private int intruAlerts;
        [ObservableProperty] private int motionAlerts;
        [ObservableProperty] private int fireAlerts;
        [ObservableProperty] private int smokeAlerts;
        [ObservableProperty] private int waterAlerts;

        // Device counts
        [ObservableProperty] private int totalCameras;
        [ObservableProperty] private int onlineCameras;
        [ObservableProperty] private int totalSensors;
        [ObservableProperty] private int onlineSensors;
        [ObservableProperty] private double systemHealth;
        [ObservableProperty] private string systemHealthText = "0%";
        [ObservableProperty] private string cameraCountText = "0 / 0";
        [ObservableProperty] private string sensorCountText = "0 / 0";
        [ObservableProperty] private int anomalyCount;
        [ObservableProperty] private int activeAlertCount;

        // Sensor category averages
        [ObservableProperty] private double averageHumidity;
        [ObservableProperty] private double averageVibration;
        [ObservableProperty] private double averageGasLevel;
        [ObservableProperty] private double averageWaterLevel;
        [ObservableProperty] private string averageHumidityText  = "0%";
        [ObservableProperty] private string averageVibrationText = "0 mm/s";
        [ObservableProperty] private string averageGasLevelText  = "0 ppm";

        public ObservableCollection<NodeStatus>         Nodes               { get; } = new();
        public ObservableCollection<RealtimeAlert>      RealtimeAlerts      { get; } = new();
        public ObservableCollection<TemperatureReading> TemperatureReadings { get; } = new();
        public ObservableCollection<CameraStatus>       Cameras             { get; } = new();

        // ── FIX 1: Persistent ObservableValue collections for mini charts ──
        // LiveCharts receives Add/Remove on these → only re-renders the changed point.
        // No new array allocation per tick, no full-redraw per tick.
        private readonly ObservableCollection<ObservableValue> _tempValues;
        private readonly ObservableCollection<ObservableValue> _humValues;
        private readonly ObservableCollection<ObservableValue> _vibValues;
        private readonly ObservableCollection<ObservableValue> _waterValues;
        private readonly ObservableCollection<ObservableValue> _lightValues;

        // ── FIX 2: Persistent ObservableValues for pie chart ──────────────
        // PieSeries[] is created once; only .Value is mutated on each alert.
        private readonly ObservableValue _pieIntru       = new(0);
        private readonly ObservableValue _pieMotion      = new(0);
        private readonly ObservableValue _pieFire        = new(0);
        private readonly ObservableValue _pieSmoke       = new(0);
        private readonly ObservableValue _piePlaceholder = new(1);

        // ── FIX 3: Throttle — each chart updates at most once per 2s ──────
        private static readonly TimeSpan _chartInterval = TimeSpan.FromSeconds(2);
        private DateTime _lastTempChart  = DateTime.MinValue;
        private DateTime _lastHumChart   = DateTime.MinValue;
        private DateTime _lastVibChart   = DateTime.MinValue;
        private DateTime _lastWaterChart = DateTime.MinValue;
        private DateTime _lastLightChart = DateTime.MinValue;

        private readonly IDataService _mock = DataServiceLocator.Current;
        private readonly DispatcherQueue? _dispatcherQueue;
        private readonly Random _rng = new();

        public MonitoringDashboardViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Build persistent sliding-window collections (24 points each)
            _tempValues  = MakeValueCollection(24, 24.0);
            _humValues   = MakeValueCollection(24, 55.0);
            _vibValues   = MakeValueCollection(24, 5.0);
            _waterValues = MakeValueCollection(24, 8.0);
            _lightValues = MakeValueCollection(24, 150.0);

            InitializeMiniCharts();
            InitializePieChart();
            LoadFromMockData();
            RefreshAlertCounts();
            SyncPieValues();   // sync pie to initial alert counts

            _mock.SensorTick    += OnSensorTick;
            _mock.AlertGenerated += OnAlertGenerated;

            StartClockUpdates();
        }

        // ── Load initial state ────────────────────────────────────────

        private void LoadFromMockData()
        {
            var sensors  = _mock.Sensors;
            var cameras  = _mock.Cameras;
            var allNodes = _mock.Lines.SelectMany(l => l.Nodes).ToList();

            TotalNodes   = allNodes.Count;
            ActiveNodes  = allNodes.Count(n => sensors.Any(s => s.NodeId == n.NodeId && s.IsOnline));
            OfflineNodes = allNodes.Count(n => sensors.All(s => s.NodeId == n.NodeId && !s.IsOnline));

            TotalCameras  = cameras.Count;
            OnlineCameras = cameras.Count(c => c.IsOnline);
            TotalSensors  = sensors.Count;
            OnlineSensors = sensors.Count(s => s.IsOnline);

            int totalDevices = TotalCameras + TotalSensors;
            SystemHealth     = totalDevices > 0
                ? Math.Round((OnlineCameras + OnlineSensors) * 100.0 / totalDevices, 0)
                : 0;
            SystemHealthText = $"{SystemHealth:F0}%";
            CameraCountText  = $"{OnlineCameras} / {TotalCameras}";
            SensorCountText  = $"{OnlineSensors} / {TotalSensors}";
            AnomalyCount     = sensors.Count(s => s.CurrentLevel >= SensorAlertLevel.Warning);
            ActiveAlertCount = _mock.ActiveAlerts.Count;

            var tempSensors = sensors.Where(s => s.Category == AlertCategory.Temperature).ToList();
            AverageTemperature = tempSensors.Count > 0 ? Math.Round(tempSensors.Average(s => s.CurrentValue), 1) : 0;
            MaxTemperature     = tempSensors.Count > 0 ? Math.Round(tempSensors.Max(s => s.CurrentValue), 1) : 0;

            var humSensors = sensors.Where(s => s.Category == AlertCategory.Humidity).ToList();
            AverageHumidity     = humSensors.Count > 0 ? Math.Round(humSensors.Average(s => s.CurrentValue), 1) : 0;
            AverageHumidityText = $"{AverageHumidity:F0}%";

            var accSensors = sensors.Where(s => s.Category == AlertCategory.Accelerometer).ToList();
            AverageVibration     = accSensors.Count > 0 ? Math.Round(accSensors.Average(s => s.CurrentValue), 2) : 0;
            AverageVibrationText = $"{AverageVibration:F2} m/s²";

            var radSensors = sensors.Where(s => s.Category == AlertCategory.Radar).ToList();
            AverageGasLevel     = radSensors.Count > 0 ? Math.Round(radSensors.Average(s => s.CurrentValue), 1) : 0;
            AverageGasLevelText = $"{AverageGasLevel:F0}%";

            Nodes.Clear();
            foreach (var node in allNodes)
            {
                var nodeSensors = sensors.Where(s => s.NodeId == node.NodeId).ToList();
                var tempSensor  = nodeSensors.FirstOrDefault(s => s.Category == AlertCategory.Temperature);
                Nodes.Add(new NodeStatus
                {
                    NodeId      = node.NodeId,
                    Location    = node.NodeName,
                    IsOnline    = nodeSensors.Any(s => s.IsOnline),
                    Temperature = tempSensor != null ? Math.Round(tempSensor.CurrentValue, 1) : 0,
                    LastUpdate  = DateTime.Now
                });
            }

            Cameras.Clear();
            foreach (var cam in cameras)
            {
                Cameras.Add(new CameraStatus
                {
                    CameraId    = cam.CameraId,
                    Location    = cam.Location,
                    IsRecording = cam.IsOnline,
                    Fps         = 25,
                    Resolution  = "1920x1080"
                });
            }

            TemperatureReadings.Clear();
            double baseTemp = tempSensors.Count > 0 ? tempSensors.Average(s => s.NominalValue) : 24.0;
            for (int i = 0; i < 24; i++)
                TemperatureReadings.Add(new TemperatureReading { Hour = i, Temperature = baseTemp });

            RealtimeAlerts.Clear();
            foreach (var alert in _mock.AlertHistory.Take(10))
            {
                RealtimeAlerts.Add(new RealtimeAlert
                {
                    Source    = alert.NodeId,
                    Message   = alert.Title,
                    Timestamp = alert.CreatedAt.DateTime,
                    Severity  = SeverityToString(alert.Severity)
                });
            }
        }

        private void RefreshAlertCounts()
        {
            var history = _mock.AlertHistory;
            TodayAlerts    = history.Count;
            CriticalAlerts = history.Count(a => a.Severity == AlertSeverity.Critical);
            WarningAlerts  = history.Count(a => a.Severity == AlertSeverity.High || a.Severity == AlertSeverity.Medium);
            IntruAlerts    = history.Count(a => a.Severity == AlertSeverity.Low);
            MotionAlerts   = history.Count(a => a.Severity == AlertSeverity.Medium);
            FireAlerts     = history.Count(a => a.Severity == AlertSeverity.High);
            SmokeAlerts    = history.Count(a => a.Severity == AlertSeverity.Critical);
            WaterAlerts    = 0;
        }

        // ── Live event handlers ───────────────────────────────────────

        private void OnSensorTick(object? sender, SensorTickEventArgs e)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                switch (e.Sensor.Category)
                {
                    case AlertCategory.Temperature:
                        SlideMiniChart(_tempValues, e.NewValue, ref _lastTempChart);
                        var tempSensors = _mock.Sensors.Where(s => s.Category == AlertCategory.Temperature).ToList();
                        if (tempSensors.Count > 0)
                        {
                            AverageTemperature = Math.Round(tempSensors.Average(s => s.CurrentValue), 1);
                            MaxTemperature     = Math.Round(tempSensors.Max(s => s.CurrentValue), 1);
                        }
                        var node = Nodes.FirstOrDefault(n => n.NodeId == e.Sensor.NodeId);
                        if (node != null)
                        {
                            node.Temperature = Math.Round(e.NewValue, 1);
                            node.LastUpdate  = DateTime.Now;
                        }
                        int hour = DateTime.Now.Hour;
                        if (hour < TemperatureReadings.Count)
                            TemperatureReadings[hour].Temperature = Math.Round(e.NewValue, 1);
                        break;

                    case AlertCategory.Humidity:
                        SlideMiniChart(_humValues, e.NewValue, ref _lastHumChart);
                        var humSensors = _mock.Sensors.Where(s => s.Category == AlertCategory.Humidity).ToList();
                        if (humSensors.Count > 0)
                        {
                            AverageHumidity     = Math.Round(humSensors.Average(s => s.CurrentValue), 1);
                            AverageHumidityText = $"{AverageHumidity:F0}%";
                        }
                        break;

                    case AlertCategory.Accelerometer:
                        SlideMiniChart(_vibValues, e.NewValue, ref _lastVibChart);
                        var accSensors = _mock.Sensors.Where(s => s.Category == AlertCategory.Accelerometer).ToList();
                        if (accSensors.Count > 0)
                        {
                            AverageVibration     = Math.Round(accSensors.Average(s => s.CurrentValue), 2);
                            AverageVibrationText = $"{AverageVibration:F2} m/s²";
                        }
                        break;

                    case AlertCategory.Light:
                        SlideMiniChart(_lightValues, e.NewValue, ref _lastLightChart);
                        break;

                    case AlertCategory.Radar:
                    case AlertCategory.Infrared:
                        SlideMiniChart(_waterValues, e.NewValue, ref _lastWaterChart);
                        var radSensors = _mock.Sensors.Where(s => s.Category == AlertCategory.Radar).ToList();
                        if (radSensors.Count > 0)
                        {
                            AverageGasLevel     = Math.Round(radSensors.Average(s => s.CurrentValue), 1);
                            AverageGasLevelText = $"{AverageGasLevel:F0}%";
                        }
                        break;

                    default:
                        SlideMiniChart(_lightValues, e.NewValue, ref _lastLightChart);
                        break;
                }
            });
        }

        private void OnAlertGenerated(object? sender, AlertGeneratedEventArgs e)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                RefreshAlertCounts();
                AnomalyCount     = _mock.Sensors.Count(s => s.CurrentLevel >= SensorAlertLevel.Warning);
                ActiveAlertCount = _mock.ActiveAlerts.Count;

                // FIX 2: mutate ObservableValues in-place — no new array, no full re-render
                SyncPieValues();

                RealtimeAlerts.Insert(0, new RealtimeAlert
                {
                    Source    = e.Alert.NodeId,
                    Message   = e.Alert.Title,
                    Timestamp = e.Alert.CreatedAt.DateTime,
                    Severity  = SeverityToString(e.Alert.Severity)
                });
                if (RealtimeAlerts.Count > 10)
                    RealtimeAlerts.RemoveAt(RealtimeAlerts.Count - 1);

                SystemStatus = CriticalAlerts > 0 ? "Có cảnh báo khẩn cấp" : "Hoạt động bình thường";
            });
        }

        // ── Clock + system metrics ────────────────────────────────────

        private async void StartClockUpdates()
        {
            while (true)
            {
                await System.Threading.Tasks.Task.Delay(2000);
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    CurrentTime    = DateTime.Now.ToString("HH:mm:ss");
                    CurrentDate    = DateTime.Now.ToString("dd/MM/yyyy");
                    CpuUsage       = Math.Round(40 + _rng.NextDouble() * 20, 1);
                    MemoryUsage    = Math.Round(65 + _rng.NextDouble() * 10, 1);
                    NetworkLatency = $"{10 + _rng.Next(10)}ms";
                });
            }
        }

        // ── Chart builders ────────────────────────────────────────────

        private void InitializeMiniCharts()
        {
            HiddenXAxis = new LiveChartsCore.Kernel.Sketches.ICartesianAxis[]
            {
                new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 24 }
            };
            HiddenYAxis = new LiveChartsCore.Kernel.Sketches.ICartesianAxis[]
            {
                new Axis { IsVisible = false }
            };

            // Series arrays are set ONCE here — LiveCharts tracks the inner collections
            TemperatureSeries = MakeMiniChart(_tempValues,  new SKColor(239, 68, 68));
            HumiditySeries    = MakeMiniChart(_humValues,   new SKColor(59, 130, 246));
            VibrationSeries   = MakeMiniChart(_vibValues,   new SKColor(245, 158, 11));
            WaterLevelSeries  = MakeMiniChart(_waterValues, new SKColor(239, 68, 68));
            LightSeries       = MakeMiniChart(_lightValues, new SKColor(139, 92, 246));
        }

        private void InitializePieChart()
        {
            // FIX 2: Create persistent pie series ONCE — each wraps its ObservableValue.
            // On each alert, only .Value is mutated → LiveCharts animates the diff.
            AlertDistributionSeries = new ISeries[]
            {
                MakePieSeries("Thấp",              _pieIntru,       new SKColor(34, 197, 94)),
                MakePieSeries("Trung bình",         _pieMotion,      new SKColor(234, 179, 8)),
                MakePieSeries("Cao",                _pieFire,        new SKColor(249, 115, 22)),
                MakePieSeries("Nghiêm trọng",       _pieSmoke,       new SKColor(239, 68, 68)),
                MakePieSeries("Không có cảnh báo",  _piePlaceholder, new SKColor(75, 85, 99)),
            };
        }

        private void SyncPieValues()
        {
            bool hasAny = IntruAlerts > 0 || MotionAlerts > 0 || FireAlerts > 0 || SmokeAlerts > 0;
            _pieIntru.Value       = IntruAlerts;
            _pieMotion.Value      = MotionAlerts;
            _pieFire.Value        = FireAlerts;
            _pieSmoke.Value       = SmokeAlerts;
            // Placeholder visible only when all slices are zero
            _piePlaceholder.Value = hasAny ? 0 : 1;
        }

        private static PieSeries<ObservableValue> MakePieSeries(string name, ObservableValue value, SKColor color) =>
            new()
            {
                Name                 = name,
                Values               = new[] { value },
                Fill                 = new SolidColorPaint(color),
                DataLabelsPaint      = new SolidColorPaint(new SKColor(255, 255, 255)),
                DataLabelsSize       = 16,
                DataLabelsPosition   = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter  = point => $"{point.Coordinate.PrimaryValue}",
                HoverPushout         = 15,
                MaxRadialColumnWidth = double.MaxValue,
            };

        private static ObservableCollection<ObservableValue> MakeValueCollection(int length, double initial)
        {
            var col = new ObservableCollection<ObservableValue>();
            for (int i = 0; i < length; i++)
                col.Add(new ObservableValue(initial));
            return col;
        }

        private static ISeries[] MakeMiniChart(ObservableCollection<ObservableValue> values, SKColor color) =>
            new ISeries[]
            {
                new LineSeries<ObservableValue>
                {
                    Values         = values,
                    Fill           = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 30)),
                    Stroke         = new SolidColorPaint(color) { StrokeThickness = 2 },
                    GeometrySize   = 0,
                    LineSmoothness = 0.65,
                }
            };

        // FIX 1 + 3: slide the window only when the throttle interval has elapsed.
        // RemoveAt(0) + Add fires two targeted CollectionChanged events →
        // LiveCharts re-renders only the affected segment, not the whole line.
        private static void SlideMiniChart(
            ObservableCollection<ObservableValue> values,
            double newValue,
            ref DateTime lastUpdate)
        {
            var now = DateTime.UtcNow;
            if ((now - lastUpdate) < _chartInterval) return;
            lastUpdate = now;

            values.RemoveAt(0);
            values.Add(new ObservableValue(newValue));
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static string SeverityToString(AlertSeverity severity) => severity switch
        {
            AlertSeverity.Critical => "critical",
            AlertSeverity.High     => "high",
            AlertSeverity.Medium   => "warning",
            _                      => "low"
        };
    }

    public class NodeStatus
    {
        public string NodeId    { get; set; } = string.Empty;
        public string Location  { get; set; } = string.Empty;
        public bool   IsOnline  { get; set; }
        public double Temperature { get; set; }
        public DateTime LastUpdate { get; set; }

        public SolidColorBrush StatusBrush => IsOnline
            ? new SolidColorBrush(Color.FromArgb(255, 63, 207, 142))
            : new SolidColorBrush(Color.FromArgb(255, 123, 126, 133));

        public SolidColorBrush TemperatureBrush => Temperature > 35
            ? new SolidColorBrush(Color.FromArgb(255, 240, 98, 93))
            : new SolidColorBrush(Color.FromArgb(255, 154, 166, 178));

        public string StatusText      => IsOnline ? "Online" : "Offline";
        public string TemperatureText => $"{Temperature:F1}°C";
        public string LastUpdateText  => $"{(DateTime.Now - LastUpdate).TotalMinutes:F0}m trước";
    }

    public class RealtimeAlert
    {
        public string   Source    { get; set; } = string.Empty;
        public string   Message   { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string   Severity  { get; set; } = "low";

        public string TimeText => Timestamp.ToString("HH:mm:ss");

        public SolidColorBrush SeverityBrush => Severity switch
        {
            "critical" => new SolidColorBrush(Color.FromArgb(255, 240, 98, 93)),
            "high"     => new SolidColorBrush(Color.FromArgb(255, 255, 209, 102)),
            "warning"  => new SolidColorBrush(Color.FromArgb(255, 255, 209, 102)),
            _          => new SolidColorBrush(Color.FromArgb(255, 154, 166, 178))
        };

        public string SeverityLabel => Severity switch
        {
            "critical" => "KHẨN CẤP",
            "high"     => "CAO",
            "warning"  => "TRUNG BÌNH",
            _          => "THẤP"
        };
    }

    public class TemperatureReading
    {
        public int    Hour        { get; set; }
        public double Temperature { get; set; }
        public string HourLabel   => $"{Hour:D2}:00";
    }

    public class CameraStatus
    {
        public string CameraId   { get; set; } = string.Empty;
        public string Location   { get; set; } = string.Empty;
        public bool   IsRecording { get; set; }
        public int    Fps         { get; set; }
        public string Resolution  { get; set; } = string.Empty;

        public SolidColorBrush StatusBrush => IsRecording
            ? new SolidColorBrush(Color.FromArgb(255, 63, 207, 142))
            : new SolidColorBrush(Color.FromArgb(255, 123, 126, 133));

        public string StatusText => IsRecording ? $"Recording • {Fps} FPS" : "Offline";
    }
}
