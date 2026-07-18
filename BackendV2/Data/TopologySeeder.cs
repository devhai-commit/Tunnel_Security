using BackendV2.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendV2.Data;

/// <summary>
/// Seeds the Node/Sensor topology that matches what NodePublisherSim simulates by default
/// (NODE_ID=1, publishing Light/WaterLevel/TemperatureHumidity/Radar every tick), so the
/// AppDbContext has a corresponding node + sensors for the readings BackendV2 decodes.
/// </summary>
public static class TopologySeeder
{
    private const string SimNodeId = "node-1";

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Nodes.AnyAsync(n => n.Id == SimNodeId, ct))
            return;

        db.Nodes.Add(new Node
        {
            Id = SimNodeId,
            Code = "NODE-01",
            Name = "Node giả lập 1 (NodePublisherSim)",
            Description = "Node giả lập dữ liệu cảm biến, publish qua NodePublisherSim (NODE_ID=1)",
            Latitude = 21.0285,
            Longitude = 105.8542,
            Status = NodeStatus.Online,
            NodeByteId = 1,
            IsHub = false
        });

        db.Cameras.Add(new Camera
        {
            Id = "CAM-HUB-01",
            Name = "Camera giả lập node 1",
            Description = "Camera giả lập, publish qua NodeSim (WebSocket ingest tới BackendV2)",
            NodeId = SimNodeId,
            StreamUrl = "ws://localhost:5080/ws/camera/CAM-HUB-01/view",
            Protocol = CameraProtocol.WebSocket,
            Status = CameraStatus.Offline,
            Resolution = "640x480",
            Fps = 15,
            Codec = "MJPEG"
        });

        db.Sensors.AddRange(
            new Sensor
            {
                Id = $"{SimNodeId}-light",
                NodeId = SimNodeId,
                Name = "Cảm biến ánh sáng",
                Type = SensorType.Light,
                Description = "Cường độ ánh sáng (lx)",
                SensorByteId = 1,
                Unit = "lx",
                WarningThreshold = 750,
                CriticalThreshold = 800
            },
            new Sensor
            {
                Id = $"{SimNodeId}-water",
                NodeId = SimNodeId,
                Name = "Cảm biến mực nước",
                Type = SensorType.WaterLevel,
                Description = "Độ sâu mực nước (m)",
                SensorByteId = 2,
                Unit = "m",
                WarningThreshold = 2.5,
                CriticalThreshold = 3
            },
            new Sensor
            {
                Id = $"{SimNodeId}-temp",
                NodeId = SimNodeId,
                Name = "Cảm biến nhiệt độ",
                Type = SensorType.Temperature,
                Description = "Nhiệt độ (°C)",
                SensorByteId = 3,
                Unit = "°C",
                WarningThreshold = 32,
                CriticalThreshold = 35
            },
            new Sensor
            {
                Id = $"{SimNodeId}-hum",
                NodeId = SimNodeId,
                Name = "Cảm biến độ ẩm",
                Type = SensorType.Humidity,
                Description = "Độ ẩm (%)",
                SensorByteId = 3,
                Unit = "%",
                WarningThreshold = 85,
                CriticalThreshold = 90
            },
            new Sensor
            {
                Id = $"{SimNodeId}-radar",
                NodeId = SimNodeId,
                Name = "Cảm biến radar",
                Type = SensorType.Radar,
                Description = "Radar — số đối tượng phát hiện",
                SensorByteId = 4,
                Unit = "đối tượng",
                WarningThreshold = 2,
                CriticalThreshold = 3
            });

        await db.SaveChangesAsync(ct);
    }
}
