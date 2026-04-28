using Center.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace Center.ViewModels
{
    public partial class DataStreamsViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isPaused = false;
        [ObservableProperty] private string _stationFilter = "All";
        [ObservableProperty] private int _totalReadingsReceived = 0;
        [ObservableProperty] private int _anomalyCount = 0;
        [ObservableProperty] private string _connectionStatus = "Disconnected";

        public BoundedObservableCollection<LogEntry> Entries { get; } = new(maxSize: 500, trimTo: 400);
        public ObservableCollection<SensorStreamEntry> SensorEntries { get; } = new();

        public void AddEntry(LogEntry entry)
        {
            if (IsPaused) return;
            if (StationFilter != "All" && !entry.Source.Contains(StationFilter)) return;
            Entries.AddBounded(entry);
        }

        public void AddSensorReading(SensorUpdateDto update)
        {
            if (IsPaused) return;

            TotalReadingsReceived++;

            bool isAnomaly = update.Type == "CO" && update.CurrentValue > 50
                          || update.Type == "Temperature" && update.CurrentValue > 45
                          || update.Type == "Humidity" && update.CurrentValue > 90
                          || update.Type == "Gas" && update.CurrentValue > 100;

            if (isAnomaly) AnomalyCount++;

            var entry = new SensorStreamEntry
            {
                Timestamp = update.LastReading.HasValue
                    ? new DateTimeOffset(update.LastReading.Value, TimeSpan.Zero)
                    : DateTimeOffset.UtcNow,
                SensorId = update.Id,
                SensorName = update.Name,
                SensorType = update.Type,
                NodeId = update.NodeId,
                NodeName = update.NodeName ?? update.NodeId,
                Value = update.CurrentValue,
                Unit = update.Unit,
                IsAnomaly = isAnomaly
            };

            if (SensorEntries.Count > 1000) SensorEntries.RemoveAt(0);
            SensorEntries.Insert(0, entry);

            // Also add to log stream
            var level = isAnomaly ? LogModels.LogLevel.Warning : LogModels.LogLevel.Info;
            AddEntry(new LogEntry
            {
                Timestamp = entry.Timestamp,
                Level = (Center.Models.LogLevel)level,
                Source = $"{update.NodeName ?? update.NodeId} / {update.Name}",
                Message = $"{update.Type}: {update.CurrentValue:F2} {update.Unit}"
                        + (isAnomaly ? " [ANOMALY]" : string.Empty)
            });
        }

        public void AddDeviceStatusChange(DeviceStatusUpdateDto update)
        {
            var level = update.CurrentStatus == "Offline"
                ? Center.Models.LogLevel.Warning
                : Center.Models.LogLevel.Info;

            AddEntry(new LogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = level,
                Source = update.Name,
                Message = $"Device {update.Id} status: {update.PreviousStatus} → {update.CurrentStatus}"
            });
        }

        public void SetConnected(bool connected)
        {
            ConnectionStatus = connected ? "Connected" : "Disconnected";
        }

        public void Clear()
        {
            Entries.Clear();
            SensorEntries.Clear();
            TotalReadingsReceived = 0;
            AnomalyCount = 0;
        }
    }

    public class SensorStreamEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public string SensorId { get; set; } = string.Empty;
        public string SensorName { get; set; } = string.Empty;
        public string SensorType { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public bool IsAnomaly { get; set; }

        public string DisplayTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");
        public string DisplayValue => $"{Value:F2} {Unit}";
        public string AnomalyBadge => IsAnomaly ? "⚠" : string.Empty;
    }

    // Alias to avoid LogLevel naming conflict
    internal static class LogModels
    {
        internal enum LogLevel { Debug, Info, Warning, Error, System }
    }
}
