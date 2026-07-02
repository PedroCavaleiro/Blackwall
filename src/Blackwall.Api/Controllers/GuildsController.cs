using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Bot.Services;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
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
    LockdownService lockdownService,
    BlacklistService blacklistService,
    BanSyncService banSyncService
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
            instance.ShareBanList,
            new SpamConfigurationDto(
                instance.SpamConfiguration.MaxMessagesPerWindow,
                instance.SpamConfiguration.RateLimitWindowSeconds,
                instance.SpamConfiguration.DuplicateMessageThreshold,
                instance.SpamConfiguration.DuplicateWindowSeconds,
                instance.SpamConfiguration.DuplicateCrossChannelEnabled,
                instance.SpamConfiguration.MentionLimit,
                instance.SpamConfiguration.BlockInviteLinks,
                instance.SpamConfiguration.BlockSuspiciousLinks,
                instance.SpamConfiguration.LinkWhitelistMode,
                instance.SpamConfiguration.SafeBrowsingEnabled,
                instance.SpamConfiguration.SafeBrowsingBlockUnsure,
                instance.SpamConfiguration.IsEnabled,
                instance.SpamConfiguration.IsDryRun,
                instance.SpamConfiguration.LogChannelId,
                instance.SpamConfiguration.IsAntiRaidEnabled,
                instance.SpamConfiguration.AntiRaidJoinThreshold,
                instance.SpamConfiguration.AntiRaidWindowSeconds,
                instance.SpamConfiguration.AntiRaidCooldownMinutes,
                instance.SpamConfiguration.IsAccountScoringEnabled,
                instance.SpamConfiguration.AutoTimeoutMediumRiskOnJoin,
                instance.SpamConfiguration.AutoTimeoutHighRiskOnJoin,
                instance.SpamConfiguration.AccountScoringTimeoutMinutes,
                instance.SpamConfiguration.IsLockedDown,
                instance.SpamConfiguration.RateLimitAction,
                instance.SpamConfiguration.RateLimitAutoLockdown,
                instance.SpamConfiguration.RateLimitTimeoutMinutes,
                instance.SpamConfiguration.RateLimitMessageDeleteDays,
                instance.SpamConfiguration.DuplicateAction,
                instance.SpamConfiguration.DuplicateAutoLockdown,
                instance.SpamConfiguration.DuplicateTimeoutMinutes,
                instance.SpamConfiguration.DuplicateMessageDeleteDays,
                instance.SpamConfiguration.MentionLimitAction,
                instance.SpamConfiguration.MentionLimitAutoLockdown,
                instance.SpamConfiguration.MentionLimitTimeoutMinutes,
                instance.SpamConfiguration.MentionLimitMessageDeleteDays,
                instance.SpamConfiguration.InviteLinkAction,
                instance.SpamConfiguration.InviteLinkAutoLockdown,
                instance.SpamConfiguration.InviteLinkTimeoutMinutes,
                instance.SpamConfiguration.InviteLinkMessageDeleteDays,
                instance.SpamConfiguration.SuspiciousLinkAction,
                instance.SpamConfiguration.SuspiciousLinkAutoLockdown,
                instance.SpamConfiguration.SuspiciousLinkTimeoutMinutes,
                instance.SpamConfiguration.SuspiciousLinkMessageDeleteDays
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
        spam.LinkWhitelistMode = request.LinkWhitelistMode;
        spam.SafeBrowsingEnabled = request.SafeBrowsingEnabled;
        spam.SafeBrowsingBlockUnsure = request.SafeBrowsingBlockUnsure;
        spam.IsEnabled = request.IsEnabled;
        spam.IsDryRun = request.IsDryRun;
        spam.LogChannelId = request.LogChannelId;
        spam.IsAntiRaidEnabled = request.IsAntiRaidEnabled;
        spam.AntiRaidJoinThreshold = Math.Max(2, request.AntiRaidJoinThreshold);
        spam.AntiRaidWindowSeconds = Math.Clamp(request.AntiRaidWindowSeconds, 5, 300);
        spam.AntiRaidCooldownMinutes = Math.Max(1, request.AntiRaidCooldownMinutes);
        spam.IsAccountScoringEnabled = request.IsAccountScoringEnabled;
        spam.AutoTimeoutMediumRiskOnJoin = request.AutoTimeoutMediumRiskOnJoin;
        spam.AutoTimeoutHighRiskOnJoin = request.AutoTimeoutHighRiskOnJoin;
        spam.AccountScoringTimeoutMinutes = Math.Max(1, request.AccountScoringTimeoutMinutes);
        spam.RateLimitAction = request.RateLimitAction;
        spam.RateLimitAutoLockdown = request.RateLimitAutoLockdown;
        spam.RateLimitTimeoutMinutes = Math.Max(1, request.RateLimitTimeoutMinutes);
        spam.RateLimitMessageDeleteDays = Math.Clamp(request.RateLimitMessageDeleteDays, 0, 7);
        spam.DuplicateAction = request.DuplicateAction;
        spam.DuplicateAutoLockdown = request.DuplicateAutoLockdown;
        spam.DuplicateTimeoutMinutes = Math.Max(1, request.DuplicateTimeoutMinutes);
        spam.DuplicateMessageDeleteDays = Math.Clamp(request.DuplicateMessageDeleteDays, 0, 7);
        spam.MentionLimitAction = request.MentionLimitAction;
        spam.MentionLimitAutoLockdown = request.MentionLimitAutoLockdown;
        spam.MentionLimitTimeoutMinutes = Math.Max(1, request.MentionLimitTimeoutMinutes);
        spam.MentionLimitMessageDeleteDays = Math.Clamp(request.MentionLimitMessageDeleteDays, 0, 7);
        spam.InviteLinkAction = request.InviteLinkAction;
        spam.InviteLinkAutoLockdown = request.InviteLinkAutoLockdown;
        spam.InviteLinkTimeoutMinutes = Math.Max(1, request.InviteLinkTimeoutMinutes);
        spam.InviteLinkMessageDeleteDays = Math.Clamp(request.InviteLinkMessageDeleteDays, 0, 7);
        spam.SuspiciousLinkAction = request.SuspiciousLinkAction;
        spam.SuspiciousLinkAutoLockdown = request.SuspiciousLinkAutoLockdown;
        spam.SuspiciousLinkTimeoutMinutes = Math.Max(1, request.SuspiciousLinkTimeoutMinutes);
        spam.SuspiciousLinkMessageDeleteDays = Math.Clamp(request.SuspiciousLinkMessageDeleteDays, 0, 7);
        spam.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await spamConfigurationCache.InvalidateAsync(discordGuildId);
        _ = Task.Run(() => blacklistService.RefreshGuildAsync(discordGuildId, CancellationToken.None), cancellationToken);

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
    /// Returns the list of default blacklist URLs available from configuration.
    /// </summary>
    /// <returns>A list of default blacklist URLs.</returns>
    /// <response code="200">Returns the list of default blacklists.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    [HttpGet("blacklists/defaults")]
    [ProducesResponseType(typeof(IReadOnlyList<DefaultBlacklistResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<DefaultBlacklistResponse>>> GetDefaultBlacklists(
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();

        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        await Task.CompletedTask;
        return Ok(blacklistService.GetDefaultBlacklists()
            .Select(url => new DefaultBlacklistResponse(url))
            .ToList());
    }

    /// <summary>
    /// Returns all blacklists configured for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of blacklists configured for the guild.</returns>
    /// <response code="200">Returns the list of guild blacklists.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/blacklists")]
    [ProducesResponseType(typeof(IReadOnlyList<BlacklistResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BlacklistResponse>>> GetBlacklists(
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
            .Include(x => x.SpamConfiguration.Blacklists)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(instance.SpamConfiguration.Blacklists
            .Select(b => new BlacklistResponse(b.Id, b.Url))
            .ToList());
    }

    /// <summary>
    /// Adds a blacklist URL to the guild's configuration. The URL can be one of the defaults
    /// or a custom AdGuard-format blacklist URL provided by the user.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The blacklist URL to add.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Blacklist added successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    /// <response code="409">The blacklist URL is already configured for this guild.</response>
    [HttpPost("{discordGuildId:long}/blacklists")]
    [ProducesResponseType(typeof(BlacklistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BlacklistResponse>> AddBlacklist(
        long discordGuildId,
        [FromBody] AddBlacklistRequest request,
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
            .Include(x => x.SpamConfiguration.Blacklists)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return BadRequest(new ProblemDetails {
                Title = "Invalid URL.",
                Detail = "The blacklist URL must be a valid HTTP or HTTPS URL.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.SpamConfiguration.Blacklists.Any(b => b.Url.Equals(request.Url, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ProblemDetails {
                Title = "Blacklist already configured.",
                Detail = "This blacklist URL is already configured for this guild.",
                Status = StatusCodes.Status409Conflict
            });

        var blacklist = new GuildBlacklist {
            SpamConfigurationId = instance.SpamConfiguration.Id,
            Url = request.Url
        };

        instance.SpamConfiguration.Blacklists.Add(blacklist);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => blacklistService.RefreshGuildAsync(discordGuildId, CancellationToken.None), cancellationToken);

        return Ok(new BlacklistResponse(blacklist.Id, blacklist.Url));
    }

    /// <summary>
    /// Removes a blacklist URL from the guild's configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="blacklistId">The ID of the blacklist to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Blacklist removed successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or blacklist does not exist.</response>
    [HttpDelete("{discordGuildId:long}/blacklists/{blacklistId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBlacklist(
        long discordGuildId,
        long blacklistId,
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
            .Include(x => x.SpamConfiguration.Blacklists)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var blacklist = instance.SpamConfiguration.Blacklists.FirstOrDefault(b => b.Id == blacklistId);
        if (blacklist is null)
            return NotFound(new ProblemDetails {
                Title = "Blacklist not found.",
                Detail = "No blacklist with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        instance.SpamConfiguration.Blacklists.Remove(blacklist);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => blacklistService.RefreshGuildAsync(discordGuildId, CancellationToken.None), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Manually triggers a refresh of all blacklists for the specified guild,
    /// re-downloading and updating the Redis domain set.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Blacklists refreshed successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPost("{discordGuildId:long}/blacklists/refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshBlacklists(
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

        await blacklistService.RefreshGuildAsync(discordGuildId, cancellationToken);

        return Ok(new { Message = "Blacklists refreshed." });
    }

    /// <summary>
    /// Returns all custom domains configured for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of custom domains configured for the guild.</returns>
    /// <response code="200">Returns the list of custom domains.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/blacklists/domains")]
    [ProducesResponseType(typeof(IReadOnlyList<BlacklistDomainResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BlacklistDomainResponse>>> GetBlacklistDomains(
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
            .Include(x => x.SpamConfiguration.BlacklistDomains)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(instance.SpamConfiguration.BlacklistDomains
            .Select(d => new BlacklistDomainResponse(d.Id, d.Domain))
            .ToList());
    }

    /// <summary>
    /// Adds a custom domain to the guild's configuration. In blacklist mode, this domain
    /// is treated as blocked. In whitelist mode, this domain is treated as allowed.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The domain to add.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Domain added successfully.</response>
    /// <response code="400">The domain is invalid.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    /// <response code="409">The domain is already configured for this guild.</response>
    [HttpPost("{discordGuildId:long}/blacklists/domains")]
    [ProducesResponseType(typeof(BlacklistDomainResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BlacklistDomainResponse>> AddBlacklistDomain(
        long discordGuildId,
        [FromBody] AddBlacklistDomainRequest request,
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
            .Include(x => x.SpamConfiguration.BlacklistDomains)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var domain = request.Domain.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain) || domain.Contains(' ') || domain.Contains('/'))
            return BadRequest(new ProblemDetails {
                Title = "Invalid domain.",
                Detail = "The domain must be a valid hostname without protocol or path.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.SpamConfiguration.BlacklistDomains.Any(d => d.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ProblemDetails {
                Title = "Domain already configured.",
                Detail = "This domain is already configured for this guild.",
                Status = StatusCodes.Status409Conflict
            });

        var entry = new GuildBlacklistDomain {
            SpamConfigurationId = instance.SpamConfiguration.Id,
            Domain = domain
        };

        instance.SpamConfiguration.BlacklistDomains.Add(entry);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => blacklistService.RefreshGuildAsync(discordGuildId, CancellationToken.None), cancellationToken);

        return Ok(new BlacklistDomainResponse(entry.Id, entry.Domain));
    }

    /// <summary>
    /// Removes a custom domain from the guild's configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="domainId">The ID of the domain entry to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Domain removed successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or domain does not exist.</response>
    [HttpDelete("{discordGuildId:long}/blacklists/domains/{domainId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBlacklistDomain(
        long discordGuildId,
        long domainId,
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
            .Include(x => x.SpamConfiguration.BlacklistDomains)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var entry = instance.SpamConfiguration.BlacklistDomains.FirstOrDefault(d => d.Id == domainId);
        if (entry is null)
            return NotFound(new ProblemDetails {
                Title = "Domain not found.",
                Detail = "No domain with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        instance.SpamConfiguration.BlacklistDomains.Remove(entry);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => blacklistService.RefreshGuildAsync(discordGuildId, CancellationToken.None));

        return NoContent();
    }

    /// <summary>
    /// Updates the ban list sharing preference for a guild.
    /// When enabled, the guild's ban list becomes visible to other guilds managed by the bot.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The share ban list preference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Share preference updated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPut("{discordGuildId:long}/bans/share")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateShareBanList(
        long discordGuildId,
        [FromBody] UpdateShareBanListRequest request,
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

        instance.ShareBanList = request.ShareBanList;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Returns all guilds that have ban list sharing enabled and are active.
    /// Excludes the guild making the request.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID of the requesting guild.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of guilds with shared ban lists.</returns>
    /// <response code="200">Returns the list of shared ban list guilds.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpGet("{discordGuildId:long}/bans/shared-guilds")]
    [ProducesResponseType(typeof(IReadOnlyList<SharedBanListGuildResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SharedBanListGuildResponse>>> GetSharedBanListGuilds(
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

        var sharedGuilds = await dbContext.GuildInstances
            .Where(x => x.IsActive && x.ShareBanList && x.DiscordGuildId != discordGuildId)
            .Select(x => new {
                x.DiscordGuildId,
                x.Name,
                x.IconHash,
                BanCount = x.Bans.Count
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(sharedGuilds
            .Select(x => new SharedBanListGuildResponse(x.DiscordGuildId, x.Name, x.IconHash, x.BanCount))
            .ToList());
    }

    /// <summary>
    /// Returns all bans for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of bans for the guild.</returns>
    /// <response code="200">Returns the list of bans.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/bans")]
    [ProducesResponseType(typeof(IReadOnlyList<GuildBanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<GuildBanResponse>>> GetBans(
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
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(instance.Bans
            .Select(b => new GuildBanResponse(b.Id, b.DiscordUserId, b.Username, b.Reason, b.BannedAtUtc))
            .OrderByDescending(b => b.BannedAtUtc)
            .ToList());
    }

    /// <summary>
    /// Returns the bans of a shared guild. The source guild must have ban list sharing enabled.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID of the requesting guild.</param>
    /// <param name="sourceDiscordGuildId">The Discord guild ID whose bans to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of bans from the source guild.</returns>
    /// <response code="200">Returns the list of bans from the source guild.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild, or the source guild does not share its ban list.</response>
    /// <response code="404">The source guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/bans/source/{sourceDiscordGuildId:long}")]
    [ProducesResponseType(typeof(IReadOnlyList<GuildBanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<GuildBanResponse>>> GetSourceGuildBans(
        long discordGuildId,
        long sourceDiscordGuildId,
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

        var sourceInstance = await dbContext.GuildInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == sourceDiscordGuildId && x.IsActive, cancellationToken);

        if (sourceInstance is null)
            return NotFound(new ProblemDetails {
                Title = "Source guild not found.",
                Detail = "No guild instance exists for the source Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (!sourceInstance.ShareBanList)
            return Forbid();

        return Ok(sourceInstance.Bans
            .Select(b => new GuildBanResponse(b.Id, b.DiscordUserId, b.Username, b.Reason, b.BannedAtUtc))
            .OrderByDescending(b => b.BannedAtUtc)
            .ToList());
    }

    /// <summary>
    /// Synchronizes the ban list for the specified guild from Discord into the database.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Bans synced successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPost("{discordGuildId:long}/bans/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncBans(
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

        var count = await banSyncService.SyncBansAsync(discordGuildId, cancellationToken);

        return Ok(new { Message = $"Synced {count} bans.", Count = count });
    }

    /// <summary>
    /// Imports bans from a shared guild into the specified guild. Only imports from guilds
    /// that have ban list sharing enabled. Users already banned in the target guild are skipped.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID to import bans into.</param>
    /// <param name="request">The import request specifying the source guild and optional user IDs.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Bans imported successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPost("{discordGuildId:long}/bans/import")]
    [ProducesResponseType(typeof(ImportBansResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImportBansResultResponse>> ImportBans(
        long discordGuildId,
        [FromBody] ImportBansRequest request,
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

        if (request.SourceDiscordGuildId == discordGuildId)
            return BadRequest(new ProblemDetails {
                Title = "Cannot import from self.",
                Detail = "The source guild cannot be the same as the target guild.",
                Status = StatusCodes.Status400BadRequest
            });

        var (imported, skipped, failed, errors) = await banSyncService.ImportBansAsync(
            discordGuildId, request.SourceDiscordGuildId, request.DiscordUserIds, cancellationToken);

        return Ok(new ImportBansResultResponse(imported, skipped, failed, errors));
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