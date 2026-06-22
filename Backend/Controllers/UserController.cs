using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TunnelSecurity.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        [Authorize]
        [HttpGet("profile")]
        public IActionResult Profile()
        {
            return Ok("Authenticated user");
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            return Ok(new
            {
                username,
                role
            });
        }
    }
}