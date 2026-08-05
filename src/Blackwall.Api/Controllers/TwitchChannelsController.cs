using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Api.Services.Twitch;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public sealed class TwitchChannelsController(
    TwitchOAuthService twitchOAuthService,
    TwitchChannelService twitchChannelService,
    BlackwallDbContext dbContext
) : ControllerBase {

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ManageableTwitchChannelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ManageableTwitchChannelResponse>>> Get(
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId.Value, cancellationToken);

        if (user is null)
            return Unauthorized(new ProblemDetails {
                Title = "User not found.",
                Detail = "The authenticated user no longer exists.",
                Status = StatusCodes.Status401Unauthorized
            });

        if (!user.TwitchUserId.HasValue)
            return Ok(new List<ManageableTwitchChannelResponse>());

        var channels = await twitchChannelService.GetManageableChannelsAsync(appUserId.Value, cancellationToken);
        return Ok(channels);
    }

    [HttpGet("install")]
    [ProducesResponseType(typeof(TwitchBotInstallResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TwitchBotInstallResponse>> GetInstallUrl(
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId.Value, cancellationToken);

        if (user is null)
            return Unauthorized(new ProblemDetails {
                Title = "User not found.",
                Detail = "The authenticated user no longer exists.",
                Status = StatusCodes.Status401Unauthorized
            });

        if (!user.TwitchUserId.HasValue)
            return BadRequest(new ProblemDetails {
                Title = "Twitch account not linked.",
                Detail = "You must link your Twitch account before installing the bot.",
                Status = StatusCodes.Status400BadRequest
            });

        var state = await twitchOAuthService.CreateBotInstallStateAsync(appUserId.Value);
        var url = twitchOAuthService.BuildBotInstallUrl(state);

        return Ok(new TwitchBotInstallResponse(url));
    }

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }
}
