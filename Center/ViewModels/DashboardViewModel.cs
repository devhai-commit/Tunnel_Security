using Center.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Center.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty] private int _totalStations;
        [ObservableProperty] private int _activeStreams;
        [ObservableProperty] private double _systemHealthPercent;
        [ObservableProperty] private string _dataRateDisplay = "0 MB/s";
        [ObservableProperty] private int _totalReadings;
        [ObservableProperty] private int _activeNodes;
        [ObservableProperty] private int _anomalyCount;
        [ObservableProperty] private bool _isSignalRConnected;
        [ObservableProperty] private string _connectionStatusText = "Disconnected";

        // Track distinct active nodes from sensor updates
        private readonly HashSet<string> _activeNodeIds = new();

        public ObservableCollection<StationInfo> RecentStations { get; } = new();
        public ObservableCollection<LogEntry> LiveLogs { get; } = new();

        public void AddLog(LogEntry entry)
        {
            if (LiveLogs.Count > 200) LiveLogs.RemoveAt(0);
            LiveLogs.Add(entry);
        }

        public void UpdateStation(StationInfo station)
        {
            for (int i = 0; i < RecentStations.Count; i++)
            {
                if (RecentStations[i].StationCode == station.StationCode)
                {
                    RecentStations[i] = station;
                    return;
                }
            }
            if (RecentStations.Count < 5)
                RecentStations.Add(station);
        }

        public void OnSensorUpdated(SensorUpdateDto update)
        {
            TotalReadings++;
            _activeNodeIds.Add(update.NodeId);
            ActiveNodes = _activeNodeIds.Count;

            bool isAnomaly = update.Type == "CO" && update.CurrentValue > 50
                          || update.Type == "Temperature" && update.CurrentValue > 45
                          || update.Type == "Humidity" && update.CurrentValue > 90
                          || update.Type == "Gas" && update.CurrentValue > 100;
            if (isAnomaly) AnomalyCount++;

            ActiveStreams = ActiveNodes;
            SystemHealthPercent = _activeNodeIds.Count > 0
                ? System.Math.Max(0, 100.0 - (AnomalyCount * 2.0))
                : 100.0;
        }

        public void OnDeviceStatusChanged(DeviceStatusUpdateDto update)
        {
            if (update.CurrentStatus == "Offline")
                _activeNodeIds.Remove(update.Id);
            else
                _activeNodeIds.Add(update.Id);

            ActiveNodes = _activeNodeIds.Count;
        }

        public void SetSignalRConnected(bool connected)
        {
            IsSignalRConnected = connected;
            ConnectionStatusText = connected ? "Live" : "Disconnected";
        }
    }
}
