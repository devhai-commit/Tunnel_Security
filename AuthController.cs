using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TunnelSecurity.Auth.DTOs;
using TunnelSecurity.Auth.Services;

namespace TunnelSecurity.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;

        public AuthController(IAuthService auth) => _auth = auth;

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
        {
            await _auth.RegisterAsync(request, ct);
            return Created(string.Empty, null);
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        {
            var res = await _auth.LoginAsync(request, ct);
            return Ok(res);
        }

        [HttpPost("refresh")]
        public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
        {
            var res = await _auth.RefreshAsync(request.RefreshToken, ct);
            return Ok(res);
        }

        [Authorize]
        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke([FromBody] RevokeRequest request, CancellationToken ct)
        {
            await _auth.RevokeAsync(request.RefreshToken, ct);
            return NoContent();
        }
    }
}