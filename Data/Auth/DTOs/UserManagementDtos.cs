using System;
using System.Collections.Generic;

namespace TunnelSecurity.Data.Auth.DTOs
{
    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UpdateProfileRequest
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class UserListItemResponse
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public bool IsActive { get; set; }
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

    public class SaveUserAccessRequest
    {
        public Guid UserId { get; set; }
        public List<Guid> RoleIds { get; set; } = new();
        public List<Guid> GrantedPermissionIds { get; set; } = new();
        public List<Guid> DeniedPermissionIds { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }

    public class SaveUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public Guid? RoleId { get; set; }
        public string? NewPassword { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SaveRoleRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> PermissionCodes { get; set; } = new();
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
