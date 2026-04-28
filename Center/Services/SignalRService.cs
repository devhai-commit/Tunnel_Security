using Center.Models;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;

namespace Center.Services
{
    public class SignalRService
    {
        private HubConnection? _connection;

        public event Action<StationInfo>? StationUpdated;
        public event Action<AlertEntry>? AlertReceived;
        public event Action<LogEntry>? LogReceived;
        public event Action<SensorUpdateDto>? SensorUpdated;
        public event Action<DeviceStatusUpdateDto>? DeviceStatusChanged;
        public event Action? Reconnected;
        public event Action? Disconnected;

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public async Task StartAsync(string hubUrl)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<StationInfo>("StationUpdated",       s => StationUpdated?.Invoke(s));
            _connection.On<AlertEntry>( "AlertReceived",        a => AlertReceived?.Invoke(a));
            _connection.On<LogEntry>(   "LogReceived",          l => LogReceived?.Invoke(l));
            _connection.On<SensorUpdateDto>("SensorUpdated",    s => SensorUpdated?.Invoke(s));
            _connection.On<DeviceStatusUpdateDto>("DeviceStatusChanged", d => DeviceStatusChanged?.Invoke(d));

            _connection.Reconnected    += _ => { Reconnected?.Invoke();  return Task.CompletedTask; };
            _connection.Closed         += _ => { Disconnected?.Invoke(); return Task.CompletedTask; };

            try { await _connection.StartAsync(); }
            catch { /* connection failed, will retry via AutoReconnect */ }
        }

        public async Task StopAsync()
        {
            if (_connection != null)
                await _connection.StopAsync();
        }
    }
}
