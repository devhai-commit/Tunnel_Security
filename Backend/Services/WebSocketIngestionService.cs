using System.Net.WebSockets;
using System.Text.Json;
using Backend.Config;

namespace Backend.Services;

/// <summary>
/// Kết nối WebSocket tới thiết bị và decode binary frame protocol.
/// Frame format (10 bytes): AA | NODE_ID | SENSOR_ID | SEQ | VALUE[4] | CRC8 | BB
/// Thiết bị cũng có thể gửi JSON: {"sensorId": "...", "value": 23.5}
/// </summary>
public class WebSocketIngestionService : BackgroundService
{
    private readonly WebSocketConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebSocketIngestionService> _logger;

    private const byte FrameStart = 0xAA;
    private const byte FrameStop = 0xBB;
    private const int FrameSize = 10;

    public WebSocketIngestionService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<WebSocketIngestionService> logger)
    {
        _config = configuration.GetSection("Devices:WebSocket").Get<WebSocketConfig>() ?? new WebSocketConfig();
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled || _config.Endpoints.Count == 0)
        {
            _logger.LogInformation("[WebSocket] Disabled or no endpoints configured");
            return;
        }

        var tasks = _config.Endpoints.Select(ep => ConnectAndReceiveAsync(ep, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task ConnectAndReceiveAsync(WebSocketEndpointConfig ep, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(ep.Url), ct);
                _logger.LogInformation("[WebSocket] Connected to {DeviceId} at {Url}", ep.DeviceId, ep.Url);

                var buffer = new byte[1024];
                var frameBuffer = new List<byte>();

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    frameBuffer.AddRange(buffer[..result.Count]);
                    await ProcessBufferAsync(ep.DeviceId, frameBuffer, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("[WebSocket] {DeviceId} disconnected: {Message} — reconnecting in 5s",
                    ep.DeviceId, ex.Message);
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task ProcessBufferAsync(string deviceId, List<byte> buffer, CancellationToken ct)
    {
        // Thử decode JSON first
        if (buffer.Count > 0 && buffer[0] == '{')
        {
            await TryDecodeJsonAsync(deviceId, buffer, ct);
            return;
        }

        // Decode binary frames
        while (buffer.Count >= FrameSize)
        {
            int startIdx = buffer.IndexOf(FrameStart);
            if (startIdx < 0) { buffer.Clear(); return; }
            if (startIdx > 0) buffer.RemoveRange(0, startIdx);
            if (buffer.Count < FrameSize) return;

            var frame = buffer.GetRange(0, FrameSize).ToArray();
            buffer.RemoveRange(0, FrameSize);

            if (frame[9] != FrameStop) continue;

            byte expectedCrc = ComputeCrc8(frame, 0, 8);
            if (expectedCrc != frame[8])
            {
                _logger.LogDebug("[WebSocket] {DeviceId} CRC mismatch — discarding frame", deviceId);
                continue;
            }

            // Map nodeId (0x01-0x0A) + sensorByte to sensorId string
            var sensorId = MapFrameToSensorId(deviceId, frame[1], frame[2]);
            if (sensorId == null) continue;

            float value = BitConverter.ToSingle(frame, 4);

            using var scope = _scopeFactory.CreateScope();
            var broadcaster = scope.ServiceProvider.GetRequiredService<SensorBroadcaster>();
            await broadcaster.ProcessReadingAsync(sensorId, value, ct);
        }
    }

    private async Task TryDecodeJsonAsync(string deviceId, List<byte> buffer, CancellationToken ct)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            buffer.Clear();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sensorId", out var sidEl)) return;
            if (!doc.RootElement.TryGetProperty("value", out var valEl)) return;

            using var scope = _scopeFactory.CreateScope();
            var broadcaster = scope.ServiceProvider.GetRequiredService<SensorBroadcaster>();
            await broadcaster.ProcessReadingAsync(sidEl.GetString()!, valEl.GetDouble(), ct);
        }
        catch
        {
            buffer.Clear();
        }
    }

    // nodeId byte 0x01-0x0A maps to our DB sensor IDs by convention
    private static string? MapFrameToSensorId(string deviceId, byte nodeId, byte sensorByte)
    {
        // Convention: deviceId contains nodeId prefix, e.g. "NODE-L1-01"
        // sensor byte 0x01=Temp, 0x02=Humidity, etc.
        string suffix = sensorByte switch
        {
            0x01 => "TEMP",
            0x02 => "HUM",
            0x03 => null!,         // Light — not in our model
            0x04 => "RADAR",
            0x05 => "VIB",
            0x07 => "WATER",
            _ => null!
        };
        if (suffix == null) return null;
        return $"{deviceId}-{suffix}";
    }

    private static byte ComputeCrc8(byte[] data, int offset, int length)
    {
        byte crc = 0;
        for (int i = offset; i < offset + length; i++) crc ^= data[i];
        return crc;
    }
}
