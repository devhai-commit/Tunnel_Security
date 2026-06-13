using Microsoft.Extensions.Options;
using SimDevice.Config;
using SimDevice.Models;

namespace SimDevice.Services;

/// <summary>
/// BackgroundService that:
///   1. Discovers sensors from Backend at startup.
///   2. Ticks every TickMs ms, generates new values, and pushes them via HTTP.
/// </summary>
public sealed class SimulationWorker : BackgroundService
{
    private readonly SensorDiscoveryService _discovery;
    private readonly HttpDeviceTransport _transport;
    private readonly RandomWalkGenerator _walker;
    private readonly SimOptions _opts;
    private readonly ILogger<SimulationWorker> _logger;

    public SimulationWorker(
        SensorDiscoveryService discovery,
        HttpDeviceTransport transport,
        RandomWalkGenerator walker,
        IOptions<SimOptions> opts,
        ILogger<SimulationWorker> logger)
    {
        _discovery = discovery;
        _transport = transport;
        _walker    = walker;
        _opts      = opts.Value;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SimDevice starting — Backend: {Url}, Station: {StationId}, Tick: {TickMs}ms",
            _opts.BackendUrl, _opts.StationId, _opts.TickMs);

        List<SimSensor> sensors = await DiscoverWithRetryAsync(stoppingToken);

        _logger.LogInformation("Simulation running for {Count} sensors", sensors.Count);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opts.TickMs));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TickAsync(sensors, stoppingToken);
        }
    }

    private async Task TickAsync(List<SimSensor> sensors, CancellationToken ct)
    {
        if (_opts.ParallelPush)
        {
            var semaphore = _opts.MaxConcurrency > 0
                ? new SemaphoreSlim(_opts.MaxConcurrency)
                : null;

            var tasks = sensors.Select(async sensor =>
            {
                if (semaphore is not null) await semaphore.WaitAsync(ct);
                try
                {
                    double next = _walker.Next(sensor);
                    sensor.CurrentValue = next;
                    await _transport.PushAsync(sensor, next, ct);
                }
                finally
                {
                    semaphore?.Release();
                }
            });

            await Task.WhenAll(tasks);
            semaphore?.Dispose();
        }
        else
        {
            foreach (var sensor in sensors)
            {
                double next = _walker.Next(sensor);
                sensor.CurrentValue = next;
                await _transport.PushAsync(sensor, next, ct);
            }
        }
    }

    /// <summary>Retries discovery every 5 seconds until Backend is reachable.</summary>
    private async Task<List<SimSensor>> DiscoverWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                return await _discovery.DiscoverAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Backend not reachable, retrying in 5s... ({Message})", ex.Message);
                await Task.Delay(5_000, ct);
            }
        }

        ct.ThrowIfCancellationRequested();
        return [];
    }
}
