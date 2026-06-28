using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Cache;
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
    SpamConfigurationCache spamConfigurationCache,
    DiscordGuildCacheService guildCache
) : ControllerBase {
    
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ManageableGuildResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ManageableGuildResponse>>> Get(
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();

        if (appUserId is null) {
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        try {
            var response = await LoadManageableGuildsForUser(appUserId.Value, cancellationToken);
            return Ok(response);
        } catch (InvalidOperationException ex) {
            return Unauthorized(new ProblemDetails {
                Title = "Unable to load guilds.",
                Detail = ex.Message,
                Status = StatusCodes.Status401Unauthorized
            });
        } catch (HttpRequestException ex) {
            return Unauthorized(new ProblemDetails {
                Title = "Discord API error.",
                Detail = ex.Message,
                Status = StatusCodes.Status401Unauthorized
            });
        }
    }

    /// <summary>
    /// Returns the current settings for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The guild settings including spam configuration.</returns>
    /// <response code="200">Returns the guild settings.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/settings")]
    [ProducesResponseType(typeof(GuildSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GuildSettingsResponse>> GetSettings(
        long discordGuildId,
        CancellationToken cancellationToken)
    {
        var appUserId = GetCurrentUserId();

        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await guildClaimService.CanOpenGuildAsync(appUserId.Value, discordGuildId, cancellationToken);

        if (!canOpen)
            return Forbid();

        var instance = await dbContext.GuildInstances
            .Include(x => x.SpamConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(new GuildSettingsResponse(
            instance.DiscordGuildId,
            instance.Name,
            instance.IconHash,
            instance.IsActive,
            new SpamConfigurationDto(
                instance.SpamConfiguration.MaxMessagesPerWindow,
                instance.SpamConfiguration.RateLimitWindowSeconds,
                instance.SpamConfiguration.DuplicateMessageThreshold,
                instance.SpamConfiguration.MentionLimit,
                instance.SpamConfiguration.BlockInviteLinks,
                instance.SpamConfiguration.BlockSuspiciousLinks
            )
        ));
    }

    /// <summary>
    /// Updates the spam configuration for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The updated spam settings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Settings updated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPut("{discordGuildId:long}/settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings(
        long discordGuildId,
        [FromBody] UpdateGuildSettingsRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();

        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await guildClaimService.CanOpenGuildAsync(appUserId.Value, discordGuildId, cancellationToken);

        if (!canOpen)
            return Forbid();

        var instance = await dbContext.GuildInstances
            .Include(x => x.SpamConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var spam = instance.SpamConfiguration;
        spam.MaxMessagesPerWindow = request.MaxMessagesPerWindow;
        spam.RateLimitWindowSeconds = request.RateLimitWindowSeconds;
        spam.DuplicateMessageThreshold = request.DuplicateMessageThreshold;
        spam.MentionLimit = request.MentionLimit;
        spam.BlockInviteLinks = request.BlockInviteLinks;
        spam.BlockSuspiciousLinks = request.BlockSuspiciousLinks;
        spam.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await spamConfigurationCache.InvalidateAsync(discordGuildId);

        return NoContent();
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