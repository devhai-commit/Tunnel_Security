using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Station.Services
{
    public sealed class CurrentUserSession
    {
        private static readonly Lazy<CurrentUserSession> _instance = new(() => new CurrentUserSession());

        public static CurrentUserSession Instance => _instance.Value;

        public string? Username { get; private set; }
        public string? FullName { get; private set; }
        public string? RoleName { get; private set; }
        public IReadOnlyList<string> Permissions { get; private set; } = Array.Empty<string>();
        public string? AccessToken { get; private set; }
        public DateTimeOffset? ExpiresAt { get; private set; }

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Username);

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

        public bool HasAdminAccess =>
            HasPermission("SYSTEM_ADMINISTRATION") ||
            string.Equals(RoleName, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(RoleName, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Username, "admin", StringComparison.OrdinalIgnoreCase);

        private CurrentUserSession()
        {
        }

        public void SetSession(string accessToken, DateTimeOffset? expiresAt = null)
        {
            AccessToken = accessToken;
            ExpiresAt = expiresAt;

            try
            {
                var payload = ParseJwtPayload(accessToken);
                Username = GetString(payload, "unique_name") ?? GetString(payload, "username") ?? GetString(payload, "sub");
                FullName = GetString(payload, "full_name") ?? Username;
                RoleName = GetString(payload, "role")
                    ?? GetString(payload, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                    ?? "Người dùng";
                Permissions = GetStrings(payload, "permission");
            }
            catch
            {
                Username = null;
                FullName = null;
                RoleName = null;
                Permissions = Array.Empty<string>();
            }
        }

        public void Clear()
        {
            Username = null;
            FullName = null;
            RoleName = null;
            Permissions = Array.Empty<string>();
            AccessToken = null;
            ExpiresAt = null;
        }

        public void UpdateProfile(string username, string fullName)
        {
            Username = string.IsNullOrWhiteSpace(username) ? Username : username.Trim();
            FullName = string.IsNullOrWhiteSpace(fullName) ? Username : fullName.Trim();
        }

        public bool HasPermission(string permissionCode)
        {
            if (string.IsNullOrWhiteSpace(permissionCode))
            {
                return false;
            }

            return ExpandPermissionAliases(permissionCode)
                .Any(candidate => Permissions.Contains(candidate, StringComparer.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> ExpandPermissionAliases(string permissionCode)
        {
            yield return permissionCode;

            if (PermissionAliases.TryGetValue(permissionCode, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    yield return alias;
                }
            }
        }

        private static JsonElement ParseJwtPayload(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("Invalid JWT token.");
            }

            var payloadBytes = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payloadBytes);
            return document.RootElement.Clone();
        }

        private static byte[] DecodeBase64Url(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }

            return Convert.FromBase64String(base64);
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Array when property.GetArrayLength() > 0 => property[0].GetString(),
                _ => null
            };
        }

        private static IReadOnlyList<string> GetStrings(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return Array.Empty<string>();
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToArray();
            }

            if (property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()))
            {
                return new[] { property.GetString()! };
            }

            return Array.Empty<string>();
        }
    }
}
