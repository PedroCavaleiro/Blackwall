using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Bot.Services;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Blackwall.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public sealed class GuildsController(
    BlackwallDbContext dbContext,
    DiscordOAuthService discordOAuthService,
    GuildClaimService guildClaimService,
    SpamConfigurationCache spamConfigurationCache,
    NetWatchSnareChannelCache netWatchSnareChannelCache,
    DiscordGuildCacheService guildCache,
    DiscordSocketClient discordClient,
    LockdownService lockdownService,
    BlacklistService blacklistService,
    BanSyncService banSyncService,
    AllowedBotService allowedBotService,
    AiSentinelCache aiSentinelCache,
    AiSentinelService aiSentinelService,
    IOptions<AppConfiguration> appConfiguration
) : ControllerBase {

    private (byte[] Key, byte[] Iv) GetCryptoParams() {
        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
        return (key, iv);
    }

    private static string? Encrypt(string? plainText, byte[] key, byte[] iv) {
        if (string.IsNullOrWhiteSpace(plainText))
            return plainText;
        return AesCrypto.EncryptString(plainText, key, iv);
    }

    private static string? Decrypt(string? cipherText, byte[] key, byte[] iv) {
        if (string.IsNullOrWhiteSpace(cipherText))
            return cipherText;
        try {
            return AesCrypto.DecryptString(cipherText, key, iv);
        } catch {
            return cipherText;
        }
    }
    
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
                instance.SpamConfiguration.IsTestMode,
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
                instance.SpamConfiguration.SuspiciousLinkMessageDeleteDays,
                instance.SpamConfiguration.IsContentGuardEnabled,
                instance.SpamConfiguration.ContentGuardFuzzyMatching,
                instance.SpamConfiguration.ContentGuardInvisibleCharScrubbing,
                instance.SpamConfiguration.ContentGuardZalgoBlocking,
                instance.SpamConfiguration.ContentGuardCopypastaHashing,
                instance.SpamConfiguration.ContentGuardFuzzyThreshold,
                instance.SpamConfiguration.ContentGuardZalgoMaxCombining,
                instance.SpamConfiguration.ContentGuardCopypastaMinLength,
                instance.SpamConfiguration.ContentGuardCopypastaThreshold,
                instance.SpamConfiguration.ContentGuardCopypastaWindowSeconds,
                instance.SpamConfiguration.ContentGuardAction,
                instance.SpamConfiguration.ContentGuardAutoLockdown,
                instance.SpamConfiguration.ContentGuardTimeoutMinutes,
                instance.SpamConfiguration.ContentGuardMessageDeleteDays,
                instance.SpamConfiguration.IsMessageAuditEnabled,
                instance.SpamConfiguration.MessageAuditRetentionDays
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
        spam.IsTestMode = request.IsTestMode;
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
        spam.IsContentGuardEnabled = request.IsContentGuardEnabled;
        spam.ContentGuardFuzzyMatching = request.ContentGuardFuzzyMatching;
        spam.ContentGuardInvisibleCharScrubbing = request.ContentGuardInvisibleCharScrubbing;
        spam.ContentGuardZalgoBlocking = request.ContentGuardZalgoBlocking;
        spam.ContentGuardCopypastaHashing = request.ContentGuardCopypastaHashing;
        spam.ContentGuardFuzzyThreshold = Math.Clamp(request.ContentGuardFuzzyThreshold, 1, 5);
        spam.ContentGuardZalgoMaxCombining = Math.Clamp(request.ContentGuardZalgoMaxCombining, 1, 10);
        spam.ContentGuardCopypastaMinLength = Math.Clamp(request.ContentGuardCopypastaMinLength, 50, 5000);
        spam.ContentGuardCopypastaThreshold = Math.Max(2, request.ContentGuardCopypastaThreshold);
        spam.ContentGuardCopypastaWindowSeconds = Math.Clamp(request.ContentGuardCopypastaWindowSeconds, 10, 3600);
        spam.ContentGuardAction = request.ContentGuardAction;
        spam.ContentGuardAutoLockdown = request.ContentGuardAutoLockdown;
        spam.ContentGuardTimeoutMinutes = Math.Max(1, request.ContentGuardTimeoutMinutes);
        spam.ContentGuardMessageDeleteDays = Math.Clamp(request.ContentGuardMessageDeleteDays, 0, 7);
        spam.IsMessageAuditEnabled = request.IsMessageAuditEnabled;
        spam.MessageAuditRetentionDays = Math.Clamp(request.MessageAuditRetentionDays, 7, 90);
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
    /// Returns all auto-sync rules for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Returns the list of auto-sync rules.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpGet("{discordGuildId:long}/bans/auto-sync")]
    [ProducesResponseType(typeof(IReadOnlyList<BanSyncRuleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<BanSyncRuleResponse>>> GetBanSyncRules(
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
            .Include(x => x.BanSyncRules)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var sourceGuildIds = instance.BanSyncRules.Select(r => r.SourceDiscordGuildId).ToHashSet();
        var sourceGuilds = await dbContext.GuildInstances
            .Where(x => sourceGuildIds.Contains(x.DiscordGuildId))
            .ToDictionaryAsync(x => x.DiscordGuildId, cancellationToken);

        return Ok(instance.BanSyncRules
            .Select(r => new BanSyncRuleResponse(
                r.Id,
                r.SourceDiscordGuildId,
                sourceGuilds.TryGetValue(r.SourceDiscordGuildId, out var src) ? src.Name : "Unknown",
                r.IsEnabled,
                r.LastSyncedAtUtc == DateTime.MinValue ? null : r.LastSyncedAtUtc
            ))
            .OrderBy(r => r.SourceGuildName)
            .ToList());
    }

    /// <summary>
    /// Adds a new auto-sync rule for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The auto-sync rule to add.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Auto-sync rule added successfully.</response>
    /// <response code="400">The source guild does not have ban list sharing enabled or is the same as the target.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPost("{discordGuildId:long}/bans/auto-sync")]
    [ProducesResponseType(typeof(BanSyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BanSyncRuleResponse>> AddBanSyncRule(
        long discordGuildId,
        [FromBody] AddBanSyncRuleRequest request,
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

        if (request.SourceDiscordGuildId == discordGuildId)
            return BadRequest(new ProblemDetails {
                Title = "Cannot auto-sync from self.",
                Detail = "The source guild cannot be the same as the target guild.",
                Status = StatusCodes.Status400BadRequest
            });

        var instance = await dbContext.GuildInstances
            .Include(x => x.BanSyncRules)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var sourceGuild = await dbContext.GuildInstances
            .FirstOrDefaultAsync(x => x.DiscordGuildId == request.SourceDiscordGuildId && x.IsActive, cancellationToken);

        if (sourceGuild is null)
            return BadRequest(new ProblemDetails {
                Title = "Source guild not found.",
                Detail = "The source guild does not exist or is not active.",
                Status = StatusCodes.Status400BadRequest
            });

        if (!sourceGuild.ShareBanList)
            return BadRequest(new ProblemDetails {
                Title = "Ban list not shared.",
                Detail = "The source guild does not have ban list sharing enabled.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.BanSyncRules.Any(r => r.SourceDiscordGuildId == request.SourceDiscordGuildId))
            return BadRequest(new ProblemDetails {
                Title = "Rule already exists.",
                Detail = "An auto-sync rule for this source guild already exists.",
                Status = StatusCodes.Status400BadRequest
            });

        var rule = new GuildBanSyncRule {
            TargetGuildInstanceId = instance.Id,
            SourceDiscordGuildId = request.SourceDiscordGuildId,
            IsEnabled = true
        };

        dbContext.GuildBanSyncRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new BanSyncRuleResponse(rule.Id, rule.SourceDiscordGuildId, sourceGuild.Name, rule.IsEnabled, null));
    }

    /// <summary>
    /// Updates an existing auto-sync rule (e.g. enable/disable).
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ruleId">The ID of the auto-sync rule to update.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Auto-sync rule updated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The rule does not exist.</response>
    [HttpPut("{discordGuildId:long}/bans/auto-sync/{ruleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBanSyncRule(
        long discordGuildId,
        long ruleId,
        [FromBody] UpdateBanSyncRuleRequest request,
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

        var rule = await dbContext.GuildBanSyncRules
            .FirstOrDefaultAsync(x => x.Id == ruleId && x.TargetGuildInstanceId == instance.Id, cancellationToken);

        if (rule is null)
            return NotFound(new ProblemDetails {
                Title = "Auto-sync rule not found.",
                Detail = "No auto-sync rule exists with the specified ID.",
                Status = StatusCodes.Status404NotFound
            });

        rule.IsEnabled = request.IsEnabled;
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deletes an auto-sync rule.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ruleId">The ID of the auto-sync rule to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Auto-sync rule deleted successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The rule does not exist.</response>
    [HttpDelete("{discordGuildId:long}/bans/auto-sync/{ruleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBanSyncRule(
        long discordGuildId,
        long ruleId,
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

        var rule = await dbContext.GuildBanSyncRules
            .FirstOrDefaultAsync(x => x.Id == ruleId && x.TargetGuildInstanceId == instance.Id, cancellationToken);

        if (rule is null)
            return NotFound(new ProblemDetails {
                Title = "Auto-sync rule not found.",
                Detail = "No auto-sync rule exists with the specified ID.",
                Status = StatusCodes.Status404NotFound
            });

        dbContext.GuildBanSyncRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Returns all banned words configured for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of banned words.</returns>
    /// <response code="200">Returns the list of banned words.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpGet("{discordGuildId:long}/banned-words")]
    [ProducesResponseType(typeof(IReadOnlyList<BannedWordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<BannedWordResponse>>> GetBannedWords(
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

        var words = await dbContext.GuildBannedWords
            .Where(x => x.SpamConfiguration.GuildInstance.DiscordGuildId == discordGuildId)
            .Select(x => new BannedWordResponse(x.Id, x.Word))
            .ToListAsync(cancellationToken);

        return Ok(words);
    }

    /// <summary>
    /// Adds a banned word to the guild's Content Guard configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The word to ban.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Word added successfully.</response>
    /// <response code="400">The word is invalid.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    /// <response code="409">The word is already configured for this guild.</response>
    [HttpPost("{discordGuildId:long}/banned-words")]
    [ProducesResponseType(typeof(BannedWordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BannedWordResponse>> AddBannedWord(
        long discordGuildId,
        [FromBody] AddBannedWordRequest request,
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
            .Include(x => x.SpamConfiguration.BannedWords)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var word = request.Word.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(word) || word.Length > 100)
            return BadRequest(new ProblemDetails {
                Title = "Invalid word.",
                Detail = "The word must be between 1 and 100 characters.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.SpamConfiguration.BannedWords.Any(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ProblemDetails {
                Title = "Word already configured.",
                Detail = "This word is already in the banned words list for this guild.",
                Status = StatusCodes.Status409Conflict
            });

        var entry = new GuildBannedWord {
            SpamConfigurationId = instance.SpamConfiguration.Id,
            Word = word
        };

        instance.SpamConfiguration.BannedWords.Add(entry);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await spamConfigurationCache.InvalidateAsync(discordGuildId);

        return Ok(new BannedWordResponse(entry.Id, entry.Word));
    }

    /// <summary>
    /// Removes a banned word from the guild's Content Guard configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="wordId">The ID of the banned word entry to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Word removed successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or word does not exist.</response>
    [HttpDelete("{discordGuildId:long}/banned-words/{wordId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBannedWord(
        long discordGuildId,
        long wordId,
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
            .Include(x => x.SpamConfiguration.BannedWords)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var entry = instance.SpamConfiguration.BannedWords.FirstOrDefault(w => w.Id == wordId);
        if (entry is null)
            return NotFound(new ProblemDetails {
                Title = "Banned word not found.",
                Detail = "No banned word with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        instance.SpamConfiguration.BannedWords.Remove(entry);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await spamConfigurationCache.InvalidateAsync(discordGuildId);

        return NoContent();
    }

    /// <summary>
    /// Returns all allowed bots configured for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of allowed bots configured for the guild.</returns>
    /// <response code="200">Returns the list of allowed bots.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/allowed-bots")]
    [ProducesResponseType(typeof(IReadOnlyList<AllowedBotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AllowedBotResponse>>> GetAllowedBots(
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
            .Include(x => x.SpamConfiguration.AllowedBots)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(instance.SpamConfiguration.AllowedBots
            .Select(b => new AllowedBotResponse(b.Id, b.DiscordBotId, b.BotUsername))
            .ToList());
    }

    /// <summary>
    /// Adds a bot to the guild's allowed bots list. Messages from this bot will be
    /// skipped by the spam detection pipeline.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The bot to add.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Bot added successfully.</response>
    /// <response code="400">The bot ID is invalid.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    /// <response code="409">The bot is already configured for this guild.</response>
    [HttpPost("{discordGuildId:long}/allowed-bots")]
    [ProducesResponseType(typeof(AllowedBotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AllowedBotResponse>> AddAllowedBot(
        long discordGuildId,
        [FromBody] AddAllowedBotRequest request,
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
            .Include(x => x.SpamConfiguration.AllowedBots)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (request.DiscordBotId <= 0)
            return BadRequest(new ProblemDetails {
                Title = "Invalid bot ID.",
                Detail = "The bot ID must be a valid Discord snowflake.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.SpamConfiguration.AllowedBots.Any(b => b.DiscordBotId == request.DiscordBotId))
            return Conflict(new ProblemDetails {
                Title = "Bot already allowed.",
                Detail = "This bot is already on the allowed list for this guild.",
                Status = StatusCodes.Status409Conflict
            });

        var entry = new GuildAllowedBot {
            SpamConfigurationId = instance.SpamConfiguration.Id,
            DiscordBotId = request.DiscordBotId,
            BotUsername = string.IsNullOrWhiteSpace(request.BotUsername) ? request.DiscordBotId.ToString() : request.BotUsername.Trim()
        };

        instance.SpamConfiguration.AllowedBots.Add(entry);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => allowedBotService.RefreshGuildAsync(discordGuildId, CancellationToken.None), cancellationToken);

        return Ok(new AllowedBotResponse(entry.Id, entry.DiscordBotId, entry.BotUsername));
    }

    /// <summary>
    /// Removes a bot from the guild's allowed bots list.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="botId">The ID of the allowed bot entry to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Bot removed successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or bot entry does not exist.</response>
    [HttpDelete("{discordGuildId:long}/allowed-bots/{botId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAllowedBot(
        long discordGuildId,
        long botId,
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
            .Include(x => x.SpamConfiguration.AllowedBots)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var entry = instance.SpamConfiguration.AllowedBots.FirstOrDefault(b => b.Id == botId);
        if (entry is null)
            return NotFound(new ProblemDetails {
                Title = "Allowed bot not found.",
                Detail = "No allowed bot with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        instance.SpamConfiguration.AllowedBots.Remove(entry);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => allowedBotService.RefreshGuildAsync(discordGuildId, CancellationToken.None), cancellationToken);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  MESSAGE AUDIT
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of message audit events for a guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The page size (max 100).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Returns the list of audit event summaries.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpGet("{discordGuildId:long}/audit/events")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageAuditEventSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<MessageAuditEventSummaryDto>>> GetAuditEvents(
        long discordGuildId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default
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

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var instance = await dbContext.GuildInstances
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var events = await dbContext.MessageAuditEvents
            .Where(e => e.GuildInstanceId == instance.Id)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new MessageAuditEventSummaryDto(
                e.Id,
                e.DiscordUserId,
                e.Username,
                e.DiscordChannelId,
                e.ChannelName,
                e.Violations,
                e.Action,
                e.IsDryRun,
                e.CreatedAtUtc,
                e.Records.Count
            ))
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    /// <summary>
    /// Returns the full detail of a single audit event, including all associated messages.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="eventId">The audit event ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">Returns the audit event detail with messages.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The audit event does not exist.</response>
    [HttpGet("{discordGuildId:long}/audit/events/{eventId:long}")]
    [ProducesResponseType(typeof(MessageAuditEventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessageAuditEventDetailDto>> GetAuditEventDetail(
        long discordGuildId,
        long eventId,
        CancellationToken cancellationToken = default
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

        var evt = await dbContext.MessageAuditEvents
            .Where(e => e.Id == eventId && e.GuildInstance.DiscordGuildId == discordGuildId)
            .Select(e => new {
                e.Id,
                e.DiscordUserId,
                e.Username,
                e.AvatarHash,
                e.DiscordChannelId,
                e.ChannelName,
                e.Violations,
                e.Action,
                e.IsDryRun,
                e.CreatedAtUtc,
                Records = e.Records.Select(r => new MessageAuditMessageDto(
                    r.Id,
                    r.DiscordMessageId,
                    r.DiscordUserId,
                    r.Username,
                    r.DiscordChannelId,
                    r.ChannelName,
                    r.Content,
                    System.Text.Json.JsonSerializer.Deserialize<List<EmbedDataDto>>(r.EmbedsJson) ?? new List<EmbedDataDto>(),
                    r.MessageTimestampUtc
                )).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (evt is null)
            return NotFound(new ProblemDetails {
                Title = "Audit event not found.",
                Detail = "No audit event with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(new MessageAuditEventDetailDto(
            evt.Id,
            evt.DiscordUserId,
            evt.Username,
            evt.AvatarHash,
            evt.DiscordChannelId,
            evt.ChannelName,
            evt.Violations,
            evt.Action,
            evt.IsDryRun,
            evt.CreatedAtUtc,
            evt.Records
        ));
    }

    /// <summary>
    /// Deletes an entire audit event and all its associated message records.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="eventId">The audit event ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Audit event deleted successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The audit event does not exist.</response>
    [HttpDelete("{discordGuildId:long}/audit/events/{eventId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAuditEvent(
        long discordGuildId,
        long eventId,
        CancellationToken cancellationToken = default
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

        var evt = await dbContext.MessageAuditEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.GuildInstance.DiscordGuildId == discordGuildId, cancellationToken);

        if (evt is null)
            return NotFound(new ProblemDetails {
                Title = "Audit event not found.",
                Detail = "No audit event with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        dbContext.MessageAuditEvents.Remove(evt);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deletes a single message audit record from an event.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="eventId">The audit event ID.</param>
    /// <param name="recordId">The audit record ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Audit record deleted successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The audit record does not exist.</response>
    [HttpDelete("{discordGuildId:long}/audit/events/{eventId:long}/records/{recordId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAuditRecord(
        long discordGuildId,
        long eventId,
        long recordId,
        CancellationToken cancellationToken = default
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

        var record = await dbContext.MessageAuditRecords
            .FirstOrDefaultAsync(r => r.Id == recordId && r.EventId == eventId
                && r.Event.GuildInstance.DiscordGuildId == discordGuildId, cancellationToken);

        if (record is null)
            return NotFound(new ProblemDetails {
                Title = "Audit record not found.",
                Detail = "No audit record with this ID exists for this event.",
                Status = StatusCodes.Status404NotFound
            });

        dbContext.MessageAuditRecords.Remove(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  NETWATCH SNARE CHANNELS (TRAP CHANNELS)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all netWatchSnare (trap) channels configured for a guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of netWatchSnare channel configurations.</returns>
    /// <response code="200">Returns the list of netWatchSnare channels.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpGet("{discordGuildId:long}/netWatchSnares")]
    [ProducesResponseType(typeof(IReadOnlyList<NetWatchSnareChannelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NetWatchSnareChannelDto>>> GetNetWatchSnareChannels(
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
            .Include(x => x.SpamConfiguration.NetWatchSnareChannels)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(instance.SpamConfiguration.NetWatchSnareChannels
            .Select(s => new NetWatchSnareChannelDto(
                s.Id,
                s.DiscordChannelId,
                s.ChannelName,
                s.Action,
                s.TimeoutMinutes,
                s.MessageDeleteDays,
                s.IsEnabled
            ))
            .ToList());
    }

    /// <summary>
    /// Creates a new netWatchSnare (trap) channel for the guild.
    /// When a user sends a message in the designated channel, the configured action is applied automatically.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The netWatchSnare channel configuration to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">NetWatchSnare channel created successfully.</response>
    /// <response code="400">The channel ID is invalid or the channel does not exist.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    /// <response code="409">A netWatchSnare already exists for this channel.</response>
    [HttpPost("{discordGuildId:long}/netWatchSnares")]
    [ProducesResponseType(typeof(NetWatchSnareChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NetWatchSnareChannelDto>> CreateNetWatchSnareChannel(
        long discordGuildId,
        [FromBody] CreateNetWatchSnareChannelRequest request,
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
            .Include(x => x.SpamConfiguration.NetWatchSnareChannels)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (request.DiscordChannelId <= 0)
            return BadRequest(new ProblemDetails {
                Title = "Invalid channel ID.",
                Detail = "The channel ID must be a valid Discord snowflake.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.SpamConfiguration.NetWatchSnareChannels.Any(s => s.DiscordChannelId == request.DiscordChannelId))
            return Conflict(new ProblemDetails {
                Title = "NetWatchSnare already exists.",
                Detail = "A netWatchSnare channel is already configured for this Discord channel.",
                Status = StatusCodes.Status409Conflict
            });

        var guild = discordClient.GetGuild((ulong)discordGuildId);
        string channelName = request.ChannelName;
        if (guild is not null) {
            var channel = guild.GetTextChannel((ulong)request.DiscordChannelId);
            if (channel is null)
                return BadRequest(new ProblemDetails {
                    Title = "Channel not found.",
                    Detail = "The bot cannot see the specified channel. Ensure it is a text channel in this guild.",
                    Status = StatusCodes.Status400BadRequest
                });
            channelName = channel.Name;
        }

        var netWatchSnare = new NetWatchSnareChannel {
            SpamConfigurationId = instance.SpamConfiguration.Id,
            DiscordChannelId = request.DiscordChannelId,
            ChannelName = channelName,
            Action = request.Action,
            TimeoutMinutes = Math.Max(1, request.TimeoutMinutes),
            MessageDeleteDays = Math.Clamp(request.MessageDeleteDays, 0, 7),
            IsEnabled = true
        };

        instance.SpamConfiguration.NetWatchSnareChannels.Add(netWatchSnare);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await netWatchSnareChannelCache.InvalidateAsync(discordGuildId);

        return Ok(new NetWatchSnareChannelDto(
            netWatchSnare.Id,
            netWatchSnare.DiscordChannelId,
            netWatchSnare.ChannelName,
            netWatchSnare.Action,
            netWatchSnare.TimeoutMinutes,
            netWatchSnare.MessageDeleteDays,
            netWatchSnare.IsEnabled
        ));
    }

    /// <summary>
    /// Updates an existing netWatchSnare (trap) channel configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="netWatchSnareId">The ID of the netWatchSnare channel to update.</param>
    /// <param name="request">The updated configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="200">NetWatchSnare channel updated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or netWatchSnare channel does not exist.</response>
    [HttpPut("{discordGuildId:long}/netWatchSnares/{netWatchSnareId:long}")]
    [ProducesResponseType(typeof(NetWatchSnareChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetWatchSnareChannelDto>> UpdateNetWatchSnareChannel(
        long discordGuildId,
        long netWatchSnareId,
        [FromBody] UpdateNetWatchSnareChannelRequest request,
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
            .Include(x => x.SpamConfiguration.NetWatchSnareChannels)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var netWatchSnare = instance.SpamConfiguration.NetWatchSnareChannels.FirstOrDefault(s => s.Id == netWatchSnareId);
        if (netWatchSnare is null)
            return NotFound(new ProblemDetails {
                Title = "NetWatchSnare not found.",
                Detail = "No netWatchSnare channel with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        netWatchSnare.Action = request.Action;
        netWatchSnare.TimeoutMinutes = Math.Max(1, request.TimeoutMinutes);
        netWatchSnare.MessageDeleteDays = Math.Clamp(request.MessageDeleteDays, 0, 7);
        netWatchSnare.IsEnabled = request.IsEnabled;
        netWatchSnare.UpdatedAtUtc = DateTime.UtcNow;
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await netWatchSnareChannelCache.InvalidateAsync(discordGuildId);

        return Ok(new NetWatchSnareChannelDto(
            netWatchSnare.Id,
            netWatchSnare.DiscordChannelId,
            netWatchSnare.ChannelName,
            netWatchSnare.Action,
            netWatchSnare.TimeoutMinutes,
            netWatchSnare.MessageDeleteDays,
            netWatchSnare.IsEnabled
        ));
    }

    /// <summary>
    /// Deletes a netWatchSnare (trap) channel configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="netWatchSnareId">The ID of the netWatchSnare channel to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">NetWatchSnare channel deleted successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or netWatchSnare channel does not exist.</response>
    [HttpDelete("{discordGuildId:long}/netWatchSnares/{netWatchSnareId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNetWatchSnareChannel(
        long discordGuildId,
        long netWatchSnareId,
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
            .Include(x => x.SpamConfiguration.NetWatchSnareChannels)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var netWatchSnare = instance.SpamConfiguration.NetWatchSnareChannels.FirstOrDefault(s => s.Id == netWatchSnareId);
        if (netWatchSnare is null)
            return NotFound(new ProblemDetails {
                Title = "NetWatchSnare not found.",
                Detail = "No netWatchSnare channel with this ID exists for this guild.",
                Status = StatusCodes.Status404NotFound
            });

        instance.SpamConfiguration.NetWatchSnareChannels.Remove(netWatchSnare);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await netWatchSnareChannelCache.InvalidateAsync(discordGuildId);

        return NoContent();
    }

    /// <summary>
    /// Deletes all netWatchSnare (trap) channels for the guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">All netWatchSnare channels deleted successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpDelete("{discordGuildId:long}/netWatchSnares")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAllNetWatchSnareChannels(
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
            .Include(x => x.SpamConfiguration.NetWatchSnareChannels)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        dbContext.NetWatchSnareChannels.RemoveRange(instance.SpamConfiguration.NetWatchSnareChannels);
        instance.SpamConfiguration.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await netWatchSnareChannelCache.InvalidateAsync(discordGuildId);

        return NoContent();
    }

    /// <summary>
    /// Returns the AI Sentinel configuration for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The AI Sentinel configuration.</returns>
    /// <response code="200">Returns the AI Sentinel configuration.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance or AI Sentinel configuration does not exist.</response>
    [HttpGet("{discordGuildId:long}/ai-sentinel/config")]
    [ProducesResponseType(typeof(AiSentinelConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiSentinelConfigurationDto>> GetAiSentinelConfig(
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
            .Include(x => x.AiSentinelConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var ai = instance.AiSentinelConfiguration;
        var (encKey, encIv) = GetCryptoParams();
        return Ok(new AiSentinelConfigurationDto(
            ai.IsEnabled,
            ai.IsDryRun,
            ai.IsTrainingMode,
            ai.Provider,
            string.IsNullOrWhiteSpace(ai.ApiKey) ? null : "••••••••••••••••",
            ai.OllamaUrl,
            ai.OllamaHeader1Key,
            Decrypt(ai.OllamaHeader1Value, encKey, encIv),
            ai.OllamaHeader2Key,
            Decrypt(ai.OllamaHeader2Value, encKey, encIv),
            ai.OllamaHeader3Key,
            Decrypt(ai.OllamaHeader3Value, encKey, encIv),
            ai.Model,
            ai.Action,
            ai.AutoLockdown,
            ai.TimeoutMinutes,
            ai.MessageDeleteDays
        ));
    }

    /// <summary>
    /// Updates the AI Sentinel configuration for a guild the authenticated user can manage.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The updated AI Sentinel settings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Settings updated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The guild instance does not exist.</response>
    [HttpPut("{discordGuildId:long}/ai-sentinel/config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAiSentinelConfig(
        long discordGuildId,
        [FromBody] UpdateAiSentinelConfigurationRequest request,
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
            .Include(x => x.AiSentinelConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Guild not found.",
                Detail = "No guild instance exists for this Discord guild ID.",
                Status = StatusCodes.Status404NotFound
            });

        var ai = instance.AiSentinelConfiguration;
        var (encKey, encIv) = GetCryptoParams();
        ai.IsEnabled = request.IsEnabled;
        ai.IsDryRun = request.IsDryRun;
        ai.IsTrainingMode = request.IsTrainingMode;
        ai.Provider = request.Provider;
        if (!string.IsNullOrWhiteSpace(request.ApiKey) && request.ApiKey != "••••••••••••••••")
            ai.ApiKey = Encrypt(request.ApiKey, encKey, encIv);
        ai.OllamaUrl = request.OllamaUrl;
        ai.OllamaHeader1Key = request.OllamaHeader1Key;
        ai.OllamaHeader1Value = Encrypt(request.OllamaHeader1Value, encKey, encIv);
        ai.OllamaHeader2Key = request.OllamaHeader2Key;
        ai.OllamaHeader2Value = Encrypt(request.OllamaHeader2Value, encKey, encIv);
        ai.OllamaHeader3Key = request.OllamaHeader3Key;
        ai.OllamaHeader3Value = Encrypt(request.OllamaHeader3Value, encKey, encIv);
        ai.Model = request.Model;
        ai.Action = request.Action;
        ai.AutoLockdown = request.AutoLockdown;
        ai.TimeoutMinutes = Math.Max(1, request.TimeoutMinutes);
        ai.MessageDeleteDays = Math.Clamp(request.MessageDeleteDays, 0, 7);
        ai.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await aiSentinelCache.InvalidateAsync(discordGuildId);

        return NoContent();
    }

    /// <summary>
    /// Lists available models for the configured AI provider.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of available models.</returns>
    /// <response code="200">Returns the list of available models.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpGet("{discordGuildId:long}/ai-sentinel/models")]
    [ProducesResponseType(typeof(IReadOnlyList<AiSentinelModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AiSentinelModelDto>>> ListAiSentinelModels(
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
            .Include(x => x.AiSentinelConfiguration)
            .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

        if (instance is null)
            return Ok(new List<AiSentinelModelDto>());

        var ai = instance.AiSentinelConfiguration;
        var (encKey, encIv) = GetCryptoParams();
        var models = await aiSentinelService.ListModelsAsync(
            ai.Provider,
            Decrypt(ai.ApiKey, encKey, encIv),
            ai.OllamaUrl,
            ai.OllamaHeader1Key, Decrypt(ai.OllamaHeader1Value, encKey, encIv),
            ai.OllamaHeader2Key, Decrypt(ai.OllamaHeader2Value, encKey, encIv),
            ai.OllamaHeader3Key, Decrypt(ai.OllamaHeader3Value, encKey, encIv),
            cancellationToken);

        return Ok(models);
    }

    /// <summary>
    /// Lists available models for the provided AI provider credentials without saving.
    /// Used for live model loading when the user enters or changes API keys.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="request">The provider credentials to test.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of available models.</returns>
    /// <response code="200">Returns the list of available models.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpPost("{discordGuildId:long}/ai-sentinel/models")]
    [ProducesResponseType(typeof(IReadOnlyList<AiSentinelModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AiSentinelModelDto>>> ListAiSentinelModelsWithCredentials(
        long discordGuildId,
        [FromBody] ListAiSentinelModelsRequest request,
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

        var (encKey, encIv) = GetCryptoParams();

        string? apiKey = request.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "••••••••••••••••") {
            var instance = await dbContext.GuildInstances
                .Include(x => x.AiSentinelConfiguration)
                .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);
            apiKey = instance is not null
                ? Decrypt(instance.AiSentinelConfiguration.ApiKey, encKey, encIv)
                : null;
        }

        string? ollamaUrl = request.OllamaUrl;
        string? h1v = request.OllamaHeader1Value;
        string? h2v = request.OllamaHeader2Value;
        string? h3v = request.OllamaHeader3Value;

        if (request.Provider == AiSentinelProvider.Ollama) {
            var instance = await dbContext.GuildInstances
                .Include(x => x.AiSentinelConfiguration)
                .FirstOrDefaultAsync(x => x.DiscordGuildId == discordGuildId, cancellationToken);

            if (instance is not null) {
                var ai = instance.AiSentinelConfiguration;
                ollamaUrl = string.IsNullOrWhiteSpace(ollamaUrl) ? ai.OllamaUrl : ollamaUrl;
                h1v = string.IsNullOrWhiteSpace(h1v) ? Decrypt(ai.OllamaHeader1Value, encKey, encIv) : h1v;
                h2v = string.IsNullOrWhiteSpace(h2v) ? Decrypt(ai.OllamaHeader2Value, encKey, encIv) : h2v;
                h3v = string.IsNullOrWhiteSpace(h3v) ? Decrypt(ai.OllamaHeader3Value, encKey, encIv) : h3v;
            }
        }

        var models = await aiSentinelService.ListModelsAsync(
            request.Provider,
            apiKey,
            ollamaUrl,
            request.OllamaHeader1Key, h1v,
            request.OllamaHeader2Key, h2v,
            request.OllamaHeader3Key, h3v,
            cancellationToken);

        return Ok(models);
    }

    /// <summary>
    /// Returns a paginated list of AI Sentinel log entries for a guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="page">The page number (1-based).</param>
    /// <param name="pageSize">The number of entries per page.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of AI Sentinel log summaries.</returns>
    /// <response code="200">Returns the list of log entries.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    [HttpGet("{discordGuildId:long}/ai-sentinel/logs")]
    [ProducesResponseType(typeof(IReadOnlyList<AiSentinelLogSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AiSentinelLogSummaryDto>>> GetAiSentinelLogs(
        long discordGuildId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default
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

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var logs = await dbContext.AiSentinelLogs
            .Where(l => l.AiSentinelConfiguration.GuildInstance.DiscordGuildId == discordGuildId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AiSentinelLogSummaryDto(
                l.Id,
                l.DiscordMessageId,
                l.DiscordUserId,
                l.Username,
                l.DiscordChannelId,
                l.ChannelName,
                l.Classification,
                l.Reasoning,
                l.Provider,
                l.Model,
                l.IsDryRun,
                l.WouldAction,
                l.TrainingFeedback,
                l.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }

    /// <summary>
    /// Returns the full detail of a specific AI Sentinel log entry.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="logId">The log entry ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full log entry detail.</returns>
    /// <response code="200">Returns the log entry detail.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The log entry does not exist.</response>
    [HttpGet("{discordGuildId:long}/ai-sentinel/logs/{logId:long}")]
    [ProducesResponseType(typeof(AiSentinelLogDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiSentinelLogDetailDto>> GetAiSentinelLogDetail(
        long discordGuildId,
        long logId,
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

        var log = await dbContext.AiSentinelLogs
            .Where(l => l.Id == logId
                && l.AiSentinelConfiguration.GuildInstance.DiscordGuildId == discordGuildId)
            .FirstOrDefaultAsync(cancellationToken);

        if (log is null)
            return NotFound(new ProblemDetails {
                Title = "Log not found.",
                Detail = "No AI Sentinel log entry exists with this ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(new AiSentinelLogDetailDto(
            log.Id,
            log.DiscordMessageId,
            log.DiscordUserId,
            log.Username,
            log.AvatarHash,
            log.DiscordChannelId,
            log.ChannelName,
            log.Content,
            log.EmbedsJson,
            log.Classification,
            log.Reasoning,
            log.Provider,
            log.Model,
            log.IsDryRun,
            log.WouldAction,
            log.TrainingFeedback,
            log.MessageTimestampUtc,
            log.CreatedAtUtc
        ));
    }

    /// <summary>
    /// Updates the training feedback for a specific AI Sentinel log entry.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="logId">The log entry ID.</param>
    /// <param name="request">The training feedback update.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Feedback updated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    /// <response code="403">The current user cannot manage the specified guild.</response>
    /// <response code="404">The log entry does not exist.</response>
    [HttpPut("{discordGuildId:long}/ai-sentinel/logs/{logId:long}/feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAiSentinelTrainingFeedback(
        long discordGuildId,
        long logId,
        [FromBody] UpdateAiSentinelTrainingFeedbackRequest request,
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

        var log = await dbContext.AiSentinelLogs
            .Where(l => l.Id == logId
                && l.AiSentinelConfiguration.GuildInstance.DiscordGuildId == discordGuildId)
            .FirstOrDefaultAsync(cancellationToken);

        if (log is null)
            return NotFound(new ProblemDetails {
                Title = "Log not found.",
                Detail = "No AI Sentinel log entry exists with this ID.",
                Status = StatusCodes.Status404NotFound
            });

        log.TrainingFeedback = request.Feedback;
        await dbContext.SaveChangesAsync(cancellationToken);

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