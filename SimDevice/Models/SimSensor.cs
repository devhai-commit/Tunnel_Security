namespace SimDevice.Models;

/// <summary>
/// Runtime state of a single simulated sensor.
/// Populated from Backend API at startup and updated each tick.
/// </summary>
public sealed class SimSensor
{
    public string Id { get; init; } = default!;
    public string NodeId { get; init; } = default!;
    public string Type { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string Unit { get; init; } = default!;

    public double? WarningThreshold { get; init; }
    public double? CriticalThreshold { get; init; }

    /// <summary>Current simulated value — updated each tick by RandomWalkGenerator.</summary>
    public double CurrentValue { get; set; }
}
