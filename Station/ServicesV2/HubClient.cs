using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Station.ServicesV2
{
    /// <summary>
    /// SignalR client cho BackendV2 SensorHub (/hubs/sensors). Backend broadcast một event
    /// duy nhất "NewReading" mang object Reading thô (không phải DTO đã flatten như
    /// Backend v1's "SensorUpdated") — mỗi reading ứng với đúng 1 sensor.
    /// </summary>
    public class HubClient : IAsyncDisposable
    {
        private readonly string _baseUrl;
        private readonly Func<string?>? _accessTokenProvider;
        private HubConnection? _connection;
        private bool _isConnected;

        public event EventHandler<ReadingDto>? ReadingReceived;
        public event EventHandler<bool>? ConnectionChanged;

        public bool IsConnected => _isConnected;

        public HubClient(string baseUrl, Func<string?>? accessTokenProvider = null)
        {
            _baseUrl = baseUrl;
            _accessTokenProvider = accessTokenProvider;
        }

        public async Task ConnectAsync()
        {
            if (_connection != null)
            {
                await DisconnectAsync();
            }

            _connection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/hubs/sensors", options =>
                {
                    if (_accessTokenProvider != null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_accessTokenProvider());
                    }
                })
                .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) })
                .AddJsonProtocol(options =>
                {
                    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                })
                .Build();

            _connection.On<ReadingDto>("NewReading", reading =>
            {
                ReadingReceived?.Invoke(this, reading);
            });

            _connection.Reconnecting += _ =>
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                return Task.CompletedTask;
            };

            _connection.Reconnected += _ =>
            {
                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
                return Task.CompletedTask;
            };

            _connection.Closed += _ =>
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                return Task.CompletedTask;
            };

            try
            {
                await _connection.StartAsync();
                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
            }
            catch
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_connection == null) return;

            try
            {
                await _connection.StopAsync();
            }
            finally
            {
                await _connection.DisposeAsync();
                _connection = null;
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Khớp field-for-field với BackendV2.Models.Reading. Level là int vì BackendV2 không
    /// cấu hình JsonStringEnumConverter — 0=Normal, 1=Warning, 2=Critical.
    /// </summary>
    public class ReadingDto
    {
        public int Id { get; set; }
        public string SensorId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public string? Description { get; set; }
        public string? NodeId { get; set; }
        public short? NodeByteId { get; set; }
        public short? SensorByteId { get; set; }
        public short? Seq { get; set; }
        public int Level { get; set; }
        public bool Crc8Ok { get; set; }
    }
}
