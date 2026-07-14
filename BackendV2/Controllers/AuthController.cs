using System.Security.Claims;
using BackendV2.DTOs;
using BackendV2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendV2.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    private bool HasSystemAdminAccess()
    {
        return User.Claims.Any(claim =>
                   string.Equals(claim.Type, "permission", StringComparison.OrdinalIgnoreCase)
                   && (string.Equals(claim.Value, "SYSTEM_ADMINISTRATION", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(claim.Value, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase)))
               || User.IsInRole("Admin")
               || User.IsInRole("ADMIN")
               || User.IsInRole("Administrator")
               || User.IsInRole("StationManager");
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        await _auth.RegisterAsync(req, ct);
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await _auth.LoginAsync(req, ct);
            return Ok(resp);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var resp = await _auth.RefreshAsync(req.RefreshToken, ct);
        return Ok(resp);
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RevokeRequest req, CancellationToken ct)
    {
        await _auth.RevokeAsync(req.RefreshToken, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("create-user")]
    public async Task<IActionResult> CreateUser([FromBody] RegisterRequest req, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        await _auth.RegisterAsync(req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        await _auth.ChangePasswordAsync(userId, req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        await _auth.UpdateProfileAsync(userId, req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        var users = await _auth.GetUsersAsync(ct);
        return Ok(users);
    }

    [Authorize]
    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] SaveUserRequest req, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        await _auth.UpdateUserAsync(id, req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPut("users/{id:guid}/access")]
    public async Task<IActionResult> SaveUserAccess(Guid id, [FromBody] SaveUserAccessRequest req, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        req.UserId = id;
        await _auth.SaveUserAccessAsync(req, ct);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        await _auth.DeleteUserAsync(id, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        var roles = await _auth.GetRolesAsync(ct);
        return Ok(roles);
    }

    [Authorize]
    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions(CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        var permissions = await _auth.GetPermissionsAsync(ct);
        return Ok(permissions);
    }

    [Authorize]
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        var logs = await _auth.GetAuditLogsAsync(ct);
        return Ok(logs);
    }

    [Authorize]
    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] SaveRoleRequest req, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        var role = await _auth.CreateRoleAsync(req, ct);
        return Ok(role);
    }

    [Authorize]
    [HttpPut("roles/{id:guid}")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] SaveRoleRequest req, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        var role = await _auth.UpdateRoleAsync(id, req, ct);
        return Ok(role);
    }

    [Authorize]
    [HttpDelete("roles/{id:guid}")]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        if (!HasSystemAdminAccess())
            return Forbid();

        await _auth.DeleteRoleAsync(id, ct);
        return NoContent();
    }
}
