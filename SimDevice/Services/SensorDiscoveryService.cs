using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SimDevice.Config;
using SimDevice.Models;

namespace SimDevice.Services;

/// <summary>
/// Fetches the full sensor topology from the Backend API at startup.
/// Returns a flat list of SimSensor objects ready for simulation.
/// </summary>
public sealed class SensorDiscoveryService
{
    private readonly IHttpClientFactory _factory;
    private readonly SimOptions _opts;
    private readonly ILogger<SensorDiscoveryService> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SensorDiscoveryService(
        IHttpClientFactory factory,
        IOptions<SimOptions> opts,
        ILogger<SensorDiscoveryService> logger)
    {
        _factory = factory;
        _opts    = opts.Value;
        _logger  = logger;
    }

    public async Task<List<SimSensor>> DiscoverAsync(CancellationToken ct)
    {
        var url = $"{_opts.BackendUrl.TrimEnd('/')}/api/stations/{_opts.StationId}";
        _logger.LogInformation("Discovering sensors from {Url}", url);

        StationDto? station;
        using var http = _factory.CreateClient("SimDevice");
        try
        {
            station = await http.GetFromJsonAsync<StationDto>(url, _json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch station topology from {Url}", url);
            throw;
        }

        if (station is null)
        {
            _logger.LogError("Backend returned null for station {StationId}", _opts.StationId);
            throw new InvalidOperationException($"Station {_opts.StationId} not found.");
        }

        var sensors = station.Lines
            .SelectMany(l => l.Nodes)
            .SelectMany(n => n.Sensors)
            .Where(s => s.IsEnabled)
            .Select(s => new SimSensor
            {
                Id               = s.Id,
                NodeId           = s.NodeId,
                Type             = s.Type.ToString(),
                Name             = s.Name,
                Unit             = s.Unit,
                WarningThreshold  = s.WarningThreshold,
                CriticalThreshold = s.CriticalThreshold,
                CurrentValue      = s.CurrentValue ?? 0
            })
            .ToList();

        _logger.LogInformation(
            "Discovered {Count} enabled sensors across {Lines} lines",
            sensors.Count,
            station.Lines.Count);

        return sensors;
    }
}
