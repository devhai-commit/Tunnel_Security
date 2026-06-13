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
        // Chỉ seed khi chưa có dữ liệu
        if (await _db.Stations.AnyAsync())
        {
            _logger.LogInformation("Database already seeded — skipping");
            return;
        }

        _logger.LogInformation("Seeding database from MockData...");

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
}
