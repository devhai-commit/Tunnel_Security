using System;

namespace Center.Models
{
    /// <summary>
    /// Matches the anonymous payload from Backend/Services/SensorBroadcaster.cs
    /// sent via SignalR "SensorUpdated" event.
    /// </summary>
    public class SensorUpdateDto
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double CurrentValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime? LastReading { get; set; }
        public string? NodeStatus { get; set; }
        public string? NodeName { get; set; }
    }

    /// <summary>
    /// Matches the anonymous payload from Backend/Services/DeviceHealthService.cs
    /// sent via SignalR "DeviceStatusChanged" event.
    /// </summary>
    public class DeviceStatusUpdateDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PreviousStatus { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public DateTime? LastOnline { get; set; }
    }
}
