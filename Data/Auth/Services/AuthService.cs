using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TunnelSecurity.Auth;
using TunnelSecurity.Auth.DTOs;
using TunnelSecurity.Data.Auth.DTOs;
using TunnelSecurity.Data.Auth.Models;

namespace TunnelSecurity.Data.Auth.Services
{
    public class AuthService : IAuthService
    {
        private static readonly string[] AdminLikeRoles =
        {
            "Admin",
            "StationManager",
            "Administrator"
        };

        private readonly AuthDbContext _db;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly JwtSettings _jwt;
        private readonly LoginSecuritySettings _loginSecurity;

        public AuthService(
            AuthDbContext db,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions,
            IOptions<LoginSecuritySettings> loginSecurityOptions)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _jwt = jwtOptions?.Value ?? throw new ArgumentNullException(nameof(jwtOptions));
            _loginSecurity = loginSecurityOptions?.Value ?? throw new ArgumentNullException(nameof(loginSecurityOptions));
        }

        public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var normalizedUsername = request.Username?.Trim() ?? string.Empty;
            var normalizedFullName = string.IsNullOrWhiteSpace(request.FullName)
                ? normalizedUsername
                : request.FullName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
                throw new InvalidOperationException("Tên đăng nhập không được để trống.");

            if (string.IsNullOrWhiteSpace(request.Password))
                throw new InvalidOperationException("Mật khẩu không được để trống.");

            if (await _db.Users.AnyAsync(u => u.Username == normalizedUsername, ct))
                throw new InvalidOperationException("Tên đăng nhập đã tồn tại.");

            var user = new User
            {
                Username = normalizedUsername,
                FullName = normalizedFullName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: user.Id,
                action: "USER_CREATED",
                targetType: "User",
                targetId: user.Id.ToString(),
                oldValue: null,
                newValue: new
                {
                    user.Username,
                    user.FullName,
                    user.RoleId,
                    user.IsActive
                },
                ct);
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var now = DateTimeOffset.UtcNow;
            var username = !string.IsNullOrWhiteSpace(request.Username)
                ? request.Username.Trim()
                : request.UsernameOrEmail?.Trim() ?? string.Empty;
            var user = await _db.Users
                .Include(u => u.Role)
                    .ThenInclude(r => r!.RoleFunctionGroups)
                        .ThenInclude(rfg => rfg.FunctionGroup)
                .FirstOrDefaultAsync(u => u.Username == username, ct);

            if (user == null)
                throw new UnauthorizedAccessException("Sai username hoặc mật khẩu.");

            if (IsLockoutActive(user, now))
                throw new UnauthorizedAccessException(BuildActiveLockoutMessage(user.LockoutEndAt!.Value, now));

            if (user.LockoutEndAt.HasValue || user.FailedLoginAttempts > 0 || user.LastFailedLoginAt.HasValue)
            {
                ResetFailedLoginStateIfWindowExpired(user, now);
            }

            if (!user.IsActive)
                throw new UnauthorizedAccessException("Tài khoản đã bị khoá.");

            var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? string.Empty, request.Password);
            if (verify == PasswordVerificationResult.Failed)
            {
                await RegisterFailedLoginAttemptAsync(user, now, ct);
                throw new UnauthorizedAccessException("Sai username hoặc mật khẩu.");
            }

            if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            }

            ResetFailedLoginState(user);
            user.LastLoginAt = now;
            user.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            return await CreateTokensForUserAsync(user, ct);
        }

        public async Task<LoginResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentNullException(nameof(refreshToken));

            var tokenHash = HashToken(refreshToken);
            var existing = await _db.RefreshTokens
                .Include(t => t.User)
                    .ThenInclude(u => u!.Role)
                        .ThenInclude(r => r!.RoleFunctionGroups)
                            .ThenInclude(rfg => rfg.FunctionGroup)
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

            if (existing == null || existing.Revoked || existing.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new UnauthorizedAccessException("Invalid refresh token.");

            existing.Revoked = true;

            var newRefreshValue = GenerateSecureToken();
            var newRefresh = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = existing.UserId,
                TokenHash = HashToken(newRefreshValue),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays),
                Revoked = false
            };

            _db.RefreshTokens.Add(newRefresh);
            existing.ReplacedByToken = newRefresh.Id;
            await _db.SaveChangesAsync(ct);

            var response = await CreateAccessTokenOnlyAsync(existing.User!, ct);
            response.RefreshToken = newRefreshValue;
            return response;
        }

        public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return;

            var tokenHash = HashToken(refreshToken);
            var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
            if (existing == null)
                return;

            existing.Revoked = true;
            await _db.SaveChangesAsync(ct);
        }

        public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                throw new InvalidOperationException("Mật khẩu hiện tại không được để trống.");
            if (string.IsNullOrWhiteSpace(request.NewPassword))
                throw new InvalidOperationException("Mật khẩu mới không được để trống.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null)
                throw new InvalidOperationException("Không tìm thấy tài khoản.");

            var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? string.Empty, request.CurrentPassword);
            if (verify == PasswordVerificationResult.Failed)
                throw new UnauthorizedAccessException("Mật khẩu hiện tại không chính xác.");

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            ResetFailedLoginState(user);
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: userId,
                action: "PASSWORD_CHANGED",
                targetType: "User",
                targetId: user.Id.ToString(),
                oldValue: null,
                newValue: new { user.Id, user.Username },
                ct);
        }

        public async Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var normalizedUsername = request.Username?.Trim() ?? string.Empty;
            var normalizedFullName = string.IsNullOrWhiteSpace(request.FullName)
                ? normalizedUsername
                : request.FullName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
                throw new InvalidOperationException("Tên người dùng không được để trống.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null)
                throw new InvalidOperationException("Không tìm thấy tài khoản.");

            var oldValue = new
            {
                user.Username,
                user.FullName
            };

            var duplicated = await _db.Users.AnyAsync(u =>
                u.Id != userId &&
                u.Username != null &&
                u.Username == normalizedUsername, ct);

            if (duplicated)
                throw new InvalidOperationException("Tên người dùng đã tồn tại.");

            user.Username = normalizedUsername;
            user.FullName = normalizedFullName;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: userId,
                action: "PROFILE_UPDATED",
                targetType: "User",
                targetId: user.Id.ToString(),
                oldValue: oldValue,
                newValue: new
                {
                    user.Username,
                    user.FullName
                },
                ct);
        }

        public async Task<List<UserListItemResponse>> GetUsersAsync(CancellationToken ct = default)
        {
            var users = await _db.Users
                .Include(u => u.Role)
                    .ThenInclude(r => r!.RoleFunctionGroups)
                        .ThenInclude(rfg => rfg.FunctionGroup)
                .OrderBy(u => u.CreatedAt)
                .ToListAsync(ct);

            var result = new List<UserListItemResponse>();
            foreach (var user in users)
            {
                var permissions = await ResolvePermissionCodesAsync(user, ct);
                result.Add(new UserListItemResponse
                {
                    Id = user.Id,
                    Username = user.Username ?? string.Empty,
                    FullName = user.FullName ?? user.Username ?? string.Empty,
                    Roles = string.IsNullOrWhiteSpace(user.Role?.Name)
                        ? new List<string>()
                        : new List<string> { user.Role!.Name },
                    Permissions = permissions,
                    IsActive = user.IsActive
                });
            }

            return result;
        }

        public async Task UpdateUserAsync(Guid userId, SaveUserRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var normalizedUsername = request.Username?.Trim() ?? string.Empty;
            var normalizedFullName = string.IsNullOrWhiteSpace(request.FullName)
                ? normalizedUsername
                : request.FullName.Trim();

            if (string.IsNullOrWhiteSpace(normalizedUsername))
                throw new InvalidOperationException("Tên đăng nhập không được để trống.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null)
                throw new InvalidOperationException("Không tìm thấy tài khoản.");

            if (await _db.Users.AnyAsync(u => u.Id != userId && u.Username == normalizedUsername, ct))
                throw new InvalidOperationException("Tên đăng nhập đã tồn tại.");

            var oldValue = new
            {
                user.Username,
                user.FullName,
                user.RoleId,
                user.IsActive,
                HasPassword = !string.IsNullOrWhiteSpace(user.PasswordHash)
            };

            user.Username = normalizedUsername;
            user.FullName = normalizedFullName;
            user.RoleId = request.RoleId == Guid.Empty ? null : request.RoleId;
            user.IsActive = request.IsActive;

            var hasNewPassword = !string.IsNullOrWhiteSpace(request.NewPassword);
            if (hasNewPassword)
            {
                if (request.NewPassword!.Trim().Length < 6)
                    throw new InvalidOperationException("Mật khẩu mới phải có ít nhất 6 ký tự.");

                user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword.Trim());
            }

            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: null,
                action: "USER_UPDATED",
                targetType: "User",
                targetId: user.Id.ToString(),
                oldValue: oldValue,
                newValue: new
                {
                    user.Username,
                    user.FullName,
                    user.RoleId,
                    user.IsActive,
                    PasswordUpdated = hasNewPassword
                },
                ct);
        }

        public async Task<List<RoleDto>> GetRolesAsync(CancellationToken ct = default)
        {
            var roles = await _db.Roles
                .Include(r => r.RoleFunctionGroups)
                    .ThenInclude(rfg => rfg.FunctionGroup)
                .OrderBy(r => r.Name)
                .ToListAsync(ct);

            return roles.Select(role => new RoleDto
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name,
                Permissions = role.RoleFunctionGroups
                    .Where(rfg => rfg.FunctionGroup != null && !string.IsNullOrWhiteSpace(rfg.FunctionGroup.Code))
                    .Select(rfg => rfg.FunctionGroup!.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }).ToList();
        }

        public async Task<List<PermissionDto>> GetPermissionsAsync(CancellationToken ct = default)
        {
            return await _db.FunctionGroups
                .OrderBy(fg => fg.Name)
                .Select(fg => new PermissionDto
                {
                    Id = fg.Id,
                    Code = fg.Code,
                    Name = fg.Name,
                    Category = "Nhóm chức năng",
                    Description = fg.Description
                })
                .ToListAsync(ct);
        }

        public async Task<List<AuditLogDto>> GetAuditLogsAsync(CancellationToken ct = default)
        {
            return await _db.AuditLogs
                .AsNoTracking()
                .Include(log => log.ActorUser)
                .OrderByDescending(log => log.CreatedAt)
                .Take(300)
                .Select(log => new AuditLogDto
                {
                    Id = log.Id,
                    ActorUserId = log.ActorUserId,
                    ActorDisplayName = log.ActorUser != null && !string.IsNullOrWhiteSpace(log.ActorUser.FullName)
                        ? log.ActorUser.FullName
                        : log.ActorUser != null && !string.IsNullOrWhiteSpace(log.ActorUser.Username)
                            ? log.ActorUser.Username!
                            : "Hệ thống",
                    Action = log.Action,
                    TargetType = log.TargetType,
                    TargetId = log.TargetId,
                    OldValueJson = log.OldValueJson,
                    NewValueJson = log.NewValueJson,
                    CreatedAt = log.CreatedAt
                })
                .ToListAsync(ct);
        }

        public async Task<RoleDto> CreateRoleAsync(SaveRoleRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var code = NormalizeRoleCode(request.Code);
            var name = request.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Mã vai trò không được để trống.");

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Tên vai trò không được để trống.");

            if (await _db.Roles.AnyAsync(role => role.Code == code, ct))
                throw new InvalidOperationException("Mã vai trò đã tồn tại.");

            if (await _db.Roles.AnyAsync(role => role.Name == name, ct))
                throw new InvalidOperationException("Tên vai trò đã tồn tại.");

            var role = new Role
            {
                Code = code,
                Name = name
            };

            _db.Roles.Add(role);
            await _db.SaveChangesAsync(ct);

            await ReplaceRoleFunctionGroupsAsync(role.Id, request.PermissionCodes, ct);
            await WriteAuditLogAsync(
                actorUserId: null,
                action: "ROLE_CREATED",
                targetType: "Role",
                targetId: role.Id.ToString(),
                oldValue: null,
                newValue: new
                {
                    role.Code,
                    role.Name,
                    PermissionCodes = request.PermissionCodes
                },
                ct);
            return await BuildRoleDtoAsync(role.Id, ct);
        }

        public async Task<RoleDto> UpdateRoleAsync(Guid roleId, SaveRoleRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
            if (role == null)
                throw new InvalidOperationException("Không tìm thấy vai trò.");

            var oldValue = new
            {
                role.Code,
                role.Name,
                PermissionCodes = await _db.RoleFunctionGroups
                    .Where(link => link.RoleId == roleId)
                    .Include(link => link.FunctionGroup)
                    .Select(link => link.FunctionGroup!.Code)
                    .ToListAsync(ct)
            };

            var code = NormalizeRoleCode(request.Code);
            var name = request.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Mã vai trò không được để trống.");

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Tên vai trò không được để trống.");

            if (await _db.Roles.AnyAsync(r => r.Id != roleId && r.Code == code, ct))
                throw new InvalidOperationException("Mã vai trò đã tồn tại.");

            if (await _db.Roles.AnyAsync(r => r.Id != roleId && r.Name == name, ct))
                throw new InvalidOperationException("Tên vai trò đã tồn tại.");

            role.Code = code;
            role.Name = name;
            await _db.SaveChangesAsync(ct);

            await ReplaceRoleFunctionGroupsAsync(role.Id, request.PermissionCodes, ct);
            await WriteAuditLogAsync(
                actorUserId: null,
                action: "ROLE_UPDATED",
                targetType: "Role",
                targetId: role.Id.ToString(),
                oldValue: oldValue,
                newValue: new
                {
                    role.Code,
                    role.Name,
                    PermissionCodes = request.PermissionCodes
                },
                ct);
            return await BuildRoleDtoAsync(role.Id, ct);
        }

        public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
        {
            var role = await _db.Roles
                .Include(r => r.Users)
                .Include(r => r.RoleFunctionGroups)
                .FirstOrDefaultAsync(r => r.Id == roleId, ct);

            if (role == null)
                return;

            var oldValue = new
            {
                role.Code,
                role.Name,
                Users = role.Users.Select(user => user.Id).ToList(),
                PermissionCodes = role.RoleFunctionGroups
                    .Where(link => link.FunctionGroup != null)
                    .Select(link => link.FunctionGroup!.Code)
                    .ToList()
            };

            foreach (var user in role.Users)
            {
                user.RoleId = null;
                user.UpdatedAt = DateTimeOffset.UtcNow;
            }

            _db.RoleFunctionGroups.RemoveRange(role.RoleFunctionGroups);
            _db.Roles.Remove(role);
            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: null,
                action: "ROLE_DELETED",
                targetType: "Role",
                targetId: roleId.ToString(),
                oldValue: oldValue,
                newValue: null,
                ct);
        }

        public async Task SaveUserAccessAsync(SaveUserAccessRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
            if (user == null)
                throw new InvalidOperationException("Không tìm thấy tài khoản.");

            var oldValue = new
            {
                user.RoleId,
                user.IsActive
            };

            user.RoleId = request.RoleIds
                .Distinct()
                .FirstOrDefault();

            if (user.RoleId == Guid.Empty)
            {
                user.RoleId = null;
            }

            user.IsActive = request.IsActive;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: null,
                action: "USER_ACCESS_UPDATED",
                targetType: "User",
                targetId: user.Id.ToString(),
                oldValue: oldValue,
                newValue: new
                {
                    user.RoleId,
                    user.IsActive
                },
                ct);
        }

        public async Task DeleteUserAsync(Guid userId, CancellationToken ct = default)
        {
            var user = await _db.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user == null)
                return;

            var oldValue = new
            {
                user.Username,
                user.FullName,
                user.RoleId,
                user.IsActive
            };

            _db.RefreshTokens.RemoveRange(user.RefreshTokens);
            _db.Users.Remove(user);
            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(
                actorUserId: null,
                action: "USER_DELETED",
                targetType: "User",
                targetId: userId.ToString(),
                oldValue: oldValue,
                newValue: null,
                ct);
        }

        private async Task<LoginResponse> CreateTokensForUserAsync(User user, CancellationToken ct = default)
        {
            var access = await CreateAccessTokenOnlyAsync(user, ct);
            var refreshValue = GenerateSecureToken();

            var refresh = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = HashToken(refreshValue),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays),
                Revoked = false
            };

            _db.RefreshTokens.Add(refresh);
            await _db.SaveChangesAsync(ct);

            access.RefreshToken = refreshValue;
            return access;
        }

        private async Task<LoginResponse> CreateAccessTokenOnlyAsync(User user, CancellationToken ct = default)
        {
            if (user.Role == null && user.RoleId.HasValue)
            {
                user.Role = await _db.Roles
                    .Include(r => r.RoleFunctionGroups)
                        .ThenInclude(rfg => rfg.FunctionGroup)
                    .FirstOrDefaultAsync(r => r.Id == user.RoleId.Value, ct);
            }

            var permissionCodes = await ResolvePermissionCodesAsync(user, ct);
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username ?? string.Empty),
                new Claim(ClaimTypes.Name, user.Username ?? string.Empty)
            };

            if (!string.IsNullOrWhiteSpace(user.FullName))
            {
                claims.Add(new Claim("full_name", user.FullName));
            }

            if (!string.IsNullOrWhiteSpace(user.Role?.Name))
            {
                claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
            }

            foreach (var permission in permissionCodes)
            {
                claims.Add(new Claim("permission", permission));
            }

            var keyBytes = Encoding.UTF8.GetBytes(_jwt.Secret ?? string.Empty);
            var key = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expiresUtc = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
            var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                expires: expiresUtc,
                signingCredentials: creds);

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenString = tokenHandler.WriteToken(token);

            return new LoginResponse
            {
                AccessToken = tokenString,
                ExpiresAt = new DateTimeOffset(expiresUtc, TimeSpan.Zero)
            };
        }

        private async Task<List<string>> ResolvePermissionCodesAsync(User user, CancellationToken ct)
        {
            Role? role = user.Role;
            if (role == null && user.RoleId.HasValue)
            {
                role = await _db.Roles
                    .Include(r => r.RoleFunctionGroups)
                        .ThenInclude(rfg => rfg.FunctionGroup)
                    .FirstOrDefaultAsync(r => r.Id == user.RoleId.Value, ct);
            }

            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (role != null)
            {
                foreach (var functionCode in role.RoleFunctionGroups
                    .Where(rfg => rfg.FunctionGroup != null && !string.IsNullOrWhiteSpace(rfg.FunctionGroup.Code))
                    .Select(rfg => rfg.FunctionGroup!.Code))
                {
                    permissions.Add(functionCode);
                }

            }

            return permissions
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task ReplaceRoleFunctionGroupsAsync(Guid roleId, IEnumerable<string> permissionCodes, CancellationToken ct)
        {
            var normalizedCodes = (permissionCodes ?? Enumerable.Empty<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var functionGroups = await _db.FunctionGroups
                .Where(group => normalizedCodes.Contains(group.Code))
                .ToListAsync(ct);

            var existing = await _db.RoleFunctionGroups
                .Where(link => link.RoleId == roleId)
                .ToListAsync(ct);

            _db.RoleFunctionGroups.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);

            foreach (var functionGroup in functionGroups)
            {
                _db.RoleFunctionGroups.Add(new RoleFunctionGroup
                {
                    RoleId = roleId,
                    FunctionGroupId = functionGroup.Id
                });
            }

            await _db.SaveChangesAsync(ct);
        }

        private async Task<RoleDto> BuildRoleDtoAsync(Guid roleId, CancellationToken ct)
        {
            var role = await _db.Roles
                .Include(r => r.RoleFunctionGroups)
                    .ThenInclude(rfg => rfg.FunctionGroup)
                .FirstAsync(r => r.Id == roleId, ct);

            return new RoleDto
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Name,
                Permissions = role.RoleFunctionGroups
                    .Where(rfg => rfg.FunctionGroup != null)
                    .Select(rfg => rfg.FunctionGroup!.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private static string NormalizeRoleCode(string? code)
        {
            return code?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private async Task RegisterFailedLoginAttemptAsync(User user, DateTimeOffset now, CancellationToken ct)
        {
            ResetFailedLoginStateIfWindowExpired(user, now);

            user.FailedLoginAttempts++;
            user.LastFailedLoginAt = now;
            user.UpdatedAt = now;

            var newValue = new
            {
                user.Username,
                user.FailedLoginAttempts,
                user.LastFailedLoginAt
            };

            if (ShouldLockout(user))
            {
                user.LockoutEndAt = now.AddMinutes(GetLockoutMinutes());
                await RevokeActiveRefreshTokensAsync(user.Id, ct);

                await WriteAuditLogAsync(
                    actorUserId: user.Id,
                    action: "ACCOUNT_TEMPORARILY_LOCKED",
                    targetType: "User",
                    targetId: user.Id.ToString(),
                    oldValue: null,
                    newValue: new
                    {
                        user.Username,
                        user.FailedLoginAttempts,
                        user.LockoutEndAt
                    },
                    ct);

                throw new UnauthorizedAccessException(BuildNewLockoutMessage());
            }

            await WriteAuditLogAsync(
                actorUserId: user.Id,
                action: "LOGIN_FAILED",
                targetType: "User",
                targetId: user.Id.ToString(),
                oldValue: null,
                newValue: newValue,
                ct);
        }

        private async Task RevokeActiveRefreshTokensAsync(Guid userId, CancellationToken ct)
        {
            var activeTokens = await _db.RefreshTokens
                .Where(token => token.UserId == userId && !token.Revoked && token.ExpiresAt > DateTimeOffset.UtcNow)
                .ToListAsync(ct);

            foreach (var token in activeTokens)
            {
                token.Revoked = true;
            }
        }

        private void ResetFailedLoginStateIfWindowExpired(User user, DateTimeOffset now)
        {
            if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value <= now)
            {
                ResetFailedLoginState(user);
                return;
            }

            var window = TimeSpan.FromMinutes(GetFailedAttemptWindowMinutes());
            if (user.LastFailedLoginAt.HasValue && now - user.LastFailedLoginAt.Value > window)
            {
                ResetFailedLoginState(user);
            }
        }

        private static void ResetFailedLoginState(User user)
        {
            user.FailedLoginAttempts = 0;
            user.LastFailedLoginAt = null;
            user.LockoutEndAt = null;
        }

        private bool ShouldLockout(User user)
        {
            return user.FailedLoginAttempts >= GetMaxFailedLoginAttempts();
        }

        private static bool IsLockoutActive(User user, DateTimeOffset now)
        {
            return user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > now;
        }

        private string BuildActiveLockoutMessage(DateTimeOffset lockoutEndAt, DateTimeOffset now)
        {
            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((lockoutEndAt - now).TotalMinutes));
            return $"Tài khoản đang tạm thời bị khoá. Vui lòng thử lại sau khoảng {remainingMinutes} phút.";
        }

        private string BuildNewLockoutMessage()
        {
            return $"Tài khoản tạm thời bị khoá trong {GetLockoutMinutes()} phút do đăng nhập sai quá số lần cho phép.";
        }

        private int GetMaxFailedLoginAttempts()
        {
            return Math.Max(1, _loginSecurity.MaxFailedLoginAttempts);
        }

        private int GetFailedAttemptWindowMinutes()
        {
            return Math.Max(1, _loginSecurity.FailedAttemptWindowMinutes);
        }

        private int GetLockoutMinutes()
        {
            return Math.Max(1, _loginSecurity.LockoutMinutes);
        }

        private async Task WriteAuditLogAsync(
            Guid? actorUserId,
            string action,
            string targetType,
            string targetId,
            object? oldValue,
            object? newValue,
            CancellationToken ct)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                OldValueJson = oldValue == null ? null : JsonSerializer.Serialize(oldValue),
                NewValueJson = newValue == null ? null : JsonSerializer.Serialize(newValue),
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }

        private static bool IsAdminLikeRole(string? roleNameOrCode)
        {
            return !string.IsNullOrWhiteSpace(roleNameOrCode)
                && AdminLikeRoles.Contains(roleNameOrCode, StringComparer.OrdinalIgnoreCase);
        }

        private static string GenerateSecureToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        private static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var data = Encoding.UTF8.GetBytes(token);
            var hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash);
        }
    }
}
