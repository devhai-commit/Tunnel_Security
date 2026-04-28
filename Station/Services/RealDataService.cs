using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Station.Models;

namespace Station.Services;

/// <summary>
/// Kết nối tới Backend API thực tế.
/// Fetch initial topology từ REST, nhận real-time updates qua SignalR.
/// Fallback về MockDataService khi Backend không available.
/// </summary>
public class RealDataService : IDataService, IAsyncDisposable
{
    private readonly string _apiBaseUrl;
    private readonly string _stationId;
    private readonly ApiSensorClient _signalRClient;
    private readonly HttpClient _httpClient;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly Dictionary<string, SimulatedSensor> _sensorMap = new();
    private readonly Dictionary<string, SimulatedCamera> _cameraMap = new();
    private readonly object _dynamicLock = new();
    private readonly HashSet<string> _pendingDynamic = new();

    private List<SimulatedSensor> _sensors = new();
    private List<SimulatedCamera> _cameras = new();
    private List<TunnelLine> _lines = new();

    public IReadOnlyList<SimulatedSensor> Sensors => _sensors;
    public IReadOnlyList<SimulatedCamera> Cameras => _cameras;
    public IReadOnlyList<TunnelLine> Lines => _lines;
    public ObservableCollection<Alert> ActiveAlerts { get; } = new();
    public ObservableCollection<Alert> AlertHistory { get; } = new();

    public event EventHandler<SensorTickEventArgs>? SensorTick;
    public event EventHandler<AlertGeneratedEventArgs>? AlertGenerated;
    public event EventHandler? TopologyLoaded;

    public RealDataService()
    {
        _apiBaseUrl = Environment.GetEnvironmentVariable("BACKEND_BASE_URL") ?? "http://localhost:5280";
        _stationId = Environment.GetEnvironmentVariable("STATION_ID") ?? "ST01";
        _signalRClient = new ApiSensorClient(_apiBaseUrl);
        _httpClient = new HttpClient { BaseAddress = new Uri(_apiBaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public void Start()
    {
        _ = Task.Run(InitializeAsync);
    }

    public void Stop()
    {
        _ = _signalRClient.DisposeAsync().AsTask();
    }

    private async Task InitializeAsync()
    {
        // Topology load is best-effort — failure should not prevent SignalR from connecting.
        try
        {
            await LoadTopologyAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] Topology load failed: {ex.Message} — continuing with empty topology");
        }

        // Notify UI of current topology (even if empty, DataPage will show "waiting" state).
        _dispatcherQueue?.TryEnqueue(() => TopologyLoaded?.Invoke(this, EventArgs.Empty));

        // Always connect SignalR so we receive real-time sensor updates from the backend.
        try
        {
            await ConnectSignalRAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] SignalR connect failed: {ex.Message}");
        }
    }

    private async Task LoadTopologyAsync()
    {
        var json = await _httpClient.GetStringAsync($"/api/stations/{_stationId}");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var sensors = new List<SimulatedSensor>();
        var cameras = new List<SimulatedCamera>();
        var lines = new List<TunnelLine>();

        if (root.TryGetProperty("lines", out var linesEl))
        {
            foreach (var lineEl in linesEl.EnumerateArray())
            {
                var lineId = lineEl.GetProperty("id").GetString() ?? "";
                var lineName = lineEl.GetProperty("name").GetString() ?? "";

                var tunnelNodes = new List<TunnelNode>();

                if (lineEl.TryGetProperty("nodes", out var nodesEl))
                {
                    foreach (var nodeEl in nodesEl.EnumerateArray())
                    {
                        var nodeId = nodeEl.GetProperty("id").GetString() ?? "";
                        var nodeName = nodeEl.GetProperty("name").GetString() ?? "";
                        var cameraId = nodeEl.TryGetProperty("cameraId", out var camEl) ? camEl.GetString() : null;

                        // Map sensors
                        if (nodeEl.TryGetProperty("sensors", out var sensorsEl))
                        {
                            foreach (var sensorEl in sensorsEl.EnumerateArray())
                            {
                                var sensor = MapSensor(sensorEl, nodeId, nodeName, lineId, lineName);
                                sensors.Add(sensor);
                                _sensorMap[sensor.SensorId] = sensor;
                            }
                        }

                        // Map camera
                        if (cameraId != null)
                        {
                            var cam = new SimulatedCamera
                            {
                                CameraId = cameraId,
                                CameraName = $"Camera {nodeName}",
                                Location = nodeName,
                                NodeId = nodeId,
                                NodeName = nodeName,
                                LineId = lineId,
                                LineName = lineName,
                                IsOnline = false,
                                // HLS proxy endpoint — backend chuyển RTSP sang HLS
                                StreamUrl = $"{_apiBaseUrl}/api/cameras/{cameraId}/stream"
                            };
                            cameras.Add(cam);
                            _cameraMap[cameraId] = cam;
                        }

                        tunnelNodes.Add(new TunnelNode { NodeId = nodeId, NodeName = nodeName, LineId = lineId });
                    }
                }

                lines.Add(new TunnelLine { LineId = lineId, LineName = lineName, Nodes = tunnelNodes });
            }
        }

        _sensors = sensors;
        _cameras = cameras;
        _lines = lines;

        System.Diagnostics.Debug.WriteLine(
            $"[RealDataService] Loaded {sensors.Count} sensors, {cameras.Count} cameras from API");
    }

    private async Task ConnectSignalRAsync()
    {
        _signalRClient.SensorUpdated += OnSensorUpdated;
        _signalRClient.ConnectionChanged += OnConnectionChanged;

        try
        {
            await _signalRClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] SignalR connect failed: {ex.Message}");
        }
    }

    private void OnSensorUpdated(object? sender, ApiSensorUpdate update)
    {
        if (!_sensorMap.TryGetValue(update.Id, out var sensor))
        {
            bool isNew;
            lock (_dynamicLock) { isNew = _pendingDynamic.Add(update.Id); }
            if (isNew)
            {
                var dynSensor = CreateDynamicSensor(update);
                void Register()
                {
                    if (_sensorMap.ContainsKey(dynSensor.SensorId)) return;
                    _sensors.Add(dynSensor);
                    _sensorMap[dynSensor.SensorId] = dynSensor;
                    TopologyLoaded?.Invoke(this, EventArgs.Empty);
                }
                if (_dispatcherQueue != null) _dispatcherQueue.TryEnqueue(Register);
                else Register();
            }
            return;
        }

        sensor.CurrentValue = update.CurrentValue;
        sensor.IsOnline = true;

        var isAnomaly = sensor.CurrentLevel >= SensorAlertLevel.Warning;
        var args = new SensorTickEventArgs
        {
            Sensor = sensor,
            NewValue = update.CurrentValue,
            Timestamp = DateTimeOffset.UtcNow,
            IsAnomaly = isAnomaly
        };

        if (_dispatcherQueue != null)
            _dispatcherQueue.TryEnqueue(() => SensorTick?.Invoke(this, args));
        else
            SensorTick?.Invoke(this, args);

        // Tạo alert khi vượt ngưỡng
        if (isAnomaly)
            TryGenerateAlert(sensor);
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        System.Diagnostics.Debug.WriteLine($"[RealDataService] SignalR connected: {connected}");
        if (!connected)
        {
            foreach (var s in _sensors) s.IsOnline = false;
        }
    }

    private void TryGenerateAlert(SimulatedSensor sensor)
    {
        var severity = sensor.CurrentAlertSeverity;
        var alert = new Alert
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.Now,
            Severity = severity,
            Category = sensor.Category,
            NodeId = sensor.NodeId,
            NodeName = sensor.NodeName,
            SensorId = sensor.SensorId,
            SensorName = sensor.SensorName,
            SensorValue = sensor.CurrentValue,
            SensorUnit = sensor.Unit,
            Threshold = sensor.WarnThreshold,
            Title = $"Cảnh báo {sensor.SensorName}",
            Description = $"{sensor.SensorName} = {sensor.CurrentValue:F1} {sensor.Unit} (ngưỡng: {sensor.WarnThreshold})",
            State = AlertState.Unprocessed
        };

        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ActiveAlerts.Insert(0, alert);
                AlertHistory.Insert(0, alert);
                if (ActiveAlerts.Count > 100) ActiveAlerts.RemoveAt(ActiveAlerts.Count - 1);
                AlertGenerated?.Invoke(this, new AlertGeneratedEventArgs { Alert = alert });
            });
        }
    }

    private static SimulatedSensor CreateDynamicSensor(ApiSensorUpdate update)
    {
        var category = update.Type.ToLowerInvariant() switch
        {
            "temperature" => AlertCategory.Temperature,
            "humidity"    => AlertCategory.Humidity,
            "radar"       => AlertCategory.Radar,
            "vibration"   => AlertCategory.Accelerometer,
            _             => AlertCategory.Other
        };

        var nodeId   = string.IsNullOrEmpty(update.NodeId) ? ExtractNodeFromSensorId(update.Id) : update.NodeId;
        var nodeName = string.IsNullOrEmpty(update.NodeName) ? nodeId : update.NodeName;
        var (lineId, lineName) = InferLine(nodeId);
        var (warn, crit, absMax) = DefaultThresholds(category);

        return new SimulatedSensor
        {
            SensorId          = update.Id,
            SensorName        = string.IsNullOrEmpty(update.Name) ? update.Id : update.Name,
            Unit              = update.Unit ?? "",
            NodeId            = nodeId,
            NodeName          = nodeName,
            LineId            = lineId,
            LineName          = lineName,
            Location          = nodeName,
            Category          = category,
            CurrentValue      = update.CurrentValue,
            NominalValue      = warn * 0.5,
            WarnThreshold     = warn,
            CriticalThreshold = crit,
            DriftSpeed        = (crit - warn) * 0.1,
            AbsoluteMin       = 0,
            AbsoluteMax       = absMax,
            MinNormal         = 0,
            MaxNormal         = warn,
            IsOnline          = true
        };
    }

    private static string ExtractNodeFromSensorId(string sensorId)
    {
        var idx = sensorId.LastIndexOf('-');
        return idx > 0 ? sensorId[..idx] : sensorId;
    }

    private static (string lineId, string lineName) InferLine(string nodeId)
    {
        var upper = nodeId.ToUpperInvariant();
        if (upper.Contains("L1")) return ("LINE-01", "Đường hầm L1");
        if (upper.Contains("L2")) return ("LINE-02", "Đường hầm L2");
        if (upper.Contains("L3")) return ("LINE-03", "Đường hầm L3");
        return ("LINE-SIM", "Simulator");
    }

    private static (double warn, double crit, double absMax) DefaultThresholds(AlertCategory category) =>
        category switch
        {
            AlertCategory.Temperature  => (35.0, 50.0,  80.0),
            AlertCategory.Humidity     => (80.0, 95.0, 100.0),
            AlertCategory.Radar        => (5.0,  1.0,   10.0),
            AlertCategory.Accelerometer => (3.0, 8.0,   15.0),
            _                          => (50.0, 90.0, 100.0)
        };

    private static SimulatedSensor MapSensor(
        JsonElement el, string nodeId, string nodeName, string lineId, string lineName)
    {
        var id = el.GetProperty("id").GetString() ?? "";
        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : id;
        var unit = el.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : "";
        var warn = el.TryGetProperty("warningThreshold", out var w) ? w.GetDouble() : 30.0;
        var crit = el.TryGetProperty("criticalThreshold", out var c) ? c.GetDouble() : 50.0;
        var curr = el.TryGetProperty("currentValue", out var cv) && !cv.ValueKind.Equals(JsonValueKind.Null)
            ? cv.GetDouble() : warn * 0.5;

        var typeStr = el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var category = typeStr.ToLower() switch
        {
            "temperature"  => AlertCategory.Temperature,
            "humidity"     => AlertCategory.Humidity,
            "radar"        => AlertCategory.Radar,
            "smokeFire" or "smokefire" or "gas" => AlertCategory.Other,
            "vibration"    => AlertCategory.Accelerometer,
            "motion"       => AlertCategory.Infrared,
            "pressure" or "waterlevel" => AlertCategory.Other,
            _              => AlertCategory.Other
        };

        return new SimulatedSensor
        {
            SensorId = id,
            SensorName = name,
            Unit = unit,
            NodeId = nodeId,
            NodeName = nodeName,
            LineId = lineId,
            LineName = lineName,
            Location = nodeName,
            Category = category,
            CurrentValue = curr,
            NominalValue = warn * 0.5,
            WarnThreshold = warn,
            CriticalThreshold = crit,
            DriftSpeed = (crit - warn) * 0.1,
            AbsoluteMin = 0,
            AbsoluteMax = crit * 1.5,
            MinNormal = 0,
            MaxNormal = warn,
            IsOnline = true
        };
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        _httpClient.Dispose();
        await _signalRClient.DisposeAsync();
    }
}
