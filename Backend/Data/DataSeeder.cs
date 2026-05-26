using Backend.Mock;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public class DataSeeder
{
    private readonly TunnelDbContext _db;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(TunnelDbContext db, ILogger<DataSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        // Chỉ skip khi topology đã có đủ dữ liệu. Một CSDL có Station nhưng thiếu
        // Lines/Nodes vẫn cần được seed lại để Station đọc được danh sách thiết bị.
        var hasStations = await _db.Stations.AnyAsync();
        var hasLines = await _db.Lines.AnyAsync();
        var hasNodes = await _db.Nodes.AnyAsync();

        if (hasStations && hasLines && hasNodes)
        {
            _logger.LogInformation("Database already seeded — skipping");
            return;
        }

        if (hasStations || hasLines || hasNodes)
        {
            _logger.LogWarning(
                "Existing topology is incomplete. Clearing current topology before reseeding. Stations={Stations}, Lines={Lines}, Nodes={Nodes}",
                hasStations, hasLines, hasNodes);

            await ClearTopologyAsync();
        }

        _logger.LogInformation(
            "Seeding database from MockData... Existing state: Stations={Stations}, Lines={Lines}, Nodes={Nodes}",
            hasStations, hasLines, hasNodes);

        var stations = MockData.GetStations();

        // Flatten để insert đúng thứ tự (EF Core tự resolve navigation nếu dùng AddRange)
        foreach (var station in stations)
        {
            _db.Stations.Add(station);
        }

        // Seed camera devices cho các node có CameraId
        var allNodes = stations.SelectMany(s => s.Lines).SelectMany(l => l.Nodes);
        foreach (var node in allNodes.Where(n => n.CameraId != null))
        {
            _db.Cameras.Add(new CameraDevice
            {
                Id = node.CameraId!,
                NodeId = node.Id,
                Name = $"Camera {node.Name}",
                StreamUrl = $"rtsp://localhost:8554/{node.CameraId}",
                Protocol = CameraProtocol.RTSP,
                Status = CameraStatus.Offline,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Database seeded: {StationCount} stations, {CameraCount} cameras",
            stations.Count, await _db.Cameras.CountAsync());
    }

    private async Task ClearTopologyAsync()
    {
        await _db.AlertNotes.ExecuteDeleteAsync();
        await _db.Alerts.ExecuteDeleteAsync();
        await _db.CameraSnapshots.ExecuteDeleteAsync();
        await _db.VideoClips.ExecuteDeleteAsync();
        await _db.Cameras.ExecuteDeleteAsync();
        await _db.Sensors.ExecuteDeleteAsync();
        await _db.Nodes.ExecuteDeleteAsync();
        await _db.Lines.ExecuteDeleteAsync();
        await _db.Stations.ExecuteDeleteAsync();
    }
}
