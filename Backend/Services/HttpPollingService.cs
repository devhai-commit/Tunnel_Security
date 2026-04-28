using System.Text.Json;
using Backend.Config;

namespace Backend.Services;

/// <summary>
/// Poll HTTP endpoints của thiết bị theo chu kỳ.
/// Endpoint trả về: {"value": 23.5} hoặc {"value": 23.5, "unit": "°C"}
/// Dùng PeriodicTimer thay Task.Delay để tránh drift tích lũy.
/// </summary>
public sealed class HttpPollingService : BackgroundService
{
    private readonly HttpPollingConfig _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HttpPollingService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public HttpPollingService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<HttpPollingService> logger,
        IHttpClientFactory httpFactory)
    {
        _config      = configuration.GetSection("Devices:Http").Get<HttpPollingConfig>() ?? new HttpPollingConfig();
        _scopeFactory = scopeFactory;
        _logger      = logger;
        _httpFactory  = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled || _config.Endpoints.Count == 0)
        {
            _logger.LogInformation("[HTTP Polling] Disabled or no endpoints configured");
            return;
        }

        _logger.LogInformation("[HTTP Polling] Started — polling {Count} endpoints every {Interval}s",
            _config.Endpoints.Count, _config.PollIntervalSeconds);

        // Initial poll immediately, then tick on interval.
        await PollAllEndpointsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.PollIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PollAllEndpointsAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }

    private Task PollAllEndpointsAsync(CancellationToken ct)
    {
        var tasks = _config.Endpoints.Select(ep => PollEndpointAsync(ep, ct));
        return Task.WhenAll(tasks);
    }

    private async Task PollEndpointAsync(HttpEndpointConfig ep, CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var response = await client.GetAsync(ep.Url, ct);
            response.EnsureSuccessStatusCode();

            const long MaxBytes = 1 * 1024 * 1024; // 1 MB
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                _logger.LogWarning("[HTTP Polling] {DeviceId} response too large — skipped", ep.DeviceId);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var valueEl)) return;
            double value = valueEl.GetDouble();

            using var scope = _scopeFactory.CreateScope();
            var broadcaster = scope.ServiceProvider.GetRequiredService<SensorBroadcaster>();
            await broadcaster.ProcessReadingAsync(ep.SensorId, value, ct);

            _logger.LogDebug("[HTTP Polling] {DeviceId} → {SensorId} = {Value}", ep.DeviceId, ep.SensorId, value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("[HTTP Polling] {DeviceId} error: {Message}", ep.DeviceId, ex.Message);
        }
    }
}
