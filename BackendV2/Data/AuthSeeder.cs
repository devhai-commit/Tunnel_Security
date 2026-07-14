using BackendV2.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BackendV2.Data;

public static class AuthSeeder
{
    private sealed record StandardRole(string Code, string Name, string[] PermissionCodes);
    private sealed record StandardFunctionGroup(string Code, string Name, string Description);

    private static readonly StandardFunctionGroup[] StandardFunctionGroups =
    {
        new("DASHBOARD_MONITORING", "Giám sát tổng quan", "Màn hình trung tâm, tổng hợp trạng thái toàn trạm"),
        new("MONITORING_DETAIL", "Giám sát chi tiết", "Giao diện chuyên dụng cho giám sát viên quan sát dữ liệu, camera, AI realtime"),
        new("DEVICE_MANAGEMENT", "Quản lý thiết bị", "Quản lý tuyến, cụm, node, sensor, camera, thiết bị ngoại vi và điều khiển thiết bị"),
        new("ALERT_EVENT_MANAGEMENT", "Quản lý cảnh báo", "Xem, lọc, xác nhận, xử lý, đóng/mở lại cảnh báo và sự kiện"),
        new("DATA_HISTORY_REPORTING", "Báo cáo và phân tích xu hướng", "Tra cứu dữ liệu, xem lịch sử, thống kê, báo cáo và phân tích xu hướng"),
        new("SYSTEM_ADMINISTRATION", "Quản trị hệ thống", "Quản lý user, vai trò, phân quyền, cấu hình hệ thống và audit log")
    };

    private static readonly StandardRole[] StandardRoles =
    {
        new("VIEWER", "Viewer", new[] { "DASHBOARD_MONITORING", "DATA_HISTORY_REPORTING" }),
        new("OPERATOR", "Operator", new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT" }),
        new("ADMIN", "Admin", new[]
        {
            "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT",
            "ALERT_EVENT_MANAGEMENT", "DATA_HISTORY_REPORTING", "SYSTEM_ADMINISTRATION"
        })
    };

    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        IConfiguration configuration,
        CancellationToken ct = default)
    {
        var functionGroupsByCode = await UpsertFunctionGroupsAsync(db, ct);
        var rolesByCode = await UpsertRolesAsync(db, functionGroupsByCode, ct);
        await EnsureBootstrapAdminAsync(db, passwordHasher, configuration, rolesByCode, ct);
    }

    private static async Task<Dictionary<string, FunctionGroup>> UpsertFunctionGroupsAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.FunctionGroups.ToDictionaryAsync(g => g.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var standard in StandardFunctionGroups)
        {
            if (existing.TryGetValue(standard.Code, out var group))
            {
                group.Name = standard.Name;
                group.Description = standard.Description;
                continue;
            }

            group = new FunctionGroup
            {
                Code = standard.Code,
                Name = standard.Name,
                Description = standard.Description
            };
            db.FunctionGroups.Add(group);
            existing[standard.Code] = group;
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }

    private static async Task<Dictionary<string, Role>> UpsertRolesAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, FunctionGroup> functionGroupsByCode,
        CancellationToken ct)
    {
        var existingRoles = await db.Roles.ToDictionaryAsync(r => r.Code, StringComparer.OrdinalIgnoreCase, ct);
        var existingLinks = await db.RoleFunctionGroups.ToListAsync(ct);

        foreach (var standard in StandardRoles)
        {
            if (!existingRoles.TryGetValue(standard.Code, out var role))
            {
                role = new Role { Code = standard.Code, Name = standard.Name };
                db.Roles.Add(role);
                existingRoles[standard.Code] = role;
            }
            else
            {
                role.Name = standard.Name;
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var standard in StandardRoles)
        {
            var role = existingRoles[standard.Code];
            var desiredGroupIds = standard.PermissionCodes
                .Where(functionGroupsByCode.ContainsKey)
                .Select(code => functionGroupsByCode[code].Id)
                .ToHashSet();

            var currentLinks = existingLinks.Where(l => l.RoleId == role.Id).ToList();
            var currentGroupIds = currentLinks.Select(l => l.FunctionGroupId).ToHashSet();

            foreach (var groupId in desiredGroupIds.Except(currentGroupIds))
            {
                db.RoleFunctionGroups.Add(new RoleFunctionGroup { RoleId = role.Id, FunctionGroupId = groupId });
            }
        }

        await db.SaveChangesAsync(ct);
        return existingRoles;
    }

    private static async Task EnsureBootstrapAdminAsync(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        IConfiguration configuration,
        IReadOnlyDictionary<string, Role> rolesByCode,
        CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
            return;

        if (!rolesByCode.TryGetValue("ADMIN", out var adminRole))
            return;

        var bootstrapPassword = configuration["Auth:BootstrapAdminPassword"];
        if (string.IsNullOrWhiteSpace(bootstrapPassword))
            return;

        var admin = new User
        {
            Username = "admin",
            FullName = "Administrator",
            RoleId = adminRole.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        admin.PasswordHash = passwordHasher.HashPassword(admin, bootstrapPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
    }
}
