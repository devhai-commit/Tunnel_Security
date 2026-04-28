using SimDevice.Models;

namespace SimDevice.Services;

/// <summary>
/// Generates the next sensor value using mean-reverting random walk —
/// the same algorithm as Backend's BackgroundSensorSimulation.
/// </summary>
public sealed class RandomWalkGenerator
{
    private readonly Random _rng = new();

    public double Next(SimSensor sensor)
    {
        double current  = sensor.CurrentValue;
        double nominal  = (sensor.WarningThreshold ?? 30) * 0.5;
        double drift    = (sensor.CriticalThreshold ?? 50) * 0.1;
        double reversion = (nominal - current) * 0.05;
        double noise    = (_rng.NextDouble() * 2 - 1) * drift;

        // Occasional spike (1.5% probability)
        if (_rng.NextDouble() < 0.015)
            noise += (_rng.NextDouble() > 0.5 ? 1 : -1) * drift * 6;

        double next = current + reversion + noise;
        double max  = sensor.CriticalThreshold.HasValue ? sensor.CriticalThreshold.Value * 1.5 : 100;
        return Math.Clamp(next, 0, max);
    }
}
