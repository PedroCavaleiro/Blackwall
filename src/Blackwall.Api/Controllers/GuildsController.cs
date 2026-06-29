using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Bot.Services;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
using Discord;
using Discord.WebSocket;
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
    DiscordGuildCacheService guildCache,
    DiscordSocketClient discordClient,
    LockdownService lockdownService
) : ControllerBase {
    
    /// <summary>
    /// Returns all Discord guilds the authenticated user can manage.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of guilds the user can manage.</returns>
    /// <response code="200">Returns the list of manageable guilds.</response>
    /// <response code="401">The user identity could not be resolved from the JWT, the user no longer exists, or has no Discord access token.</response>
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

        return Ok(new GuildSettingsResponse(
            instance.DiscordGuildId,
            instance.Name,
            instance.IconHash,
            instance.IsActive,
            new SpamConfigurationDto(
                instance.SpamConfiguration.MaxMessagesPerWindow,
                instance.SpamConfiguration.RateLimitWindowSeconds,
                instance.SpamConfiguration.DuplicateMessageThreshold,
                instance.SpamConfiguration.DuplicateWindowSeconds,
                instance.SpamConfiguration.DuplicateCrossChannelEnabled,
                instance.SpamConfiguration.MentionLimit,
                instance.SpamConfiguration.BlockInviteLinks,
                instance.SpamConfiguration.BlockSuspiciousLinks,
                instance.SpamConfiguration.IsEnabled,
                instance.SpamConfiguration.IsDryRun,
                instance.SpamConfiguration.Action,
                instance.SpamConfiguration.LogChannelId,
                instance.SpamConfiguration.MessageDeleteDays,
                instance.SpamConfiguration.IsAntiRaidEnabled,
                instance.SpamConfiguration.AntiRaidJoinThreshold,
                instance.SpamConfiguration.AntiRaidWindowSeconds,
                instance.SpamConfiguration.AntiRaidCooldownMinutes,
                instance.SpamConfiguration.IsLockedDown
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
        spam.DuplicateWindowSeconds = Math.Clamp(request.DuplicateWindowSeconds, 1, 300);
        spam.DuplicateCrossChannelEnabled = request.DuplicateCrossChannelEnabled;
        spam.MentionLimit = request.MentionLimit;
        spam.BlockInviteLinks = request.BlockInviteLinks;
        spam.BlockSuspiciousLinks = request.BlockSuspiciousLinks;
        spam.IsEnabled = request.IsEnabled;
        spam.IsDryRun = request.IsDryRun;
        spam.Action = request.Action;
        spam.LogChannelId = request.LogChannelId;
        spam.MessageDeleteDays = Math.Clamp(request.MessageDeleteDays, 0, 7);
        spam.IsAntiRaidEnabled = request.IsAntiRaidEnabled;
        spam.AntiRaidJoinThreshold = Math.Max(2, request.AntiRaidJoinThreshold);
        spam.AntiRaidWindowSeconds = Math.Clamp(request.AntiRaidWindowSeconds, 5, 300);
        spam.AntiRaidCooldownMinutes = Math.Max(1, request.AntiRaidCooldownMinutes);
        spam.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await spamConfigurationCache.InvalidateAsync(discordGuildId);

        return NoContent();
    }

    /// <summary>
    /// Locks down the specified guild by denying Send Messages for @@everyone in all text channels
    /// and categories via channel-specific permission overwrites.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Lockdown activated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPost("{discordGuildId:long}/lockdown")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Lockdown(
        long discordGuildId,
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

        if (instance.SpamConfiguration.IsLockedDown)
            return BadRequest(new ProblemDetails {
                Title = "Already locked down.",
                Detail = "The guild is already in lockdown.",
                Status = StatusCodes.Status400BadRequest
            });

        await lockdownService.LockdownAsync((ulong)discordGuildId);

        return Ok(new { Message = "Lockdown activated." });
    }

    /// <summary>
    /// Lifts the lockdown by removing the @@everyone permission overwrites that were applied,
    /// returning those permissions to their inherited state.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Lockdown lifted successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPost("{discordGuildId:long}/unlock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlock(
        long discordGuildId,
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

        if (!instance.SpamConfiguration.IsLockedDown)
            return BadRequest(new ProblemDetails {
                Title = "Not locked down.",
                Detail = "The guild is not currently in lockdown.",
                Status = StatusCodes.Status400BadRequest
            });

        await lockdownService.UnlockAsync((ulong)discordGuildId);

        return Ok(new { Message = "Lockdown lifted." });
    }

    /// <summary>
    /// Returns all text channels visible to the bot in the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of text channels.</returns>
    /// <response code="200">Returns the list of channels.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild is not found or the bot is not connected to it.</response>
    [HttpGet("{discordGuildId:long}/channels")]
    [ProducesResponseType(typeof(IReadOnlyList<DiscordChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DiscordChannelDto>>> GetChannels(
        long discordGuildId,
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

        var guild = discordClient.GetGuild((ulong)discordGuildId);

        if (guild is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "The bot is not connected to this guild.",
                Status = StatusCodes.Status404NotFound
            });

        var channels = guild.TextChannels
            .OrderBy(c => c.Position)
            .Select(c => new DiscordChannelDto((long)c.Id, c.Name))
            .ToList();

        return Ok(channels);
    }

    /// <summary>
    /// Removes the bot from the specified Discord guild and deactivates the guild instance.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Bot removed and guild deactivated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpDelete("{discordGuildId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBot(
        long discordGuildId,
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
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var discordGuild = discordClient.GetGuild((ulong)discordGuildId);
        if (discordGuild is not null)
            await discordGuild.LeaveAsync();

        instance.IsActive = false;
        instance.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await spamConfigurationCache.InvalidateAsync(discordGuildId);

        return NoContent();
    }

    /// <summary>
    /// Extracts the current user's application ID from the JWT claims.
    /// </summary>
    /// <returns>The user's application ID, or <c>null</c> if the claim is missing or unparseable.</returns>
    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }

    /// <summary>
    /// Loads the list of Discord guilds that the specified user can manage.
    /// Uses a cache when available; otherwise fetches from the Discord API and populates the cache.
    /// </summary>
    /// <param name="appUserId">The application user ID to load guilds for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of guilds the user can manage.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the user does not exist or has no Discord access token.</exception>
    /// <exception cref="HttpRequestException">Thrown when the Discord API call fails.</exception>
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

        var cachedGuilds = await guildCache.GetAsync(appUserId);
        if (cachedGuilds is not null) {
            await guildClaimService.ClaimOwnershipAsync(appUserId, cachedGuilds, cancellationToken);
            return await guildClaimService.GetManageableGuildsAsync(appUserId, cachedGuilds, cancellationToken);
        }

        var accessToken = await discordOAuthService.EnsureFreshAccessTokenAsync(user, cancellationToken);
        var guilds = await discordOAuthService.GetCurrentUserGuildsAsync(accessToken, cancellationToken);
        await guildClaimService.ClaimOwnershipAsync(user.Id, guilds, cancellationToken);
        await guildCache.StoreAsync(appUserId, guilds);

        return await guildClaimService.GetManageableGuildsAsync(user.Id, guilds, cancellationToken);
    }
}