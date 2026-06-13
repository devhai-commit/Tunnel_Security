using Microsoft.AspNetCore.SignalR;

namespace Backend.Hubs;

public class SensorHub : Hub
{
    // Client gọi để subscribe theo trạm
    public async Task JoinStation(string stationId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"station:{stationId}");

    public async Task LeaveStation(string stationId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"station:{stationId}");

    // Client gọi để subscribe theo node cụ thể
    public async Task JoinNode(string nodeId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"node:{nodeId}");

    public async Task LeaveNode(string nodeId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"node:{nodeId}");
}
