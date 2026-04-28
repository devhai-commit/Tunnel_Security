namespace SimDevice.Config;

public sealed class SimOptions
{
    public const string Section = "Simulation";

    /// <summary>Backend base URL, e.g. http://localhost:5280</summary>
    public string BackendUrl { get; init; } = "http://localhost:5280";

    /// <summary>Station ID to simulate devices for</summary>
    public string StationId { get; init; } = "ST01";

    /// <summary>Tick interval in milliseconds between sensor pushes</summary>
    public int TickMs { get; init; } = 1500;

    /// <summary>
    /// When true, each sensor value is pushed individually.
    /// When false, all sensors are batched in one tick (still one HTTP call each,
    /// but fired in parallel within the same tick).
    /// </summary>
    public bool ParallelPush { get; init; } = true;

    /// <summary>
    /// Max degree of parallelism for HTTP pushes per tick.
    /// 0 = unlimited (one Task per sensor).
    /// </summary>
    public int MaxConcurrency { get; init; } = 10;
}
