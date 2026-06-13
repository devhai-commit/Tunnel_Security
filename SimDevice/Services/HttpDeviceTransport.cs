using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SimDevice.Config;
using SimDevice.Models;

namespace SimDevice.Services;

/// <summary>
/// Sends a single sensor reading to the Backend via
/// POST /api/sensors/{id}/measurements
/// </summary>
public sealed class HttpDeviceTransport
{
    private readonly IHttpClientFactory _factory;
    private readonly SimOptions _opts;
    private readonly ILogger<HttpDeviceTransport> _logger;

    public HttpDeviceTransport(
        IHttpClientFactory factory,
        IOptions<SimOptions> opts,
        ILogger<HttpDeviceTransport> logger)
    {
        _factory = factory;
        _opts    = opts.Value;
        _logger  = logger;
    }

    public async Task PushAsync(SimSensor sensor, double value, CancellationToken ct)
    {
        var url = $"{_opts.BackendUrl.TrimEnd('/')}/api/sensors/{sensor.Id}/measurements";
        using var http = _factory.CreateClient("SimDevice");
        try
        {
            var response = await http.PostAsJsonAsync(url, new { value }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Push failed for {SensorId}: HTTP {StatusCode}",
                    sensor.Id, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Push error for {SensorId}: {Message}", sensor.Id, ex.Message);
        }
    }
}
