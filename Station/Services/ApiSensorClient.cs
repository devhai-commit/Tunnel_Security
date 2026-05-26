using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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

    /// <summary>Event fired when a sensor value is updated from the API.</summary>
    public event EventHandler<ApiSensorUpdate>? SensorUpdated;

    /// <summary>Event fired when connection status changes.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>Event fired when a hardware device sends a JOIN_REQUEST.</summary>
    public event EventHandler<JoinRequestNotification>? NewJoinRequest;

    /// <summary>Event fired when a join request is decided (accepted/rejected).</summary>
    public event EventHandler<JoinRequestDecision>? JoinRequestDecided;

    /// <summary>
    /// Whether the client is currently connected to the API.
    /// </summary>
    public bool IsConnected => _isConnected;

    private readonly HttpClient _http;

    public ApiSensorClient(string? apiBaseUrl = null)
    {
        _apiBaseUrl = apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:5280";
        _http = new HttpClient { BaseAddress = new Uri(_apiBaseUrl), Timeout = TimeSpan.FromSeconds(10) };
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

        // Handle device join requests
        _connection.On<JoinRequestNotification>("NewJoinRequest", (req) =>
        {
            NewJoinRequest?.Invoke(this, req);
        });

        // Handle join request decisions (from another operator or server timeout)
        _connection.On<JoinRequestDecision>("JoinRequestDecided", (decision) =>
        {
            JoinRequestDecided?.Invoke(this, decision);
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

    /// <summary>Phê duyệt yêu cầu gia nhập, gán NodeByteId cho thiết bị.</summary>
    public async Task<bool> ApproveJoinRequestAsync(int requestId, byte nodeByteId)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { nodeByteId });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"/api/device-joins/{requestId}/approve", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] ApproveJoin failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Từ chối yêu cầu gia nhập.</summary>
    public async Task<bool> RejectJoinRequestAsync(int requestId, string? reason = null)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { reason });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"/api/device-joins/{requestId}/reject", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] RejectJoin failed: {ex.Message}");
            return false;
        }
    }

    public async Task<IReadOnlyList<JoinRequestNotification>> GetPendingJoinRequestsAsync()
    {
        try
        {
            var response = await _http.GetAsync("/api/device-joins?status=Pending");
            if (!response.IsSuccessStatusCode)
                return Array.Empty<JoinRequestNotification>();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var items = JsonSerializer.Deserialize<List<JoinRequestNotification>>(json, options);
            if (items is null)
                return Array.Empty<JoinRequestNotification>();
            return items;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiSensorClient] GetPendingJoinRequests failed: {ex.Message}");
            return Array.Empty<JoinRequestNotification>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _http.Dispose();
    }
}

/// <summary>DTO for sensor updates received from the API via SignalR.</summary>
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

/// <summary>Thông báo thiết bị mới muốn gia nhập — nhận qua SignalR "NewJoinRequest".</summary>
public class JoinRequestNotification
{
    public int    Id              { get; set; }
    public string MacAddress      { get; set; } = string.Empty;
    public uint   HardwareId      { get; set; }
    public string FirmwareVersion { get; set; } = string.Empty;
    public string RequestedAt     { get; set; } = string.Empty;
}

/// <summary>Kết quả phê duyệt/từ chối — nhận qua SignalR "JoinRequestDecided".</summary>
public class JoinRequestDecision
{
    public int    Id         { get; set; }
    public string Status     { get; set; } = string.Empty; // "Accepted" | "Rejected"
    public string MacAddress { get; set; } = string.Empty;
    public byte   NodeByteId { get; set; }
}
