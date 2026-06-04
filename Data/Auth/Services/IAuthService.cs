using TunnelSecurity.Auth.DTOs;
using TunnelSecurity.Data.Auth.DTOs;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace TunnelSecurity.Data.Auth.Services
{
    public interface IAuthService
    {
        Task RegisterAsync(RegisterRequest request, CancellationToken ct = default);
        Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
        Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
        Task RevokeAsync(string refreshToken, CancellationToken ct = default);
        Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
        Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
        Task<List<UserListItemResponse>> GetUsersAsync(CancellationToken ct = default);
        Task UpdateUserAsync(Guid userId, SaveUserRequest request, CancellationToken ct = default);
        Task<List<RoleDto>> GetRolesAsync(CancellationToken ct = default);
        Task<List<PermissionDto>> GetPermissionsAsync(CancellationToken ct = default);
        Task<List<AuditLogDto>> GetAuditLogsAsync(CancellationToken ct = default);
        Task<RoleDto> CreateRoleAsync(SaveRoleRequest request, CancellationToken ct = default);
        Task<RoleDto> UpdateRoleAsync(Guid roleId, SaveRoleRequest request, CancellationToken ct = default);
        Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default);
        Task SaveUserAccessAsync(SaveUserAccessRequest request, CancellationToken ct = default);
        Task DeleteUserAsync(Guid userId, CancellationToken ct = default);
    }
}
