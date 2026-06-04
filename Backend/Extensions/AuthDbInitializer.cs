using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TunnelSecurity.Data.Auth;
using TunnelSecurity.Data.Auth.Models;

namespace TunnelSecurity.Backend.Extensions
{
    public static class AuthDbInitializer
    {
        private sealed record StandardRole(string Code, string Name);
        private sealed record StandardFunctionGroup(string Code, string Name, string Description);

        private static readonly StandardRole[] StandardRoles =
        {
            new("VIEWER", "Viewer"),
            new("OPERATOR", "Operator"),
            new("ADMIN", "Admin")
        };

        private static readonly StandardFunctionGroup[] StandardFunctionGroups =
        {
            new("DASHBOARD_MONITORING", "Giám sát tổng quan", "Màn hình trung tâm, tổng hợp trạng thái toàn trạm"),
            new("MONITORING_DETAIL", "Giám sát chi tiết", "Giao diện chuyên dụng cho giám sát viên quan sát dữ liệu, camera, AI realtime"),
            new("DEVICE_MANAGEMENT", "Quản lý thiết bị", "Quản lý tuyến, cụm, node, sensor, camera, thiết bị ngoại vi và điều khiển thiết bị"),
            new("ALERT_EVENT_MANAGEMENT", "Quản lý cảnh báo", "Xem, lọc, xác nhận, xử lý, đóng/mở lại cảnh báo và sự kiện"),
            new("DATA_HISTORY_REPORTING", "Báo cáo và phân tích xu hướng", "Tra cứu dữ liệu, xem lịch sử, thống kê, báo cáo và phân tích xu hướng"),
            new("SYSTEM_ADMINISTRATION", "Quản trị hệ thống", "Quản lý user, vai trò, phân quyền, cấu hình hệ thống và audit log")
        };

        private static readonly Dictionary<string, string[]> LegacyPermissionMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ADMIN"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT", "DATA_HISTORY_REPORTING", "SYSTEM_ADMINISTRATION" },
            ["OPERATOR"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT" },
            ["VIEWER"] = new[] { "DASHBOARD_MONITORING", "DATA_HISTORY_REPORTING" },
            ["users.manage"] = new[] { "SYSTEM_ADMINISTRATION" },
            ["roles.manage"] = new[] { "SYSTEM_ADMINISTRATION" },
            ["configuration.manage"] = new[] { "SYSTEM_ADMINISTRATION" },
            ["stations.view"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL" },
            ["cameras.view"] = new[] { "MONITORING_DETAIL" },
            ["alerts.handle"] = new[] { "ALERT_EVENT_MANAGEMENT", "MONITORING_DETAIL" },
            ["devices.manage"] = new[] { "DEVICE_MANAGEMENT", "MONITORING_DETAIL" },
            ["analytics.view"] = new[] { "DATA_HISTORY_REPORTING" },
            ["MONITOR_OVERVIEW"] = new[] { "DASHBOARD_MONITORING" },
            ["MONITOR_DETAIL"] = new[] { "MONITORING_DETAIL" },
            ["OPERATION_CONTROL"] = new[] { "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT" },
            ["REPORTING"] = new[] { "DATA_HISTORY_REPORTING" },
            ["SYSTEM_ADMIN"] = new[] { "SYSTEM_ADMINISTRATION" },
            ["StationManager"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT", "DATA_HISTORY_REPORTING", "SYSTEM_ADMINISTRATION" },
            ["Admin"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT", "DATA_HISTORY_REPORTING", "SYSTEM_ADMINISTRATION" },
            ["Administrator"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT", "DATA_HISTORY_REPORTING", "SYSTEM_ADMINISTRATION" },
            ["Staff"] = new[] { "DASHBOARD_MONITORING", "MONITORING_DETAIL", "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT" }
        };

        private static readonly Dictionary<string, string> LegacyFunctionGroupCodeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["monitoring_overview"] = "DASHBOARD_MONITORING",
            ["monitor_overview"] = "DASHBOARD_MONITORING",
            ["monitoring_detail"] = "MONITORING_DETAIL",
            ["monitor_detail"] = "MONITORING_DETAIL",
            ["reporting_analytics"] = "DATA_HISTORY_REPORTING",
            ["reporting"] = "DATA_HISTORY_REPORTING",
            ["system_administration"] = "SYSTEM_ADMINISTRATION",
            ["system_admin"] = "SYSTEM_ADMINISTRATION"
        };

        public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            await db.Database.EnsureCreatedAsync(ct);
            await TryMigrateFromLegacyPostgresAsync(db, configuration, ct);
            await EnsureUserSecurityColumnsAsync(db, ct);
            await EnsureAuditLogTableAsync(db, ct);

            var roles = await UpsertRolesAsync(db, ct);
            var functionGroups = await UpsertFunctionGroupsAsync(db, ct);
            await NormalizeRoleAssignmentsAsync(db, roles, ct);
            await NormalizeRoleFunctionGroupsAsync(db, roles, functionGroups, ct);
            await PruneObsoleteFunctionGroupsAsync(db, functionGroups, ct);
        }

        private static async Task EnsureAuditLogTableAsync(AuthDbContext db, CancellationToken ct)
        {
            if (db.Database.IsSqlServer())
            {
                const string sqlServerSql = """
                    IF OBJECT_ID(N'[AuditLogs]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [AuditLogs] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [ActorUserId] uniqueidentifier NULL,
                            [Action] nvarchar(max) NOT NULL,
                            [TargetType] nvarchar(max) NOT NULL,
                            [TargetId] nvarchar(max) NOT NULL,
                            [OldValueJson] nvarchar(max) NULL,
                            [NewValueJson] nvarchar(max) NULL,
                            [CreatedAt] datetimeoffset NOT NULL
                        );
                    END
                    """;

                await db.Database.ExecuteSqlRawAsync(sqlServerSql, ct);
                return;
            }

            const string sql = """
                CREATE TABLE IF NOT EXISTS "AuditLogs" (
                    "Id" uuid PRIMARY KEY,
                    "ActorUserId" uuid NULL,
                    "Action" character varying NOT NULL,
                    "TargetType" character varying NOT NULL,
                    "TargetId" character varying NOT NULL,
                    "OldValueJson" text NULL,
                    "NewValueJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL
                );
                """;

            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }

        private static async Task EnsureUserSecurityColumnsAsync(AuthDbContext db, CancellationToken ct)
        {
            if (db.Database.IsSqlServer())
            {
                const string sqlServerSql = """
                    IF COL_LENGTH(N'[Users]', N'FailedLoginAttempts') IS NULL
                    BEGIN
                        ALTER TABLE [Users]
                        ADD [FailedLoginAttempts] int NOT NULL CONSTRAINT [DF_Users_FailedLoginAttempts] DEFAULT 0;
                    END

                    IF COL_LENGTH(N'[Users]', N'LastFailedLoginAt') IS NULL
                    BEGIN
                        ALTER TABLE [Users]
                        ADD [LastFailedLoginAt] datetimeoffset NULL;
                    END

                    IF COL_LENGTH(N'[Users]', N'LockoutEndAt') IS NULL
                    BEGIN
                        ALTER TABLE [Users]
                        ADD [LockoutEndAt] datetimeoffset NULL;
                    END
                    """;

                await db.Database.ExecuteSqlRawAsync(sqlServerSql, ct);
                return;
            }

            const string sql = """
                ALTER TABLE "Users"
                ADD COLUMN IF NOT EXISTS "FailedLoginAttempts" integer NOT NULL DEFAULT 0;

                ALTER TABLE "Users"
                ADD COLUMN IF NOT EXISTS "LastFailedLoginAt" timestamp with time zone NULL;

                ALTER TABLE "Users"
                ADD COLUMN IF NOT EXISTS "LockoutEndAt" timestamp with time zone NULL;
                """;

            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }

        private static async Task TryMigrateFromLegacyPostgresAsync(
            AuthDbContext db,
            IConfiguration configuration,
            CancellationToken ct)
        {
            if (!db.Database.IsSqlServer())
            {
                return;
            }

            var hasAnyData =
                await db.Roles.AnyAsync(ct) ||
                await db.FunctionGroups.AnyAsync(ct) ||
                await db.Users.AnyAsync(ct) ||
                await db.RoleFunctionGroups.AnyAsync(ct) ||
                await db.AuditLogs.AnyAsync(ct) ||
                await db.RefreshTokens.AnyAsync(ct);

            if (hasAnyData)
            {
                return;
            }

            var legacyConnection =
                configuration.GetConnectionString("LegacyPostgresConnection")
                ?? configuration.GetConnectionString("PostgresConnection")
                ?? configuration.GetConnectionString("DefaultPostgresConnection");

            if (string.IsNullOrWhiteSpace(legacyConnection))
            {
                return;
            }

            await using var connection = new NpgsqlConnection(legacyConnection);
            await connection.OpenAsync(ct);

            var roles = await ReadRolesAsync(connection, ct);
            var functionGroups = await ReadFunctionGroupsAsync(connection, ct);
            var users = await ReadUsersAsync(connection, ct);
            var roleFunctionGroups = await ReadRoleFunctionGroupsAsync(connection, ct);
            var refreshTokens = await ReadRefreshTokensAsync(connection, ct);
            var auditLogs = await ReadAuditLogsAsync(connection, ct);

            if (roles.Count == 0 &&
                functionGroups.Count == 0 &&
                users.Count == 0 &&
                roleFunctionGroups.Count == 0 &&
                refreshTokens.Count == 0 &&
                auditLogs.Count == 0)
            {
                return;
            }

            if (roles.Count > 0)
            {
                db.Roles.AddRange(roles);
            }

            if (functionGroups.Count > 0)
            {
                db.FunctionGroups.AddRange(functionGroups);
            }

            if (users.Count > 0)
            {
                db.Users.AddRange(users);
            }

            if (roleFunctionGroups.Count > 0)
            {
                db.RoleFunctionGroups.AddRange(roleFunctionGroups);
            }

            if (refreshTokens.Count > 0)
            {
                db.RefreshTokens.AddRange(refreshTokens);
            }

            if (auditLogs.Count > 0)
            {
                db.AuditLogs.AddRange(auditLogs);
            }

            await db.SaveChangesAsync(ct);
        }

        private static async Task<List<Role>> ReadRolesAsync(NpgsqlConnection connection, CancellationToken ct)
        {
            const string sql = """
                SELECT "Id", "Code", "Name"
                FROM "Roles";
                """;

            var result = new List<Role>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new Role
                {
                    Id = reader.GetGuid(0),
                    Code = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
                });
            }

            return result;
        }

        private static async Task<List<FunctionGroup>> ReadFunctionGroupsAsync(NpgsqlConnection connection, CancellationToken ct)
        {
            const string sql = """
                SELECT "Id", "Code", "Name", "Description"
                FROM "FunctionGroups";
                """;

            var result = new List<FunctionGroup>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new FunctionGroup
                {
                    Id = reader.GetGuid(0),
                    Code = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }

            return result;
        }

        private static async Task<List<User>> ReadUsersAsync(NpgsqlConnection connection, CancellationToken ct)
        {
            const string sql = """
                SELECT "Id", "Username", "PasswordHash", "FullName", "RoleId", "IsActive", "LastLoginAt", "CreatedAt", "UpdatedAt"
                FROM "Users";
                """;

            var result = new List<User>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new User
                {
                    Id = reader.GetGuid(0),
                    Username = reader.IsDBNull(1) ? null : reader.GetString(1),
                    PasswordHash = reader.IsDBNull(2) ? null : reader.GetString(2),
                    FullName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RoleId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                    LastLoginAt = ReadNullableDateTimeOffset(reader, 6),
                    CreatedAt = ReadDateTimeOffset(reader, 7),
                    UpdatedAt = ReadDateTimeOffset(reader, 8)
                });
            }

            return result;
        }

        private static async Task<List<RoleFunctionGroup>> ReadRoleFunctionGroupsAsync(NpgsqlConnection connection, CancellationToken ct)
        {
            const string sql = """
                SELECT "RoleId", "FunctionGroupId"
                FROM "RoleFunctionGroups";
                """;

            var result = new List<RoleFunctionGroup>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new RoleFunctionGroup
                {
                    RoleId = reader.GetGuid(0),
                    FunctionGroupId = reader.GetGuid(1)
                });
            }

            return result;
        }

        private static async Task<List<RefreshToken>> ReadRefreshTokensAsync(NpgsqlConnection connection, CancellationToken ct)
        {
            const string sql = """
                SELECT "Id", "UserId", "TokenHash", "CreatedAt", "ExpiresAt", "Revoked", "ReplacedByToken"
                FROM "RefreshTokens";
                """;

            var result = new List<RefreshToken>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new RefreshToken
                {
                    Id = reader.GetGuid(0),
                    UserId = reader.GetGuid(1),
                    TokenHash = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    CreatedAt = ReadDateTimeOffset(reader, 3),
                    ExpiresAt = ReadDateTimeOffset(reader, 4),
                    Revoked = !reader.IsDBNull(5) && reader.GetBoolean(5),
                    ReplacedByToken = reader.IsDBNull(6) ? null : reader.GetGuid(6)
                });
            }

            return result;
        }

        private static async Task<List<AuditLog>> ReadAuditLogsAsync(NpgsqlConnection connection, CancellationToken ct)
        {
            const string sql = """
                SELECT "Id", "ActorUserId", "Action", "TargetType", "TargetId", "OldValueJson", "NewValueJson", "CreatedAt"
                FROM "AuditLogs";
                """;

            var result = new List<AuditLog>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new AuditLog
                {
                    Id = reader.GetGuid(0),
                    ActorUserId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    Action = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    TargetType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    TargetId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    OldValueJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewValueJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                    CreatedAt = ReadDateTimeOffset(reader, 7)
                });
            }

            return result;
        }

        private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
        {
            var value = reader.GetValue(ordinal);
            return value switch
            {
                DateTimeOffset dateTimeOffset => dateTimeOffset,
                DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
                _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
            };
        }

        private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : ReadDateTimeOffset(reader, ordinal);
        }

        private static async Task<Dictionary<string, Role>> UpsertRolesAsync(AuthDbContext db, CancellationToken ct)
        {
            var existing = await db.Roles.ToListAsync(ct);
            var byCode = BuildLookup(existing, role => role.Code);
            var byName = BuildLookup(existing, role => role.Name);

            foreach (var standard in StandardRoles)
            {
                if (!byCode.TryGetValue(standard.Code, out var role))
                {
                    role = existing.FirstOrDefault(candidate =>
                        string.Equals(NormalizeKey(MapLegacyRoleToStandardCode(candidate)), NormalizeKey(standard.Code), StringComparison.OrdinalIgnoreCase));

                    if (role == null && byName.TryGetValue(NormalizeKey(standard.Name), out var roleByName))
                    {
                        role = roleByName;
                    }

                    if (role == null)
                    {
                        role = new Role
                        {
                            Code = standard.Code,
                            Name = standard.Name
                        };
                        db.Roles.Add(role);
                        existing.Add(role);
                    }
                }

                role.Code = standard.Code;
                if (string.IsNullOrWhiteSpace(role.Name))
                {
                    role.Name = standard.Name;
                }
                byCode[NormalizeKey(standard.Code)] = role;
                byName[NormalizeKey(standard.Name)] = role;
            }

            await db.SaveChangesAsync(ct);
            return byCode;
        }

        private static async Task<Dictionary<string, FunctionGroup>> UpsertFunctionGroupsAsync(AuthDbContext db, CancellationToken ct)
        {
            var existing = await db.FunctionGroups.ToListAsync(ct);
            var byCode = BuildLookup(existing, group => group.Code);
            var byName = BuildLookup(existing, group => group.Name);

            foreach (var standard in StandardFunctionGroups)
            {
                if (!byCode.TryGetValue(standard.Code, out var group))
                {
                    group = existing.FirstOrDefault(candidate =>
                    {
                        var candidateCode = NormalizeKey(candidate.Code);
                        return LegacyFunctionGroupCodeMap.TryGetValue(candidateCode, out var mappedCode) &&
                               string.Equals(NormalizeKey(mappedCode), NormalizeKey(standard.Code), StringComparison.OrdinalIgnoreCase);
                    });

                    if (group == null && byName.TryGetValue(NormalizeKey(standard.Name), out var groupByName))
                    {
                        group = groupByName;
                    }

                    if (group == null)
                    {
                        group = new FunctionGroup
                        {
                            Code = standard.Code,
                            Name = standard.Name,
                            Description = standard.Description
                        };
                        db.FunctionGroups.Add(group);
                        existing.Add(group);
                    }
                }

                group.Code = standard.Code;
                group.Name = standard.Name;
                group.Description = standard.Description;
                byCode[NormalizeKey(standard.Code)] = group;
                byName[NormalizeKey(standard.Name)] = group;
            }

            await db.SaveChangesAsync(ct);
            return byCode;
        }

        private static async Task PruneObsoleteFunctionGroupsAsync(
            AuthDbContext db,
            IReadOnlyDictionary<string, FunctionGroup> standardFunctionGroups,
            CancellationToken ct)
        {
            var standardIds = standardFunctionGroups.Values
                .Select(group => group.Id)
                .ToHashSet();

            var obsoleteGroups = (await db.FunctionGroups
                    .Include(group => group.RoleFunctionGroups)
                    .ToListAsync(ct))
                .Where(group => !standardIds.Contains(group.Id))
                .ToList();

            if (obsoleteGroups.Count == 0)
            {
                return;
            }

            var obsoleteLinks = obsoleteGroups
                .SelectMany(group => group.RoleFunctionGroups)
                .ToList();

            if (obsoleteLinks.Count > 0)
            {
                db.RoleFunctionGroups.RemoveRange(obsoleteLinks);
            }

            db.FunctionGroups.RemoveRange(obsoleteGroups);
            await db.SaveChangesAsync(ct);
        }

        private static async Task NormalizeRoleAssignmentsAsync(
            AuthDbContext db,
            IReadOnlyDictionary<string, Role> standardRoles,
            CancellationToken ct)
        {
            var legacyRoles = await db.Roles.ToListAsync(ct);
            var users = await db.Users.ToListAsync(ct);

            foreach (var user in users)
            {
                var currentRole = legacyRoles.FirstOrDefault(role => role.Id == user.RoleId);
                var targetCode = MapLegacyRoleToStandardCode(currentRole);
                if (targetCode == null)
                {
                    if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
                    {
                        targetCode = "ADMIN";
                    }
                    else if (string.Equals(user.Username, "operator", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(user.Username, "staff", StringComparison.OrdinalIgnoreCase))
                    {
                        targetCode = "OPERATOR";
                    }
                }

                if (targetCode != null && standardRoles.TryGetValue(targetCode, out var standardRole))
                {
                    user.RoleId = standardRole.Id;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        private static async Task NormalizeRoleFunctionGroupsAsync(
            AuthDbContext db,
            IReadOnlyDictionary<string, Role> standardRoles,
            IReadOnlyDictionary<string, FunctionGroup> standardFunctionGroups,
            CancellationToken ct)
        {
            var legacyRoles = await db.Roles
                .Include(role => role.RoleFunctionGroups)
                    .ThenInclude(link => link.FunctionGroup)
                .ToListAsync(ct);

            var newLinks = new HashSet<(Guid RoleId, Guid FunctionGroupId)>();

            foreach (var role in legacyRoles)
            {
                var mappedCode = MapLegacyRoleToStandardCode(role);
                var targetRole = standardRoles.TryGetValue(mappedCode ?? string.Empty, out var standardRole)
                    ? standardRole
                    : role;

                var targetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var existingStandardCodes = role.RoleFunctionGroups
                    .Select(link => link.FunctionGroup?.Code)
                    .Where(code => !string.IsNullOrWhiteSpace(code) && standardFunctionGroups.ContainsKey(code!))
                    .Select(code => code!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (existingStandardCodes.Count > 0)
                {
                    foreach (var code in existingStandardCodes)
                    {
                        targetCodes.Add(code);
                    }
                }
                else
                {
                    if (LegacyPermissionMap.TryGetValue(role.Code, out var mappedByCode))
                    {
                        foreach (var code in mappedByCode)
                        {
                            targetCodes.Add(code);
                        }
                    }

                    if (LegacyPermissionMap.TryGetValue(role.Name, out var mappedByName))
                    {
                        foreach (var code in mappedByName)
                        {
                            targetCodes.Add(code);
                        }
                    }
                }

                foreach (var roleFunctionGroup in role.RoleFunctionGroups)
                {
                    var legacyCode = roleFunctionGroup.FunctionGroup?.Code;
                    if (string.IsNullOrWhiteSpace(legacyCode))
                    {
                        continue;
                    }

                    if (standardFunctionGroups.ContainsKey(legacyCode))
                    {
                        targetCodes.Add(legacyCode);
                    }
                    else if (LegacyPermissionMap.TryGetValue(legacyCode, out var mapped))
                    {
                        foreach (var code in mapped)
                        {
                            targetCodes.Add(code);
                        }
                    }
                }

                foreach (var targetCode in targetCodes)
                {
                    if (standardFunctionGroups.TryGetValue(targetCode, out var functionGroup))
                    {
                        newLinks.Add((targetRole.Id, functionGroup.Id));
                    }
                }
            }

            var existingLinks = await db.RoleFunctionGroups.ToListAsync(ct);
            var obsoleteLinks = existingLinks
                .Where(link => !newLinks.Contains((link.RoleId, link.FunctionGroupId)))
                .ToList();

            if (obsoleteLinks.Count > 0)
            {
                db.RoleFunctionGroups.RemoveRange(obsoleteLinks);
            }

            foreach (var link in newLinks)
            {
                if (existingLinks.Any(existing => existing.RoleId == link.RoleId && existing.FunctionGroupId == link.FunctionGroupId))
                {
                    continue;
                }

                db.RoleFunctionGroups.Add(new RoleFunctionGroup
                {
                    RoleId = link.RoleId,
                    FunctionGroupId = link.FunctionGroupId
                });
            }

            await db.SaveChangesAsync(ct);
        }

        private static string? MapLegacyRoleToStandardCode(Role? role)
        {
            var token = role?.Code ?? role?.Name;
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            if (string.Equals(token, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "Administrator", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "StationManager", StringComparison.OrdinalIgnoreCase))
            {
                return "ADMIN";
            }

            if (string.Equals(token, "OPERATOR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "Operator", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "Staff", StringComparison.OrdinalIgnoreCase))
            {
                return "OPERATOR";
            }

            if (string.Equals(token, "VIEWER", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "Viewer", StringComparison.OrdinalIgnoreCase))
            {
                return "VIEWER";
            }

            return role?.Code;
        }

        private static Dictionary<string, TEntity> BuildLookup<TEntity>(
            IEnumerable<TEntity> items,
            Func<TEntity, string?> keySelector)
            where TEntity : class
        {
            var result = new Dictionary<string, TEntity>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var key = NormalizeKey(keySelector(item));
                if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
                {
                    continue;
                }

                result[key] = item;
            }

            return result;
        }

        private static string NormalizeKey(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
