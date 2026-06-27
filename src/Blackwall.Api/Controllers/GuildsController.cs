using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public sealed class GuildsController(
    BlackwallDbContext dbContext,
    DiscordOAuthService discordOAuthService,
    GuildClaimService guildClaimService,
    IConfiguration configuration
) : ControllerBase {
    
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ManageableGuildResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ManageableGuildResponse>>> Get(
        CancellationToken cancellationToken
    ) {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue("sub");

        if (!long.TryParse(userIdClaim, out var appUserId)) {
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null) {
            return Unauthorized(new ProblemDetails {
                Title = "User not found.",
                Detail = "The authenticated user no longer exists.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        if (string.IsNullOrWhiteSpace(user.DiscordAccessToken)) {
            return Unauthorized(new ProblemDetails {
                Title = "Discord session missing.",
                Detail = "No Discord access token is available for this user.",
                Status = StatusCodes.Status401Unauthorized
            });
        }
        
        var newAccessToken = await discordOAuthService.EnsureFreshAccessTokenAsync(user, cancellationToken);

        var guilds = await discordOAuthService.GetCurrentUserGuildsAsync(
            newAccessToken,
            cancellationToken
        );

        await guildClaimService.ClaimOwnershipAsync(user.Id, guilds, cancellationToken);

        var response = await guildClaimService.GetManageableGuildsAsync(
            user.Id,
            guilds,
            cancellationToken
        );

        return Ok(response);
    }
}