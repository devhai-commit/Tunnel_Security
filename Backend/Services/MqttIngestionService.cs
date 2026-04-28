using System.Text;
using System.Text.Json;
using Backend.Config;
using MQTTnet;
using MQTTnet.Client;

namespace Backend.Services;

/// <summary>
/// Subscribe MQTT broker và ingest sensor readings.
/// Topic format: tunnel/{stationId}/node/{nodeId}/sensor/{sensorId}
/// Payload: {"value": 23.5}
///
/// Auto-reconnect với exponential backoff (1s → 60s) khi mất kết nối.
/// </summary>
public sealed class MqttIngestionService : BackgroundService
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    private readonly MqttConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MqttIngestionService> _logger;

    // Completed by DisconnectedAsync event to unblock the WhenAny wait in the connect loop.
    private TaskCompletionSource<bool> _disconnectTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MqttIngestionService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<MqttIngestionService> logger)
    {
        _config       = configuration.GetSection("Devices:Mqtt").Get<MqttConfig>() ?? new MqttConfig();
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("[MQTT] Disabled — set Devices:Mqtt:Enabled=true to activate");
            return;
        }

        using var client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        client.DisconnectedAsync               += OnDisconnectedAsync;

        var options = new MqttClientOptionsBuilder()
            .WithClientId(_config.ClientId)
            .WithTcpServer(_config.BrokerHost, _config.BrokerPort)
            .WithCleanSession()
            .Build();

        var backoff = MinBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Reset TCS BEFORE connecting so any early disconnect is captured.
            _disconnectTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await client.ConnectAsync(options, stoppingToken);
                var topic = $"{_config.TopicPrefix}/+/node/+/sensor/+";
                await client.SubscribeAsync(topic, cancellationToken: stoppingToken);

                _logger.LogInformation(
                    "[MQTT] Connected to {Host}:{Port}, subscribed to {Topic}",
                    _config.BrokerHost, _config.BrokerPort, topic);
                backoff = MinBackoff; // reset on successful connect

                // Block until broker disconnects or host shuts down.
                await Task.WhenAny(_disconnectTcs.Task, Task.Delay(Timeout.Infinite, stoppingToken));
                stoppingToken.ThrowIfCancellationRequested();

                _logger.LogWarning("[MQTT] Connection lost — reconnecting in {D}s", backoff.TotalSeconds);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("[MQTT] Connect failed: {Msg} — retry in {D}s",
                    ex.Message, backoff.TotalSeconds);
            }

            try { await Task.Delay(backoff, stoppingToken); }
            catch (OperationCanceledException) { break; }

            backoff = TimeSpan.FromSeconds(
                Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }

        if (client.IsConnected)
        {
            try { await client.DisconnectAsync(); }
            catch { /* ignore disconnect errors on shutdown */ }
        }
        _logger.LogInformation("[MQTT] Stopped");
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs _)
    {
        _disconnectTcs.TrySetResult(true);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            // Parse topic: tunnel/{stationId}/node/{nodeId}/sensor/{sensorId}
            var parts = e.ApplicationMessage.Topic.Split('/');
            if (parts.Length < 6) return;

            var sensorId = parts[5];
            if (string.IsNullOrEmpty(sensorId)) return;

            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            using var doc = JsonDocument.Parse(payload);

            if (!doc.RootElement.TryGetProperty("value", out var valueElement)) return;
            double value = valueElement.GetDouble();

            using var scope = _scopeFactory.CreateScope();
            var broadcaster = scope.ServiceProvider.GetRequiredService<SensorBroadcaster>();
            await broadcaster.ProcessReadingAsync(sensorId, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MQTT] Message processing error: {Message}", ex.Message);
        }
    }
}
