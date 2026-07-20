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

    private const string CameraId = "CAM-HUB-01";

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Each entity is guarded independently — earlier revisions of this seeder only
        // checked Nodes and returned early, so a DB that already had "node-1" from before
        // the Camera/Sensor inserts existed would never backfill them on later runs.
        if (!await db.Nodes.AnyAsync(n => n.Id == SimNodeId, ct))
        {
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
        }

        if (!await db.Cameras.AnyAsync(c => c.Id == CameraId, ct))
        {
            db.Cameras.Add(new Camera
            {
                Id = CameraId,
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
        }

        if (!await db.Sensors.AnyAsync(s => s.NodeId == SimNodeId, ct))
        {
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
        }

        await SeedAdditionalTunnelNodesAsync(db, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Extra demo topology (node-2..node-20 + CAM-HUB-02..CAM-HUB-20) so the map/dashboard has
    /// more than a single node to show. These are display-only — only node-1 is ever fed real
    /// readings by NodePublisherSim, so NodeByteId is left null here (no real wire-protocol id).
    /// </summary>
    private static async Task SeedAdditionalTunnelNodesAsync(AppDbContext db, CancellationToken ct)
    {
        const int firstExtraNode = 2;
        const int lastExtraNode = 20;
        const double baseLatitude = 21.0285;
        const double baseLongitude = 105.8542;

        var existingNodeIds = (await db.Nodes.Select(n => n.Id).ToListAsync(ct)).ToHashSet();
        var existingCameraIds = (await db.Cameras.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var nodeIdsWithSensors = (await db.Sensors.Select(s => s.NodeId).Distinct().ToListAsync(ct)).ToHashSet();

        for (var n = firstExtraNode; n <= lastExtraNode; n++)
        {
            var nodeId = $"node-{n}";
            var cameraId = $"CAM-HUB-{n:D2}";

            if (!existingNodeIds.Contains(nodeId))
            {
                db.Nodes.Add(new Node
                {
                    Id = nodeId,
                    Code = $"NODE-{n:D2}",
                    Name = $"Node giám sát hầm số {n:D2}",
                    Description = "Node giả lập bổ sung cho demo topology (không nhận dữ liệu thực từ NodePublisherSim)",
                    Latitude = baseLatitude + n * 0.0003,
                    Longitude = baseLongitude + n * 0.0006,
                    Status = NodeStatus.Online,
                    NodeByteId = null,
                    IsHub = false
                });
            }

            if (!existingCameraIds.Contains(cameraId))
            {
                db.Cameras.Add(new Camera
                {
                    Id = cameraId,
                    Name = $"Camera giám sát hầm số {n:D2}",
                    Description = "Camera giả lập bổ sung cho demo topology",
                    NodeId = nodeId,
                    StreamUrl = $"ws://localhost:5080/ws/camera/{cameraId}/view",
                    Protocol = CameraProtocol.WebSocket,
                    Status = CameraStatus.Offline,
                    Resolution = "640x480",
                    Fps = 15,
                    Codec = "MJPEG"
                });
            }

            if (!nodeIdsWithSensors.Contains(nodeId))
            {
                db.Sensors.AddRange(
                new Sensor
                {
                    Id = $"{nodeId}-light",
                    NodeId = nodeId,
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
                    Id = $"{nodeId}-water",
                    NodeId = nodeId,
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
                    Id = $"{nodeId}-temp",
                    NodeId = nodeId,
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
                    Id = $"{nodeId}-hum",
                    NodeId = nodeId,
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
                    Id = $"{nodeId}-radar",
                    NodeId = nodeId,
                    Name = "Cảm biến radar",
                    Type = SensorType.Radar,
                    Description = "Radar — số đối tượng phát hiện",
                    SensorByteId = 4,
                    Unit = "đối tượng",
                    WarningThreshold = 2,
                    CriticalThreshold = 3
                });
            }
        }
    }
}
