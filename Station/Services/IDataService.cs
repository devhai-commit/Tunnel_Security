using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Station.Models;

namespace Station.Services;

/// <summary>
/// Interface chung cho MockDataService và RealDataService.
/// ViewModels dùng interface này thay vì dùng MockDataService.Instance trực tiếp.
/// </summary>
public interface IDataService
{
    IReadOnlyList<SimulatedSensor> Sensors { get; }
    IReadOnlyList<SimulatedCamera> Cameras { get; }
    IReadOnlyList<TunnelNode> Nodes { get; }
    IReadOnlyList<TunnelLine> Lines { get; }
    ObservableCollection<Alert> ActiveAlerts { get; }
    ObservableCollection<Alert> AlertHistory { get; }

    event EventHandler<SensorTickEventArgs>? SensorTick;
    event EventHandler<AlertGeneratedEventArgs>? AlertGenerated;
    event EventHandler? TopologyLoaded;

    /// <summary>Thiết bị phần cứng mới gửi yêu cầu gia nhập.</summary>
    event EventHandler<JoinRequestNotification>? NewJoinRequest;

    void Start();
    void Stop();

    Task<IReadOnlyList<JoinRequestNotification>> GetPendingJoinRequestsAsync();
    Task<bool> ApproveJoinRequestAsync(int requestId, byte nodeByteId);
    Task<bool> RejectJoinRequestAsync(int requestId, string? reason = null);
}
