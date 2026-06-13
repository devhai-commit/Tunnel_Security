using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Station.Services;

/// <summary>
/// Client for connecting to the Backend SignalR hub to receive real-time sensor updates.
/// </summary>
public class ApiSensorClient : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _apiBaseUrl;
    private bool _isConnected;

    /// <summary>
    /// Event fired when a sensor value is updated from the API.
    /// </summary>
    public event EventHandler<ApiSensorUpdate>? SensorUpdated;

    /// <summary>
    /// Event fired when connection status changes.
    /// </summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Whether the client is currently connected to the API.
    /// </summary>
    public bool IsConnected => _isConnected;

    public ApiSensorClient(string? apiBaseUrl = null)
    {
        // Default to the backend URL from environment or localhost
        _apiBaseUrl = apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:5280";
    }

    /// <summary>
    /// Connects to the Backend SignalR hub.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_connection != null)
        {
            await DisconnectAsync();
        }

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_apiBaseUrl}/hubs/sensors")
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
            })
            .Build();

        // Handle incoming sensor updates
        _connection.On<ApiSensorUpdate>("SensorUpdated", (update) =>
        {
            SensorUpdated?.Invoke(this, update);
        });

        // Handle connection state changes
        _connection.Reconnecting += (error) =>
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] Reconnecting... Error: {error?.Message}");
            return Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) =>
        {
            _isConnected = true;
            ConnectionChanged?.Invoke(this, true);
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] Reconnected with ID: {connectionId}");
            return Task.CompletedTask;
        };

        _connection.Closed += (error) =>
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] Connection closed. Error: {error?.Message}");
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync();
            _isConnected = true;
            ConnectionChanged?.Invoke(this, true);
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] Connected to {_apiBaseUrl}/hubs/sensors");
        }
        catch (Exception ex)
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] Failed to connect: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Disconnects from the Backend SignalR hub.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            try
            {
                await _connection.StopAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] Error stopping: {ex.Message}");
            }
            finally
            {
                await _connection.DisposeAsync();
                _connection = null;
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}

/// <summary>
/// DTO for sensor updates received from the API via SignalR.
/// </summary>
public class ApiSensorUpdate
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime LastReading { get; set; }
    public string Level { get; set; } = string.Empty;
    public string NodeStatus { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
}
