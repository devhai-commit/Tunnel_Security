using System;
using System.Collections.Generic;

namespace Station.DTOs
{
    public class LoginResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public class UserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public bool IsActive { get; set; }

        public string Role => Roles.Count == 0 ? "No role" : string.Join(", ", Roles);
        public string PermissionsDisplay => Permissions.Count == 0 ? "No permission" : string.Join(", ", Permissions);
    }

    public class RoleDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
    }

    public class PermissionDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class AuditLogDto
    {
        public Guid Id { get; set; }
        public Guid? ActorUserId { get; set; }
        public string ActorDisplayName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string? OldValueJson { get; set; }
        public string? NewValueJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
