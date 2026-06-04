using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Station.Config;
using Station.DTOs;

namespace Station.Services
{
    public class UserApiService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public UserApiService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(EnvironmentConfig.BackendBaseUrl.TrimEnd('/') + "/")
            };
        }

        public async Task<List<UserDto>> GetUsersAsync()
        {
            SetAuthorizationHeader();
            var response = await _httpClient.GetAsync("api/Auth/users");
            var content = await EnsureSuccessAsync(response);

            return JsonSerializer.Deserialize<List<UserDto>>(content, _jsonOptions) ?? new List<UserDto>();
        }

        public async Task<List<RoleDto>> GetRolesAsync()
        {
            SetAuthorizationHeader();
            var response = await _httpClient.GetAsync("api/Auth/roles");
            var content = await EnsureSuccessAsync(response);

            return JsonSerializer.Deserialize<List<RoleDto>>(content, _jsonOptions) ?? new List<RoleDto>();
        }

        public async Task<List<PermissionDto>> GetPermissionsAsync()
        {
            SetAuthorizationHeader();
            var response = await _httpClient.GetAsync("api/Auth/permissions");
            var content = await EnsureSuccessAsync(response);

            return JsonSerializer.Deserialize<List<PermissionDto>>(content, _jsonOptions) ?? new List<PermissionDto>();
        }

        public async Task<List<AuditLogDto>> GetAuditLogsAsync()
        {
            SetAuthorizationHeader();
            var response = await _httpClient.GetAsync("api/Auth/audit-logs");
            var content = await EnsureSuccessAsync(response);

            return JsonSerializer.Deserialize<List<AuditLogDto>>(content, _jsonOptions) ?? new List<AuditLogDto>();
        }

        public async Task<RoleDto> CreateRoleAsync(string name, string code, bool isSystem, IReadOnlyList<string> permissionCodes)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                code,
                name,
                isSystem,
                permissionCodes = permissionCodes ?? Array.Empty<string>()
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("api/Auth/roles", content);
            var responseContent = await EnsureSuccessAsync(response);

            return JsonSerializer.Deserialize<RoleDto>(responseContent, _jsonOptions)
                ?? throw new Exception("Không đọc được dữ liệu vai trò vừa tạo.");
        }

        public async Task<RoleDto> UpdateRoleAsync(Guid roleId, string name, string code, bool isSystem, IReadOnlyList<string> permissionCodes)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                code,
                name,
                isSystem,
                permissionCodes = permissionCodes ?? Array.Empty<string>()
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"api/Auth/roles/{roleId}", content);
            var responseContent = await EnsureSuccessAsync(response);

            return JsonSerializer.Deserialize<RoleDto>(responseContent, _jsonOptions)
                ?? throw new Exception("Không đọc được dữ liệu vai trò vừa cập nhật.");
        }

        public async Task DeleteRoleAsync(Guid roleId)
        {
            SetAuthorizationHeader();
            var response = await _httpClient.DeleteAsync($"api/Auth/roles/{roleId}");
            await EnsureSuccessAsync(response);
        }

        public async Task CreateUserAsync(string username, string fullName, string password)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                username,
                fullName,
                password
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("api/Auth/create-user", content);
            await EnsureSuccessAsync(response);
        }

        public async Task SaveUserAccessAsync(Guid userId, IReadOnlyList<Guid> roleIds, bool isActive)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                userId,
                roleIds,
                grantedPermissionIds = Array.Empty<Guid>(),
                deniedPermissionIds = Array.Empty<Guid>(),
                isActive
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"api/Auth/users/{userId}/access", content);
            await EnsureSuccessAsync(response);
        }

        public async Task UpdateUserAsync(Guid userId, string username, string fullName, Guid? roleId, bool isActive, string? newPassword = null)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                username,
                fullName,
                roleId,
                newPassword,
                isActive
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"api/Auth/users/{userId}", content);
            await EnsureSuccessAsync(response);
        }

        public async Task DeleteUserAsync(Guid userId)
        {
            SetAuthorizationHeader();
            var response = await _httpClient.DeleteAsync($"api/Auth/users/{userId}");
            await EnsureSuccessAsync(response);
        }

        public async Task ChangePasswordAsync(string currentPassword, string newPassword)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                currentPassword,
                newPassword
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("api/Auth/change-password", content);
            await EnsureSuccessAsync(response);
        }

        public async Task UpdateProfileAsync(string username, string fullName)
        {
            SetAuthorizationHeader();

            var payload = JsonSerializer.Serialize(new
            {
                username,
                fullName
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync("api/Auth/profile", content);
            await EnsureSuccessAsync(response);
        }

        private void SetAuthorizationHeader()
        {
            if (string.IsNullOrWhiteSpace(AuthSession.AccessToken))
                throw new InvalidOperationException("Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.");

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", AuthSession.AccessToken);
        }

        private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API Error: {response.StatusCode} - {content}");

            return content;
        }
    }
}
