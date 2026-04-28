using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Station.Services
{
    /// <summary>
    /// Client for connecting to Simulation API WebSocket to receive real-time sensor updates.
    /// </summary>
    public class SimulationWebSocketClient : IAsyncDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly string _wsUrl;
        private bool _isConnected;

        public event EventHandler<SimulationSensorUpdate>? SensorUpdated;
        public event EventHandler<SimulationAlertUpdate>? AlertReceived;
        public event EventHandler<bool>? ConnectionChanged;

        public bool IsConnected => _isConnected;

        public SimulationWebSocketClient(string? wsUrl = null)
        {
            _wsUrl = wsUrl ?? "ws://localhost:5050/ws";
        }

        public async Task ConnectAsync(CancellationToken externalToken = default)
        {
            if (_ws != null)
            {
                await DisconnectAsync();
            }

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalToken);
            try
            {
                await _ws.ConnectAsync(new Uri(_wsUrl), linked.Token);
                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
                System.Diagnostics.Debug.WriteLine($"[SimWS] Connected to {_wsUrl}");

                // Start receiving messages
                _ = ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                System.Diagnostics.Debug.WriteLine($"[SimWS] Failed to connect: {ex.Message}");
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_ws != null)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                    }
                }
                catch { }
                finally
                {
                    _ws.Dispose();
                    _ws = null;
                }
            }
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (_ws?.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(json);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimWS] Receive error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private void ProcessMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var type = root.GetProperty("type").GetString();

                switch (type)
                {
                    case "sensorTick":
                        var sensorUpdate = JsonSerializer.Deserialize<SimulationSensorUpdate>(json, _jsonOpts);
                        if (sensorUpdate != null)
                        {
                            SensorUpdated?.Invoke(this, sensorUpdate);
                        }
                        break;
                    case "alertGenerated":
                        var alertUpdate = JsonSerializer.Deserialize<SimulationAlertUpdate>(json, _jsonOpts);
                        if (alertUpdate != null)
                        {
                            AlertReceived?.Invoke(this, alertUpdate);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimWS] Parse error: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }
    }

    public class SimulationSensorUpdate
    {
        public string Type { get; set; } = string.Empty;
        public string SensorId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public bool IsAnomaly { get; set; }
        public string Ts { get; set; } = string.Empty;
    }

    public class SimulationAlertUpdate
    {
        public string Type { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public string SensorId { get; set; } = string.Empty;
        public double? SensorValue { get; set; }
    }
}
