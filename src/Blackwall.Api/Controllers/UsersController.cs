using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class UsersController : ControllerBase {
    /// <summary>
    /// Returns the profile of the currently authenticated user, derived from their JWT claims.
    /// </summary>
    /// <returns>The authenticated user's ID, Discord user ID, username, and display name.</returns>
    /// <response code="200">Returns the current user's profile.</response>
    /// <response code="401">The request is not authenticated.</response>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me() {
        return Ok(new {
            id = User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            discordUserId = User.FindFirstValue("discord_user_id"),
            username = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName),
            displayName = User.FindFirstValue("display_name")
        });
    }
}