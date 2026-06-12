using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net;
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
    private readonly List<TunnelNode> _nodes = new();
    private readonly Dictionary<string, SimulatedSensor> _sensorMap = new();
    private readonly Dictionary<string, SimulatedCamera> _cameraMap = new();
    private readonly object _dynamicLock = new();
    private readonly HashSet<string> _pendingDynamic = new();

    private List<SimulatedSensor> _sensors = new();
    private List<SimulatedCamera> _cameras = new();
    private List<TunnelNode> _nodesSnapshot = new();
    private List<TunnelLine> _lines = new();

    public IReadOnlyList<SimulatedSensor> Sensors => _sensors;
    public IReadOnlyList<SimulatedCamera> Cameras => _cameras;
    public IReadOnlyList<TunnelNode> Nodes => _nodesSnapshot;
    public IReadOnlyList<TunnelLine> Lines => _lines;
    public ObservableCollection<Alert> ActiveAlerts { get; } = new();
    public ObservableCollection<Alert> AlertHistory { get; } = new();

    public event EventHandler<SensorTickEventArgs>? SensorTick;
    public event EventHandler<AlertGeneratedEventArgs>? AlertGenerated;
    public event EventHandler? TopologyLoaded;
    public event EventHandler<JoinRequestNotification>? NewJoinRequest;

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
        // Topology load is best-effort, but we retry because the backend/DB may still be
        // starting when Station launches.
        try
        {
            await LoadTopologyWithRetryAsync();
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

    private async Task LoadTopologyWithRetryAsync()
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await LoadTopologyAsync();
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[RealDataService] Station '{_stationId}' not found. Waiting for valid seed data.");
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                    throw;

                System.Diagnostics.Debug.WriteLine(
                    $"[RealDataService] Topology load attempt {attempt}/{maxAttempts} failed: {ex.Message}");

                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
        }
    }

    private async Task LoadTopologyAsync()
    {
        var json = await _httpClient.GetStringAsync($"/api/stations/{_stationId}");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var sensors = new List<SimulatedSensor>();
        var cameras = new List<SimulatedCamera>();
        var nodes = new List<TunnelNode>();
        var lines = new List<TunnelLine>();

        if (root.TryGetProperty("lines", out var linesEl))
        {
            foreach (var lineEl in linesEl.EnumerateArray())
            {
                var lineId = lineEl.TryGetProperty("id", out var lineIdEl)
                    ? GetStringValue(lineIdEl)
                    : string.Empty;
                var lineName = lineEl.TryGetProperty("name", out var lineNameEl)
                    ? GetStringValue(lineNameEl, lineId)
                    : lineId;

                var tunnelNodes = new List<TunnelNode>();

                if (lineEl.TryGetProperty("nodes", out var nodesEl))
                {
                    foreach (var nodeEl in nodesEl.EnumerateArray())
                    {
                        var nodeId = nodeEl.TryGetProperty("id", out var nodeIdEl)
                            ? GetStringValue(nodeIdEl)
                            : string.Empty;
                        var nodeName = nodeEl.TryGetProperty("name", out var nodeNameEl)
                            ? GetStringValue(nodeNameEl, nodeId)
                            : nodeId;
                        var cameraId = nodeEl.TryGetProperty("cameraId", out var camEl)
                            ? GetStringValue(camEl)
                            : string.Empty;

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

                        nodes.Add(new TunnelNode
                        {
                            NodeId = nodeId,
                            NodeName = nodeName,
                            LineId = lineId,
                            LineName = lineName
                        });

                        // Map camera
                        if (!string.IsNullOrWhiteSpace(cameraId))
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
                                IsOnline = true,
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

        var nodesLoadedFromDb = await TryLoadNodesFromNodesEndpointAsync(nodes, lines);
        if (nodesLoadedFromDb)
        {
            var nodeIndex = nodes.ToDictionary(n => n.NodeId, StringComparer.OrdinalIgnoreCase);
            foreach (var s in sensors)
            {
                if (nodeIndex.TryGetValue(s.NodeId, out var node))
                {
                    s.NodeName = node.NodeName;
                    s.LineId = node.LineId;
                    s.LineName = node.LineName;
                    s.Location = node.NodeName;
                }
            }

            foreach (var cam in cameras)
            {
                if (nodeIndex.TryGetValue(cam.NodeId, out var node))
                {
                    cam.NodeName = node.NodeName;
                    cam.LineId = node.LineId;
                    cam.LineName = node.LineName;
                    cam.Location = node.NodeName;
                }
            }
        }

        _sensors = sensors;
        _cameras = cameras;
        _nodesSnapshot = nodes;
        _lines = lines;

        System.Diagnostics.Debug.WriteLine(
            $"[RealDataService] Loaded {nodes.Count} nodes, {sensors.Count} sensors, {cameras.Count} cameras from API");
    }

    private async Task<bool> TryLoadNodesFromNodesEndpointAsync(List<TunnelNode> nodes, List<TunnelLine> lines)
    {
        try
        {
            var json = await _httpClient.GetStringAsync($"/api/stations/{_stationId}/nodes");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!TryGetPropertyIgnoreCase(root, "features", out var featuresEl) ||
                featuresEl.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var loadedNodes = new List<TunnelNode>();
            var lineMap = new Dictionary<string, TunnelLine>(StringComparer.OrdinalIgnoreCase);

            foreach (var feature in featuresEl.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(feature, "properties", out var props))
                {
                    continue;
                }

                var nodeId = GetStringProperty(props, "id");
                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    continue;
                }

                var nodeName = GetStringProperty(props, "name");
                if (string.IsNullOrWhiteSpace(nodeName))
                {
                    nodeName = nodeId;
                }

                var lineId = GetStringProperty(props, "lineId");
                var lineName = GetStringProperty(props, "line");
                if (string.IsNullOrWhiteSpace(lineName))
                {
                    lineName = lineId;
                }

                loadedNodes.Add(new TunnelNode
                {
                    NodeId = nodeId,
                    NodeName = nodeName,
                    LineId = lineId,
                    LineName = lineName
                });

                if (!lineMap.TryGetValue(lineId, out var tunnelLine))
                {
                    tunnelLine = new TunnelLine
                    {
                        LineId = lineId,
                        LineName = lineName,
                        Nodes = new List<TunnelNode>()
                    };
                    lineMap[lineId] = tunnelLine;
                }

                if (!tunnelLine.Nodes.Any(n => n.NodeId == nodeId))
                {
                    tunnelLine.Nodes.Add(new TunnelNode
                    {
                        NodeId = nodeId,
                        NodeName = nodeName,
                        LineId = lineId,
                        LineName = lineName
                    });
                }
            }

            if (loadedNodes.Count == 0)
            {
                return false;
            }

            nodes.Clear();
            nodes.AddRange(loadedNodes);

            lines.Clear();
            lines.AddRange(lineMap.Values);

            return true;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] Nodes endpoint failed: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] Nodes endpoint timeout: {ex.Message}");
            return false;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] Nodes endpoint JSON error: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetStringProperty(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var value))
        {
            return string.Empty;
        }

        return GetStringValue(value);
    }

    private static string GetStringValue(JsonElement element, string fallback = "")
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? fallback,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => fallback,
            _ => element.ToString()
        };
    }

    private async Task ConnectSignalRAsync()
    {
        _signalRClient.SensorUpdated     += OnSensorUpdated;
        _signalRClient.ConnectionChanged += OnConnectionChanged;
        _signalRClient.NewJoinRequest    += OnNewJoinRequest;

        try
        {
            await _signalRClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RealDataService] SignalR connect failed: {ex.Message}");
        }
    }

    private void OnNewJoinRequest(object? sender, JoinRequestNotification req)
    {
        if (_dispatcherQueue != null)
            _dispatcherQueue.TryEnqueue(() => NewJoinRequest?.Invoke(this, req));
        else
            NewJoinRequest?.Invoke(this, req);
    }

    public Task<bool> ApproveJoinRequestAsync(int requestId, byte nodeByteId)
        => _signalRClient.ApproveJoinRequestAsync(requestId, nodeByteId);

    public Task<bool> RejectJoinRequestAsync(int requestId, string? reason = null)
        => _signalRClient.RejectJoinRequestAsync(requestId, reason);

    public Task<IReadOnlyList<JoinRequestNotification>> GetPendingJoinRequestsAsync()
        => _signalRClient.GetPendingJoinRequestsAsync();

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
            "temperature" or "3" => AlertCategory.Temperature,
            "humidity"    or "4" => AlertCategory.Humidity,
            "light"              => AlertCategory.Light,
            "radar"       or "0" => AlertCategory.Radar,
            "vibration"   or "1" => AlertCategory.Accelerometer,
            "motion"      or "8" => AlertCategory.Infrared,
            _                    => AlertCategory.Other
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
            AlertCategory.Temperature  => (35.0,   50.0,   80.0),
            AlertCategory.Humidity     => (80.0,   95.0,  100.0),
            AlertCategory.Light        => (800.0, 1500.0, 2000.0),
            AlertCategory.Radar        => (5.0,    1.0,   10.0),
            AlertCategory.Accelerometer => (3.0,   8.0,   15.0),
            _                          => (50.0,  90.0,  100.0)
        };

    private static SimulatedSensor MapSensor(
        JsonElement el, string nodeId, string nodeName, string lineId, string lineName)
    {
        var id = el.TryGetProperty("id", out var idEl) ? GetStringValue(idEl) : string.Empty;
        var name = el.TryGetProperty("name", out var n) ? GetStringValue(n, id) : id;
        var unit = el.TryGetProperty("unit", out var u) ? GetStringValue(u) : string.Empty;
        var warn = el.TryGetProperty("warningThreshold", out var w) ? w.GetDouble() : 30.0;
        var crit = el.TryGetProperty("criticalThreshold", out var c) ? c.GetDouble() : 50.0;
        var curr = el.TryGetProperty("currentValue", out var cv) && cv.ValueKind != JsonValueKind.Null
            ? cv.GetDouble() : warn * 0.5;

        var typeStr = el.TryGetProperty("type", out var t) ? GetStringValue(t) : string.Empty;
        // Backend serializes SensorType enum as integer (0=Radar,1=Vibration,2=SmokeFire,
        // 3=Temperature,4=Humidity,5=Gas,6=Pressure,7=WaterLevel,8=Motion).
        // Also accept enum name strings for forward-compat with JsonStringEnumConverter.
        var category = typeStr.ToLower() switch
        {
            "temperature"  or "3" => AlertCategory.Temperature,
            "humidity"     or "4" => AlertCategory.Humidity,
            "radar"        or "0" => AlertCategory.Radar,
            "vibration"    or "1" => AlertCategory.Accelerometer,
            "motion"       or "8" => AlertCategory.Infrared,
            "smokefire" or "gas"  or "2" or "5" => AlertCategory.Other,
            "pressure" or "waterlevel" or "6" or "7" => AlertCategory.Other,
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
