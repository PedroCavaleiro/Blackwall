using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("users")]
public sealed class UsersController : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new {
            id = User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            discordUserId = User.FindFirstValue("discord_user_id"),
            username = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName),
            displayName = User.FindFirstValue("display_name")
        });
    }
}