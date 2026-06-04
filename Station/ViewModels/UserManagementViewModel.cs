using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Station.DTOs;
using Station.Services;
using Windows.UI;

namespace Station.ViewModels
{
    public partial class UserManagementViewModel : ObservableObject
    {
        private readonly UserApiService _userApiService = new();

        public ObservableCollection<UserItem> Users { get; } = new();
        public ObservableCollection<UserItem> FilteredUsers { get; } = new();
        public ObservableCollection<RoleItem> Roles { get; } = new();
        public ObservableCollection<RoleItem> FilteredRoles { get; } = new();

        [ObservableProperty]
        private RoleItem? selectedRole;

        [ObservableProperty]
        private string loadStatus = "Chưa tải dữ liệu phân quyền.";

        private string userSearchText = string.Empty;
        public string UserSearchText
        {
            get => userSearchText;
            set
            {
                if (SetProperty(ref userSearchText, value))
                {
                    RefreshFilteredUsers();
                }
            }
        }

        private string roleSearchText = string.Empty;
        public string RoleSearchText
        {
            get => roleSearchText;
            set
            {
                if (SetProperty(ref roleSearchText, value))
                {
                    RefreshFilteredRoles();
                }
            }
        }

        private string userRoleFilter = "all";
        public string UserRoleFilter
        {
            get => userRoleFilter;
            set
            {
                if (SetProperty(ref userRoleFilter, value))
                {
                    RefreshFilteredUsers();
                }
            }
        }

        private string userStatusFilter = "all";
        public string UserStatusFilter
        {
            get => userStatusFilter;
            set
            {
                if (SetProperty(ref userStatusFilter, value))
                {
                    RefreshFilteredUsers();
                }
            }
        }

        private string roleDisplayFilter = "all";
        public string RoleDisplayFilter
        {
            get => roleDisplayFilter;
            set
            {
                if (SetProperty(ref roleDisplayFilter, value))
                {
                    RefreshFilteredRoles();
                }
            }
        }

        private string roleSortOption = "name_asc";
        public string RoleSortOption
        {
            get => roleSortOption;
            set
            {
                if (SetProperty(ref roleSortOption, value))
                {
                    RefreshFilteredRoles();
                }
            }
        }

        private UserItem? selectedUser;
        public UserItem? SelectedUser
        {
            get => selectedUser;
            set => SetProperty(ref selectedUser, value);
        }

        public int TotalUsers => Users.Count;
        public int DisplayedUsersCount => FilteredUsers.Count;
        public int ActiveUsers => Users.Count(u => u.IsActive);
        public int LockedUsers => Users.Count(u => !u.IsActive);
        public int TotalRoles => Roles.Count;
        public int DisplayedRolesCount => FilteredRoles.Count;
        public int StandardRoles => Roles.Count(r => r.IsSystem);
        public int CustomRoles => Roles.Count(r => !r.IsSystem);
        public int TotalUsersRoles => Roles.Sum(r => r.UsersCount);
        public double AvgPermissions => Roles.Count == 0 ? 0 : Math.Round(Roles.Average(r => r.PermissionsCount), 1);

        public UserManagementViewModel()
        {
            Users.CollectionChanged += Users_CollectionChanged;
            Roles.CollectionChanged += Roles_CollectionChanged;
            SeedFallbackRoles();
        }

        public async Task LoadAccessDataAsync()
        {
            if (!AuthSession.IsAuthenticated)
            {
                LoadStatus = "Chưa có phiên đăng nhập. Hãy đăng nhập lại để tải dữ liệu RBAC.";
                return;
            }

            if (!AuthSession.HasAnyPermission("SYSTEM_ADMINISTRATION", "SYSTEM_ADMIN", "users.manage", "roles.manage"))
            {
                Users.Clear();
                FilteredUsers.Clear();
                Roles.Clear();
                FilteredRoles.Clear();
                SelectedUser = null;
                SelectedRole = null;
                OnPropertyChanged(nameof(DisplayedUsersCount));
                OnPropertyChanged(nameof(DisplayedRolesCount));
                LoadStatus = "Bạn không có quyền truy cập chức năng quản trị người dùng.";
                return;
            }

            try
            {
                LoadStatus = "Đang tải người dùng và phân quyền...";

                var users = await _userApiService.GetUsersAsync();
                Users.Clear();

                foreach (var user in users)
                {
                    Users.Add(new UserItem
                    {
                        Id = user.Id,
                        UserName = user.Username,
                        FullName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
                        Role = user.Roles.Count > 0
                            ? string.Join(", ", user.Roles.Where(role => !string.IsNullOrWhiteSpace(role)))
                            : (string.IsNullOrWhiteSpace(user.Role) ? string.Empty : user.Role),
                        IsActive = user.IsActive,
                        Permissions = user.PermissionsDisplay,
                        PermissionsCount = PermissionCatalog.NormalizeCodes(user.Permissions).Count
                    });
                }

                RefreshFilteredUsers();

                try
                {
                    var roles = await _userApiService.GetRolesAsync();
                    Roles.Clear();

                    foreach (var role in roles)
                    {
                        var roleItem = new RoleItem
                        {
                            Id = role.Id,
                            Code = role.Code,
                            Name = role.Name,
                            Description = role.Code,
                            IsSystem = IsStandardRoleCode(role.Code),
                            UsersCount = users.Count(u => u.Roles.Contains(role.Name)),
                            PermissionsCount = PermissionCatalog.NormalizeCodes(role.Permissions).Count
                        };

                        foreach (var permission in PermissionCatalog.NormalizeCodes(role.Permissions))
                        {
                            roleItem.Permissions.Add(permission);
                        }

                        Roles.Add(roleItem);
                    }

                    RefreshFilteredRoles();
                    SelectedRole = Roles.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    LoadStatus = "Đã tải người dùng. Chưa tải được danh sách vai trò: " + ex.Message;
                    return;
                }

                LoadStatus = $"Đã tải {Users.Count} người dùng, {Roles.Count} vai trò.";
            }
            catch (Exception ex)
            {
                LoadStatus = "Không tải được dữ liệu phân quyền: " + ex.Message;
            }
        }

        private void SeedFallbackRoles()
        {
            Roles.Clear();

            var manager = new RoleItem
            {
                Code = "ADMIN",
                Name = "Admin",
                Description = "ADMIN",
                IsSystem = true,
                UsersCount = 0
            };
            manager.Permissions.Add("DASHBOARD_MONITORING");
            manager.Permissions.Add("MONITORING_DETAIL");
            manager.Permissions.Add("DEVICE_MANAGEMENT");
            manager.Permissions.Add("ALERT_EVENT_MANAGEMENT");
            manager.Permissions.Add("DATA_HISTORY_REPORTING");
            manager.Permissions.Add("SYSTEM_ADMINISTRATION");
            manager.PermissionsCount = manager.Permissions.Count;

            var staff = new RoleItem
            {
                Code = "OPERATOR",
                Name = "Operator",
                Description = "OPERATOR",
                IsSystem = true,
                UsersCount = 0
            };
            staff.Permissions.Add("DASHBOARD_MONITORING");
            staff.Permissions.Add("MONITORING_DETAIL");
            staff.Permissions.Add("DEVICE_MANAGEMENT");
            staff.Permissions.Add("ALERT_EVENT_MANAGEMENT");
            staff.PermissionsCount = staff.Permissions.Count;

            var viewer = new RoleItem
            {
                Code = "VIEWER",
                Name = "Viewer",
                Description = "VIEWER",
                IsSystem = true,
                UsersCount = 0
            };
            viewer.Permissions.Add("DASHBOARD_MONITORING");
            viewer.Permissions.Add("DATA_HISTORY_REPORTING");
            viewer.PermissionsCount = viewer.Permissions.Count;

            Roles.Add(manager);
            Roles.Add(staff);
            Roles.Add(viewer);
            RefreshFilteredRoles();
            SelectedRole = Roles.FirstOrDefault();
            RefreshFilteredUsers();
        }

        private void Users_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(TotalUsers));
            RefreshFilteredUsers();
            OnPropertyChanged(nameof(ActiveUsers));
            OnPropertyChanged(nameof(LockedUsers));
            RefreshRoleUserCounts();
        }

        private void Roles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshFilteredRoles();
            OnPropertyChanged(nameof(TotalRoles));
            OnPropertyChanged(nameof(StandardRoles));
            OnPropertyChanged(nameof(DisplayedRolesCount));
            OnPropertyChanged(nameof(CustomRoles));
            OnPropertyChanged(nameof(AvgPermissions));
            OnPropertyChanged(nameof(TotalUsersRoles));
        }

        public void AddUser(UserItem user)
        {
            Users.Add(user);
        }

        public async Task CreateUserAsync(UserItem user, string password)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Mật khẩu không được để trống.");

            await _userApiService.CreateUserAsync(user.UserName, user.FullName, password);
            await LoadAccessDataAsync();

            var createdUser = Users.FirstOrDefault(u =>
                string.Equals(u.UserName, user.UserName, StringComparison.OrdinalIgnoreCase));

            if (createdUser == null)
                return;

            var roleIds = ResolveRoleIds(user.Role);
            await _userApiService.SaveUserAccessAsync(createdUser.Id, roleIds, user.IsActive);
            await LoadAccessDataAsync();
        }

        public async Task UpdateUserAccessAsync(UserItem target, UserItem updated, string? newPassword = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (updated == null) throw new ArgumentNullException(nameof(updated));

            var roleId = ResolvePrimaryRoleId(updated.Role);
            await _userApiService.UpdateUserAsync(
                target.Id,
                updated.UserName,
                updated.FullName,
                roleId,
                updated.IsActive,
                string.IsNullOrWhiteSpace(newPassword) ? null : newPassword.Trim());
            await LoadAccessDataAsync();
        }

        public void UpdateUser(UserItem target, UserItem updated)
        {
            if (target == null) return;

            target.UserName = updated.UserName;
            target.FullName = updated.FullName;
            target.Role = updated.Role;
            target.Note = updated.Note;
            target.IsActive = updated.IsActive;
            target.Permissions = updated.Permissions;
            target.PermissionsCount = updated.PermissionsCount;

            Users_CollectionChanged(this, null!);
        }

        public async Task DeleteUserAsync(UserItem user)
        {
            if (user == null) return;
            await _userApiService.DeleteUserAsync(user.Id);
            await LoadAccessDataAsync();
        }

        private List<Guid> ResolveRoleIds(string? rolesText)
        {
            return (rolesText ?? string.Empty)
                .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(roleName => Roles.FirstOrDefault(role =>
                    string.Equals(role.Name, roleName, StringComparison.OrdinalIgnoreCase))?.Id)
                .Where(roleId => roleId.HasValue)
                .Select(roleId => roleId!.Value)
                .Distinct()
                .ToList();
        }

        private Guid? ResolvePrimaryRoleId(string? rolesText)
        {
            return ResolveRoleIds(rolesText).FirstOrDefault();
        }

        private void RefreshFilteredUsers()
        {
            var search = UserSearchText?.Trim() ?? string.Empty;
            var filtered = Users
                .Where(user => MatchesUserSearch(user, search))
                .Where(MatchesUserRoleFilter)
                .Where(MatchesUserStatusFilter)
                .ToList();

            FilteredUsers.Clear();
            var index = 1;
            foreach (var user in filtered)
            {
                user.OrderNumber = index++;
                FilteredUsers.Add(user);
            }

            if (SelectedUser != null && !FilteredUsers.Contains(SelectedUser))
            {
                SelectedUser = FilteredUsers.FirstOrDefault();
            }

            OnPropertyChanged(nameof(DisplayedUsersCount));
        }

        private void RefreshFilteredRoles()
        {
            var search = RoleSearchText?.Trim() ?? string.Empty;
            var filtered = Roles
                .Where(role => MatchesRoleSearch(role, search))
                .Where(MatchesRoleDisplayFilter)
                .ToList();

            filtered = RoleSortOption switch
            {
                "name_desc" => filtered
                    .OrderByDescending(role => role.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                "permissions_desc" => filtered
                    .OrderByDescending(role => role.PermissionsCount)
                    .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                "users_desc" => filtered
                    .OrderByDescending(role => role.UsersCount)
                    .ThenBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                _ => filtered
                    .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            FilteredRoles.Clear();
            var index = 1;
            foreach (var role in filtered)
            {
                role.OrderNumber = index++;
                FilteredRoles.Add(role);
            }

            OnPropertyChanged(nameof(DisplayedRolesCount));

            if (SelectedRole != null && !FilteredRoles.Contains(SelectedRole))
            {
                SelectedRole = FilteredRoles.FirstOrDefault();
            }
        }

        private static bool MatchesUserSearch(UserItem user, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return ContainsIgnoreCase(user.UserName, search)
                || ContainsIgnoreCase(user.FullName, search)
                || ContainsIgnoreCase(user.Role, search)
                || ContainsIgnoreCase(user.Permissions, search);
        }

        private bool MatchesUserRoleFilter(UserItem user)
        {
            return UserRoleFilter switch
            {
                "all" => true,
                "unassigned" => string.IsNullOrWhiteSpace(user.Role) || string.Equals(user.Role, "No role", StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(user.RoleDisplay, UserRoleFilter, StringComparison.OrdinalIgnoreCase)
                     || user.Role.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                         .Any(role => string.Equals(role, UserRoleFilter, StringComparison.OrdinalIgnoreCase))
            };
        }

        private bool MatchesUserStatusFilter(UserItem user)
        {
            return UserStatusFilter switch
            {
                "active" => user.IsActive,
                "locked" => !user.IsActive,
                _ => true
            };
        }

        private static bool IsStandardRoleCode(string? code)
        {
            return string.Equals(code, "VIEWER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "OPERATOR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "ADMIN", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string? value, string search)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesRoleSearch(RoleItem role, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return ContainsIgnoreCase(role.Name, search)
                || ContainsIgnoreCase(role.Code, search)
                || ContainsIgnoreCase(role.Description, search)
                || ContainsIgnoreCase(role.KindText, search)
                || role.Permissions.Any(permission => ContainsIgnoreCase(permission, search));
        }

        private bool MatchesRoleDisplayFilter(RoleItem role)
        {
            return RoleDisplayFilter switch
            {
                "system" => role.IsSystem,
                "custom" => !role.IsSystem,
                "assigned" => role.UsersCount > 0,
                _ => true
            };
        }

        public void AddRole(RoleItem role)
        {
            if (role == null) return;
            Roles.Add(role);
        }

        public async Task CreateRoleAsync(RoleItem role)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));

            await _userApiService.CreateRoleAsync(
                role.Name,
                role.Code,
                role.IsSystem,
                role.Permissions.ToList());

            await LoadAccessDataAsync();
        }

        public void UpdateRole(RoleItem target, RoleItem updated)
        {
            if (target == null || updated == null) return;

            target.Code = updated.Code;
            target.Name = updated.Name;
            target.Description = updated.Description;
            target.IsSystem = updated.IsSystem;
            target.UsersCount = updated.UsersCount;
            target.Permissions.Clear();

            foreach (var p in updated.Permissions)
            {
                target.Permissions.Add(p);
            }

            target.PermissionsCount = target.Permissions.Count;
            Roles_CollectionChanged(this, null!);
        }

        public async Task UpdateRoleAsync(RoleItem target, RoleItem updated)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (updated == null) throw new ArgumentNullException(nameof(updated));

            await _userApiService.UpdateRoleAsync(
                target.Id,
                updated.Name,
                updated.Code,
                updated.IsSystem,
                updated.Permissions.ToList());

            await LoadAccessDataAsync();
        }

        public void DeleteRole(RoleItem role)
        {
            if (role == null) return;
            Roles.Remove(role);
        }

        public async Task DeleteRoleAsync(RoleItem role)
        {
            if (role == null) return;
            await _userApiService.DeleteRoleAsync(role.Id);
            await LoadAccessDataAsync();
        }

        private void RefreshRoleUserCounts()
        {
            foreach (var role in Roles)
            {
                role.UsersCount = Users.Count(u =>
                    u.Role.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                        .Contains(role.Name));
            }

            Roles_CollectionChanged(this, null!);
        }
    }

    public partial class RoleItem : ObservableObject
    {
        public RoleItem()
        {
            Permissions.CollectionChanged += (_, _) => RefreshPermissionPresentation();
        }

        public Guid Id { get; set; }

        private string code = string.Empty;
        public string Code
        {
            get => code;
            set => SetProperty(ref code, value);
        }

        private int orderNumber;
        public int OrderNumber
        {
            get => orderNumber;
            set => SetProperty(ref orderNumber, value);
        }

        private string name = string.Empty;
        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }

        private string description = string.Empty;
        public string Description
        {
            get => description;
            set => SetProperty(ref description, value);
        }

        private bool isSystem;
        public bool IsSystem
        {
            get => isSystem;
            set
            {
                if (SetProperty(ref isSystem, value))
                {
                    OnPropertyChanged(nameof(KindText));
                    OnPropertyChanged(nameof(BadgeBrush));
                }
            }
        }

        private int permissionsCount;
        public int PermissionsCount
        {
            get => permissionsCount;
            set => SetProperty(ref permissionsCount, value);
        }

        private int usersCount;
        public int UsersCount
        {
            get => usersCount;
            set => SetProperty(ref usersCount, value);
        }

        public ObservableCollection<string> Permissions { get; } = new();
        public ObservableCollection<PermissionGroupItem> PermissionGroups { get; } = new();
        public string KindText => IsSystem ? "Hệ thống" : "Tuỳ chỉnh";

        public SolidColorBrush BadgeBrush =>
            IsSystem
                ? new SolidColorBrush(Color.FromArgb(255, 59, 130, 246))
                : new SolidColorBrush(Color.FromArgb(255, 234, 179, 8));

        public void RefreshPermissionPresentation()
        {
            PermissionGroups.Clear();

            foreach (var group in PermissionCatalog.BuildGroups(Permissions))
            {
                PermissionGroups.Add(group);
            }

            PermissionsCount = PermissionCatalog.NormalizeCodes(Permissions).Count;
            OnPropertyChanged(nameof(PermissionGroups));
        }
    }

    public class UserItem : ObservableObject
    {
        public Guid Id { get; set; }

        private int orderNumber;
        public int OrderNumber
        {
            get => orderNumber;
            set => SetProperty(ref orderNumber, value);
        }

        private string userName = string.Empty;
        public string UserName
        {
            get => userName;
            set => SetProperty(ref userName, value);
        }

        private string fullName = string.Empty;
        public string FullName
        {
            get => fullName;
            set => SetProperty(ref fullName, value);
        }

        private string role = "No role";
        public string Role
        {
            get => role;
            set
            {
                if (SetProperty(ref role, value))
                {
                    OnPropertyChanged(nameof(RoleDisplay));
                }
            }
        }

        public string RoleDisplay =>
            string.IsNullOrWhiteSpace(Role) ? "Chưa gán vai trò" : Role;

        private string permissions = "No permission";
        public string Permissions
        {
            get => permissions;
            set => SetProperty(ref permissions, value);
        }

        private int permissionsCount;
        public int PermissionsCount
        {
            get => permissionsCount;
            set => SetProperty(ref permissionsCount, value);
        }

        private string note = string.Empty;
        public string Note
        {
            get => note;
            set => SetProperty(ref note, value);
        }

        private bool isActive = true;
        public bool IsActive
        {
            get => isActive;
            set
            {
                if (SetProperty(ref isActive, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        public string StatusText => IsActive ? "Hoạt động" : "Khóa";

        public SolidColorBrush StatusBrush =>
            IsActive
                ? new SolidColorBrush(Color.FromArgb(255, 22, 163, 74))
                : new SolidColorBrush(Color.FromArgb(255, 148, 163, 184));
    }

    public sealed class PermissionDefinition
    {
        public PermissionDefinition(string code, string groupName, params string[] actions)
        {
            Code = code;
            GroupName = groupName;
            Actions = actions;
        }

        public string Code { get; }
        public string GroupName { get; }
        public IReadOnlyList<string> Actions { get; }
    }

    public sealed class PermissionGroupItem
    {
        public PermissionGroupItem(string name, IEnumerable<string> actions)
        {
            Name = name;
            Actions = new ObservableCollection<string>(actions);
        }

        public string Name { get; }
        public ObservableCollection<string> Actions { get; }
    }

    public static class PermissionCatalog
    {
        private static readonly IReadOnlyDictionary<string, string[]> LegacyCodeMap =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
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
                ["SYSTEM_ADMIN"] = new[] { "SYSTEM_ADMINISTRATION" }
            };

        private static readonly IReadOnlyList<PermissionDefinition> Definitions =
            new[]
            {
                new PermissionDefinition(
                    "DASHBOARD_MONITORING",
                    "Giám sát tổng quan",
                    "Màn hình trung tâm, tổng hợp trạng thái toàn trạm"),
                new PermissionDefinition(
                    "MONITORING_DETAIL",
                    "Giám sát chi tiết",
                    "Giao diện chuyên dụng cho giám sát viên quan sát dữ liệu, camera, AI realtime"),
                new PermissionDefinition(
                    "DEVICE_MANAGEMENT",
                    "Quản lý thiết bị",
                    "Quản lý tuyến, cụm, node, sensor, camera, thiết bị ngoại vi và điều khiển thiết bị"),
                new PermissionDefinition(
                    "ALERT_EVENT_MANAGEMENT",
                    "Quản lý cảnh báo",
                    "Xem, lọc, xác nhận, xử lý, đóng/mở lại cảnh báo và sự kiện"),
                new PermissionDefinition(
                    "DATA_HISTORY_REPORTING",
                    "Báo cáo và phân tích xu hướng",
                    "Tra cứu dữ liệu, xem lịch sử, thống kê, báo cáo và phân tích xu hướng"),
                new PermissionDefinition(
                    "SYSTEM_ADMINISTRATION",
                    "Quản trị hệ thống",
                    "Quản lý user, vai trò, phân quyền, cấu hình hệ thống và audit log")
            };

        public static IReadOnlyList<PermissionDefinition> All => Definitions;

        public static PermissionDefinition GetDefinition(string code)
        {
            var normalized = NormalizeCodes(new[] { code }).FirstOrDefault() ?? code?.Trim() ?? string.Empty;
            var match = Definitions.FirstOrDefault(def =>
                string.Equals(def.Code, normalized, StringComparison.OrdinalIgnoreCase));

            return match ?? new PermissionDefinition(normalized, "Khác", normalized);
        }

        public static IReadOnlyList<string> NormalizeCodes(IEnumerable<string> codes)
        {
            return (codes ?? Enumerable.Empty<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .SelectMany(NormalizeCode)
                .Where(code => Definitions.Any(def => string.Equals(def.Code, code, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetDefinitionOrder)
                .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int GetDefinitionOrder(string code)
        {
            for (var index = 0; index < Definitions.Count; index++)
            {
                if (string.Equals(Definitions[index].Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        private static IEnumerable<string> NormalizeCode(string? code)
        {
            var normalized = code?.Trim() ?? string.Empty;
            if (LegacyCodeMap.TryGetValue(normalized, out var mapped))
            {
                return mapped;
            }

            return string.IsNullOrWhiteSpace(normalized)
                ? Enumerable.Empty<string>()
                : new[] { normalized };
        }

        public static IEnumerable<PermissionGroupItem> BuildGroups(IEnumerable<string> codes)
        {
            return NormalizeCodes(codes)
                .Select(GetDefinition)
                .GroupBy(def => def.GroupName)
                .Select(group => new PermissionGroupItem(
                    group.Key,
                    group.SelectMany(def => def.Actions).Distinct(StringComparer.OrdinalIgnoreCase)));
        }
    }
}
