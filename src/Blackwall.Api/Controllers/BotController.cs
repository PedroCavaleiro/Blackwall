using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class BotController(
    DiscordOAuthService discordOAuthService,
    BlackwallDbContext dbContext,
    GuildClaimService guildClaimService,
    DiscordGuildCacheService guildCache
) : ControllerBase
{
    /// <summary>
    /// Returns the Discord OAuth2 URL to add Blackwall to a server.
    /// Optionally pre-selects a target guild if the current user can manage it.
    /// </summary>
    /// <param name="guildId">The Discord guild ID to pre-select in the bot invite flow.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bot invite URL.</returns>
    /// <response code="200">Returns the bot invite URL.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [Authorize]
    [HttpGet("invite")]
    [ProducesResponseType(typeof(BotInviteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInviteUrl([FromQuery] long? guildId, CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();

        if (appUserId is null) {
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        if (guildId.HasValue) {
            try {
                var guilds = await LoadManageableGuildsForUser(appUserId.Value, cancellationToken);
                var guild = guilds.FirstOrDefault(x => x.DiscordGuildId == guildId.Value);

                if (guild is null || !guild.CanManage)
                    return Forbid();
            }
            catch (InvalidOperationException ex) {
                return Unauthorized(new ProblemDetails {
                    Title = "Unable to verify guild access.",
                    Detail = ex.Message,
                    Status = StatusCodes.Status401Unauthorized
                });
            }
            catch (HttpRequestException ex) {
                return Unauthorized(new ProblemDetails {
                    Title = "Discord API error.",
                    Detail = ex.Message,
                    Status = StatusCodes.Status401Unauthorized
                });
            }
        }

        var url = discordOAuthService.BuildBotInviteUrl(guildId);
        return Ok(new BotInviteResponse(url));
    }

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }

    private async Task<IReadOnlyList<ManageableGuildResponse>> LoadManageableGuildsForUser(
        long appUserId,
        CancellationToken cancellationToken
    ) {
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId, cancellationToken);

        if (user is null)
            throw new InvalidOperationException("Authenticated user no longer exists.");

        if (string.IsNullOrWhiteSpace(user.DiscordAccessToken))
            throw new InvalidOperationException("No Discord access token is available for this user.");

        var cachedGuilds = await guildCache.GetAsync(appUserId, cancellationToken);
        if (cachedGuilds is not null) {
            await guildClaimService.ClaimOwnershipAsync(appUserId, cachedGuilds, cancellationToken);
            return await guildClaimService.GetManageableGuildsAsync(appUserId, cachedGuilds, cancellationToken);
        }

        var accessToken = await discordOAuthService.EnsureFreshAccessTokenAsync(user, cancellationToken);
        var guilds = await discordOAuthService.GetCurrentUserGuildsAsync(accessToken, cancellationToken);
        await guildClaimService.ClaimOwnershipAsync(user.Id, guilds, cancellationToken);
        await guildCache.StoreAsync(appUserId, guilds, cancellationToken);

        return await guildClaimService.GetManageableGuildsAsync(user.Id, guilds, cancellationToken);
    }
}