namespace SimDevice.Models;

// Minimal DTOs mirroring Backend API responses — only fields SimDevice needs.

public sealed record SensorDto(
    string Id,
    string NodeId,
    int Type,
    string Name,
    string Unit,
    double? WarningThreshold,
    double? CriticalThreshold,
    double? CurrentValue,
    bool IsEnabled);

public sealed record NodeDto(string Id, List<SensorDto> Sensors);
public sealed record LineDto(string Id, List<NodeDto> Nodes);
public sealed record StationDto(string Id, List<LineDto> Lines);
