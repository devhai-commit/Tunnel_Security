using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Station.Services
{
    public static class AuthSession
    {
        public static string? AccessToken { get; private set; }
        public static string? RefreshToken { get; private set; }
        public static DateTimeOffset ExpiresAt { get; private set; }
        public static string Username { get; private set; } = string.Empty;
        public static IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
        public static IReadOnlyList<string> Permissions { get; private set; } = Array.Empty<string>();

        public static bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

        private static readonly IReadOnlyDictionary<string, string[]> PermissionAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["SYSTEM_ADMINISTRATION"] = new[] { "SYSTEM_ADMIN" },
                ["SYSTEM_ADMIN"] = new[] { "SYSTEM_ADMINISTRATION" },
                ["MONITORING_DETAIL"] = new[] { "MONITOR_DETAIL" },
                ["MONITOR_DETAIL"] = new[] { "MONITORING_DETAIL" },
                ["DATA_HISTORY_REPORTING"] = new[] { "REPORTING" },
                ["REPORTING"] = new[] { "DATA_HISTORY_REPORTING" },
                ["DEVICE_MANAGEMENT"] = new[] { "OPERATION_CONTROL" },
                ["ALERT_EVENT_MANAGEMENT"] = new[] { "OPERATION_CONTROL" },
                ["OPERATION_CONTROL"] = new[] { "DEVICE_MANAGEMENT", "ALERT_EVENT_MANAGEMENT" },
                ["DASHBOARD_MONITORING"] = new[] { "MONITOR_OVERVIEW" },
                ["MONITOR_OVERVIEW"] = new[] { "DASHBOARD_MONITORING" }
            };

        public static void SignIn(string accessToken, string? refreshToken, DateTimeOffset expiresAt)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;

            var claims = ReadJwtPayload(accessToken);

            Username = claims.TryGetValue("unique_name", out var username)
                ? username[0]
                : string.Empty;

            Roles = claims.TryGetValue("role", out var roles)
                ? roles
                : claims.TryGetValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", out var microsoftRoles)
                    ? microsoftRoles
                    : Array.Empty<string>();

            Permissions = claims.TryGetValue("permission", out var permissions)
                ? permissions
                : Array.Empty<string>();
        }

        public static void SignOut()
        {
            AccessToken = null;
            RefreshToken = null;
            ExpiresAt = default;
            Username = string.Empty;
            Roles = Array.Empty<string>();
            Permissions = Array.Empty<string>();
        }

        public static void UpdateProfile(string username)
        {
            Username = username?.Trim() ?? string.Empty;
        }

        public static bool HasPermission(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                return false;

            if (ExpandPermissionAliases(permission)
                .Any(candidate => Permissions.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
                return true;

            return Roles.Any(role =>
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "StationManager", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasAnyPermission(params string[] permissions)
        {
            if (permissions == null || permissions.Length == 0)
                return false;

            return permissions.Any(HasPermission);
        }

        private static IEnumerable<string> ExpandPermissionAliases(string permission)
        {
            yield return permission;

            if (PermissionAliases.TryGetValue(permission, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    yield return alias;
                }
            }
        }

        private static Dictionary<string, string[]> ReadJwtPayload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return new Dictionary<string, string[]>();

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    var values = new List<string>();
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            values.Add(item.GetString() ?? string.Empty);
                    }

                    result[property.Name] = values.ToArray();
                }
                else if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = new[] { property.Value.GetString() ?? string.Empty };
                }
            }

            return result;
        }
    }
}
