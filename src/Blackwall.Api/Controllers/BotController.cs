using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class BotController(DiscordOAuthService discordOAuthService) : ControllerBase  {
    /// <summary>
    /// Returns the Discord OAuth2 URL to add Blackwall to a server.
    /// </summary>
    /// <param name="guildId">The Discord guild ID to pre-select in the bot invite flow.</param>
    /// <returns>The bot invite URL for the specified guild.</returns>
    /// <response code="200">Returns the bot invite URL.</response>
    /// <response code="400">The guild ID was not provided.</response>
    [Authorize]
    [HttpGet("invite")]
    [ProducesResponseType(typeof(BotInviteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult GetInviteUrl([FromQuery] long? guildId) {
        if (guildId == null)
            return BadRequest(new ProblemDetails {
                Title = "Invalid guild id.",
                Detail = "The guild id is required.",
                Status = StatusCodes.Status400BadRequest
            });
        
        var url = discordOAuthService.BuildBotInviteUrl(guildId);
        return Ok(new BotInviteResponse(url));
    }
}