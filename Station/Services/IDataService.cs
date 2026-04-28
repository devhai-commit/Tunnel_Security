using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    IReadOnlyList<TunnelLine> Lines { get; }
    ObservableCollection<Alert> ActiveAlerts { get; }
    ObservableCollection<Alert> AlertHistory { get; }

    event EventHandler<SensorTickEventArgs>? SensorTick;
    event EventHandler<AlertGeneratedEventArgs>? AlertGenerated;
    event EventHandler? TopologyLoaded;

    void Start();
    void Stop();
}
