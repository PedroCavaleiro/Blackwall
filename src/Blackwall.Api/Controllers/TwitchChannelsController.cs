using System.Security.Claims;
using System.Text.RegularExpressions;
using Blackwall.Api.Services;
using Blackwall.Api.Services.Twitch;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Blackwall.Modules.LinkProtection;
using Blackwall.Bot.Twitch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchLib.Api;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public sealed class TwitchChannelsController(
    TwitchOAuthService twitchOAuthService,
    TwitchChannelService twitchChannelService,
    TwitchBotService twitchBotService,
    TwitchBanSyncService twitchBanSyncService,
    [FromKeyedServices("twitch")] LinkProtectionService linkProtectionService,
    BlackwallDbContext dbContext,
    IOptions<TwitchOptions> twitchOptions,
    IOptions<AppConfiguration> appConfiguration,
    ILogger<TwitchChannelsController> logger
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

        try {
            if (!string.IsNullOrWhiteSpace(user.TwitchAccessToken)) {
                var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
                var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
                var accessToken = AesCrypto.DecryptString(user.TwitchAccessToken, key, iv);
                await twitchChannelService.AutoAddManagersAsync(appUserId.Value, accessToken, cancellationToken);
            }
        } catch {
            // Best-effort — don't block dashboard load
        }

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

    [HttpGet("{twitchUserId:long}/settings")]
    [ProducesResponseType(typeof(TwitchChannelSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TwitchChannelSettingsResponse>> GetSettings(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null) {
            config = new TwitchChannelConfiguration {
                TwitchChannelInstanceId = instance.Id,
                IsEnabled = true,
                IsDryRun = false,
                CommandTrigger = "!",
                UpdatedAtUtc = DateTime.UtcNow
            };
            instance.Configuration = config;
            dbContext.TwitchChannelConfigurations.Add(config);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var isOwner = instance.OwnerUserId == appUserId.Value;

        return Ok(new TwitchChannelSettingsResponse(
            instance.TwitchUserId,
            instance.Username,
            instance.DisplayName,
            instance.ProfileImageUrl,
            instance.IsActive,
            isOwner,
            instance.ShareBanList,
            config.IsEnabled,
            config.IsDryRun,
            config.AutoAddManagers,
            config.CommandTrigger,
            config.MaxMessagesPerWindow,
            config.RateLimitWindowSeconds,
            config.DuplicateMessageThreshold,
            config.DuplicateWindowSeconds,
            config.MentionLimit,
            config.RateLimitAction,
            config.RateLimitTimeoutMinutes,
            config.DuplicateAction,
            config.DuplicateTimeoutMinutes,
            config.MentionLimitAction,
            config.MentionLimitTimeoutMinutes,
            config.BlockSuspiciousLinks,
            config.LinkWhitelistMode,
            config.SafeBrowsingEnabled,
            config.SafeBrowsingBlockUnsure,
            config.SuspiciousLinkAction,
            config.SuspiciousLinkTimeoutMinutes,
            config.IsContentGuardEnabled,
            config.ContentGuardFuzzyMatching,
            config.ContentGuardFuzzyThreshold,
            config.ContentGuardAction,
            config.ContentGuardTimeoutMinutes
        ));
    }

    [HttpPut("{twitchUserId:long}/settings")]
    [ProducesResponseType(typeof(TwitchChannelSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TwitchChannelSettingsResponse>> UpdateSettings(
        long twitchUserId,
        [FromBody] UpdateTwitchChannelSettingsRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.CommandTrigger) || request.CommandTrigger.Length > 2)
            return BadRequest(new ProblemDetails {
                Title = "Invalid command trigger.",
                Detail = "Command trigger must be 1-2 characters.",
                Status = StatusCodes.Status400BadRequest
            });

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null) {
            config = new TwitchChannelConfiguration {
                TwitchChannelInstanceId = instance.Id,
                UpdatedAtUtc = DateTime.UtcNow
            };
            instance.Configuration = config;
            dbContext.TwitchChannelConfigurations.Add(config);
        }

        config.IsEnabled = request.IsEnabled;
        config.IsDryRun = request.IsDryRun;
        config.AutoAddManagers = request.AutoAddManagers;
        config.CommandTrigger = request.CommandTrigger;
        config.MaxMessagesPerWindow = request.MaxMessagesPerWindow;
        config.RateLimitWindowSeconds = request.RateLimitWindowSeconds;
        config.DuplicateMessageThreshold = request.DuplicateMessageThreshold;
        config.DuplicateWindowSeconds = request.DuplicateWindowSeconds;
        config.MentionLimit = request.MentionLimit;
        config.RateLimitAction = request.RateLimitAction;
        config.RateLimitTimeoutMinutes = request.RateLimitTimeoutMinutes;
        config.DuplicateAction = request.DuplicateAction;
        config.DuplicateTimeoutMinutes = request.DuplicateTimeoutMinutes;
        config.MentionLimitAction = request.MentionLimitAction;
        config.MentionLimitTimeoutMinutes = request.MentionLimitTimeoutMinutes;
        config.BlockSuspiciousLinks = request.BlockSuspiciousLinks;
        config.LinkWhitelistMode = request.LinkWhitelistMode;
        config.SafeBrowsingEnabled = request.SafeBrowsingEnabled;
        config.SafeBrowsingBlockUnsure = request.SafeBrowsingBlockUnsure;
        config.SuspiciousLinkAction = request.SuspiciousLinkAction;
        config.SuspiciousLinkTimeoutMinutes = request.SuspiciousLinkTimeoutMinutes;
        config.IsContentGuardEnabled = request.IsContentGuardEnabled;
        config.ContentGuardFuzzyMatching = request.ContentGuardFuzzyMatching;
        config.ContentGuardFuzzyThreshold = request.ContentGuardFuzzyThreshold;
        config.ContentGuardAction = request.ContentGuardAction;
        config.ContentGuardTimeoutMinutes = request.ContentGuardTimeoutMinutes;
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var isOwner = instance.OwnerUserId == appUserId.Value;

        return Ok(new TwitchChannelSettingsResponse(
            instance.TwitchUserId,
            instance.Username,
            instance.DisplayName,
            instance.ProfileImageUrl,
            instance.IsActive,
            isOwner,
            instance.ShareBanList,
            config.IsEnabled,
            config.IsDryRun,
            config.AutoAddManagers,
            config.CommandTrigger,
            config.MaxMessagesPerWindow,
            config.RateLimitWindowSeconds,
            config.DuplicateMessageThreshold,
            config.DuplicateWindowSeconds,
            config.MentionLimit,
            config.RateLimitAction,
            config.RateLimitTimeoutMinutes,
            config.DuplicateAction,
            config.DuplicateTimeoutMinutes,
            config.MentionLimitAction,
            config.MentionLimitTimeoutMinutes,
            config.BlockSuspiciousLinks,
            config.LinkWhitelistMode,
            config.SafeBrowsingEnabled,
            config.SafeBrowsingBlockUnsure,
            config.SuspiciousLinkAction,
            config.SuspiciousLinkTimeoutMinutes,
            config.IsContentGuardEnabled,
            config.ContentGuardFuzzyMatching,
            config.ContentGuardFuzzyThreshold,
            config.ContentGuardAction,
            config.ContentGuardTimeoutMinutes
        ));
    }

    [HttpDelete("{twitchUserId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBot(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (instance.OwnerUserId != appUserId.Value)
            return Forbid();

        await TryRemoveBotAsModeratorAsync(instance);

        instance.IsActive = false;
        instance.BotAccessToken = null;
        instance.BotRefreshToken = null;
        instance.BotTokenExpiresAtUtc = null;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await twitchBotService.RefreshChannelsAsync(cancellationToken);

        return NoContent();
    }

    private async Task TryRemoveBotAsModeratorAsync(TwitchChannelInstance instance) {
        var opts = twitchOptions.Value;
        if (string.IsNullOrWhiteSpace(opts.BotUsername) || string.IsNullOrWhiteSpace(instance.BotAccessToken))
            return;

        try {
            var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
            var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
            var accessToken = AesCrypto.DecryptString(instance.BotAccessToken, key, iv);

            var api = new TwitchAPI {
                Settings = {
                    ClientId = opts.ClientId,
                    AccessToken = accessToken
                }
            };

            var botUserResponse = await api.Helix.Users.GetUsersAsync(logins: [opts.BotUsername]);
            if (botUserResponse.Users.Length == 0) {
                logger.LogWarning("Bot account '{BotUser}' not found — skipping mod removal", opts.BotUsername);
                return;
            }

            var botUserId = botUserResponse.Users[0].Id;
            await api.Helix.Moderation.DeleteChannelModeratorAsync(instance.TwitchUserId.ToString(), botUserId);
            logger.LogInformation("Removed bot account '{BotUser}' (ID: {BotUserId}) as moderator from channel {ChannelId}", opts.BotUsername, botUserId, instance.TwitchUserId);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to remove bot as moderator from channel {ChannelId}", instance.TwitchUserId);
        }
    }

    [HttpGet("{twitchUserId:long}/allowed-bots")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchAllowedBotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TwitchAllowedBotResponse>>> GetAllowedBots(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.AllowedBots)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null)
            return Ok(new List<TwitchAllowedBotResponse>());

        return Ok(config.AllowedBots
            .Select(b => new TwitchAllowedBotResponse(b.Id, b.BotUsername))
            .ToList());
    }

    [HttpPost("{twitchUserId:long}/allowed-bots")]
    [ProducesResponseType(typeof(TwitchAllowedBotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TwitchAllowedBotResponse>> AddAllowedBot(
        long twitchUserId,
        [FromBody] AddTwitchAllowedBotRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.BotUsername))
            return BadRequest(new ProblemDetails {
                Title = "Invalid bot username.",
                Detail = "Bot username cannot be empty.",
                Status = StatusCodes.Status400BadRequest
            });

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.AllowedBots)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null) {
            config = new TwitchChannelConfiguration {
                TwitchChannelInstanceId = instance.Id,
                IsEnabled = true,
                IsDryRun = false,
                CommandTrigger = "!",
                UpdatedAtUtc = DateTime.UtcNow
            };
            instance.Configuration = config;
            dbContext.TwitchChannelConfigurations.Add(config);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var normalizedUsername = request.BotUsername.Trim().ToLowerInvariant();
        if (config.AllowedBots.Any(b => b.BotUsername.ToLowerInvariant() == normalizedUsername))
            return Conflict(new ProblemDetails {
                Title = "Bot already allowed.",
                Detail = "This bot is already on the allowed list for this channel.",
                Status = StatusCodes.Status409Conflict
            });

        var entry = new TwitchAllowedBot {
            TwitchChannelConfigurationId = config.Id,
            BotUsername = request.BotUsername.Trim()
        };

        config.AllowedBots.Add(entry);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new TwitchAllowedBotResponse(entry.Id, entry.BotUsername));
    }

    [HttpDelete("{twitchUserId:long}/allowed-bots/{botId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAllowedBot(
        long twitchUserId,
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

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.AllowedBots)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null)
            return NotFound(new ProblemDetails {
                Title = "Allowed bot not found.",
                Detail = "No allowed bot with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        var entry = config.AllowedBots.FirstOrDefault(b => b.Id == botId);
        if (entry is null)
            return NotFound(new ProblemDetails {
                Title = "Allowed bot not found.",
                Detail = "No allowed bot with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        config.AllowedBots.Remove(entry);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("{twitchUserId:long}/managers")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchChannelManagerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TwitchChannelManagerResponse>>> GetManagers(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var managers = await dbContext.TwitchChannelManagers
            .Include(m => m.User)
            .Where(m => m.TwitchChannelInstanceId == instance.Id)
            .Select(m => new TwitchChannelManagerResponse(
                m.Id,
                m.UserId,
                m.User.TwitchUsername ?? m.User.Username,
                m.User.TwitchDisplayName ?? m.User.DisplayName,
                m.User.TwitchProfileImageUrl,
                m.IsAdmin
            ))
            .ToListAsync(cancellationToken);

        return Ok(managers);
    }

    [HttpPost("{twitchUserId:long}/managers")]
    [ProducesResponseType(typeof(TwitchChannelManagerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TwitchChannelManagerResponse>> AddManager(
        long twitchUserId,
        [FromBody] AddTwitchChannelManagerRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new ProblemDetails {
                Title = "Invalid username.",
                Detail = "Username cannot be empty.",
                Status = StatusCodes.Status400BadRequest
            });

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        var existingManager = await dbContext.TwitchChannelManagers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.TwitchChannelInstanceId == instance.Id
                && (m.User.TwitchUsername != null && m.User.TwitchUsername.Equals(normalizedUsername, StringComparison.InvariantCultureIgnoreCase)),
                cancellationToken);

        if (existingManager is not null)
            return Conflict(new ProblemDetails {
                Title = "Manager already exists.",
                Detail = "This user is already a manager for this channel.",
                Status = StatusCodes.Status409Conflict
            });

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.TwitchUsername != null && u.TwitchUsername.Equals(normalizedUsername, StringComparison.InvariantCultureIgnoreCase),
                cancellationToken);

        if (user is null)
            return BadRequest(new ProblemDetails {
                Title = "User not found.",
                Detail = "No Blackwall account found with this Twitch username. The user must create an account first.",
                Status = StatusCodes.Status400BadRequest
            });

        var entry = new TwitchChannelManager {
            TwitchChannelInstanceId = instance.Id,
            UserId = user.Id,
            IsAdmin = false
        };

        dbContext.TwitchChannelManagers.Add(entry);

        var priorRemoval = await dbContext.TwitchRemovedManagers
            .FirstOrDefaultAsync(r => r.TwitchChannelInstanceId == instance.Id && r.UserId == user.Id, cancellationToken);
        if (priorRemoval is not null)
            dbContext.TwitchRemovedManagers.Remove(priorRemoval);

        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new TwitchChannelManagerResponse(
            entry.Id,
            user.Id,
            user.TwitchUsername ?? user.Username,
            user.TwitchDisplayName ?? user.DisplayName,
            user.TwitchProfileImageUrl,
            entry.IsAdmin
        ));
    }

    [HttpDelete("{twitchUserId:long}/managers/{managerId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveManager(
        long twitchUserId,
        long managerId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var entry = await dbContext.TwitchChannelManagers
            .FirstOrDefaultAsync(m => m.Id == managerId && m.TwitchChannelInstanceId == instance.Id, cancellationToken);

        if (entry is null)
            return NotFound(new ProblemDetails {
                Title = "Manager not found.",
                Detail = "No manager with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        dbContext.TwitchChannelManagers.Remove(entry);

        var alreadyRemoved = await dbContext.TwitchRemovedManagers
            .AnyAsync(r => r.TwitchChannelInstanceId == instance.Id && r.UserId == entry.UserId, cancellationToken);
        if (!alreadyRemoved) {
            dbContext.TwitchRemovedManagers.Add(new TwitchRemovedManager {
                TwitchChannelInstanceId = instance.Id,
                UserId = entry.UserId
            });
        }

        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

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
        return Ok(linkProtectionService.GetDefaultBlacklists()
            .Select(url => new DefaultBlacklistResponse(url))
            .ToList());
    }

    [HttpGet("{twitchUserId:long}/blacklists")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchChannelBlacklistResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TwitchChannelBlacklistResponse>>> GetBlacklists(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.Blacklists)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var blacklists = instance.Configuration?.Blacklists ?? [];
        return Ok(blacklists
            .Select(b => new TwitchChannelBlacklistResponse(b.Id, b.Url))
            .ToList());
    }

    [HttpPost("{twitchUserId:long}/blacklists")]
    [ProducesResponseType(typeof(TwitchChannelBlacklistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TwitchChannelBlacklistResponse>> AddBlacklist(
        long twitchUserId,
        [FromBody] AddTwitchChannelBlacklistRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.Blacklists)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null) {
            config = new TwitchChannelConfiguration {
                TwitchChannelInstanceId = instance.Id,
                UpdatedAtUtc = DateTime.UtcNow
            };
            instance.Configuration = config;
            dbContext.TwitchChannelConfigurations.Add(config);
        }

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return BadRequest(new ProblemDetails {
                Title = "Invalid URL.",
                Detail = "The blacklist URL must be a valid HTTP or HTTPS URL.",
                Status = StatusCodes.Status400BadRequest
            });

        if (config.Blacklists.Any(b => b.Url.Equals(request.Url, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ProblemDetails {
                Title = "Blacklist already configured.",
                Detail = "This blacklist URL is already configured for this channel.",
                Status = StatusCodes.Status409Conflict
            });

        var blacklist = new TwitchChannelBlacklist {
            TwitchChannelConfigurationId = config.Id,
            Url = request.Url
        };

        config.Blacklists.Add(blacklist);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => linkProtectionService.RefreshScopeAsync(twitchUserId, CancellationToken.None), cancellationToken);

        return Ok(new TwitchChannelBlacklistResponse(blacklist.Id, blacklist.Url));
    }

    [HttpDelete("{twitchUserId:long}/blacklists/{blacklistId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBlacklist(
        long twitchUserId,
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

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.Blacklists)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null)
            return NotFound(new ProblemDetails {
                Title = "Blacklist not found.",
                Detail = "No blacklist with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        var blacklist = config.Blacklists.FirstOrDefault(b => b.Id == blacklistId);
        if (blacklist is null)
            return NotFound(new ProblemDetails {
                Title = "Blacklist not found.",
                Detail = "No blacklist with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        config.Blacklists.Remove(blacklist);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => linkProtectionService.RefreshScopeAsync(twitchUserId, CancellationToken.None), cancellationToken);

        return NoContent();
    }

    [HttpPost("{twitchUserId:long}/blacklists/refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshBlacklists(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        await linkProtectionService.RefreshScopeAsync(twitchUserId, cancellationToken);

        return Ok(new { Message = "Blacklists refreshed." });
    }

    [HttpGet("{twitchUserId:long}/domain-rules")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchChannelDomainRuleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TwitchChannelDomainRuleResponse>>> GetDomainRules(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.DomainRules)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var rules = instance.Configuration?.DomainRules ?? [];
        return Ok(rules
            .Select(r => new TwitchChannelDomainRuleResponse(r.Id, r.Rule))
            .ToList());
    }

    [HttpPost("{twitchUserId:long}/domain-rules")]
    [ProducesResponseType(typeof(TwitchChannelDomainRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TwitchChannelDomainRuleResponse>> AddDomainRule(
        long twitchUserId,
        [FromBody] AddTwitchChannelDomainRuleRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.DomainRules)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null) {
            config = new TwitchChannelConfiguration {
                TwitchChannelInstanceId = instance.Id,
                UpdatedAtUtc = DateTime.UtcNow
            };
            instance.Configuration = config;
            dbContext.TwitchChannelConfigurations.Add(config);
        }

        if (string.IsNullOrWhiteSpace(request.Rule))
            return BadRequest(new ProblemDetails {
                Title = "Invalid rule.",
                Detail = "The domain rule must not be empty.",
                Status = StatusCodes.Status400BadRequest
            });

        var normalizedRule = request.Rule.Trim().ToLowerInvariant();

        if (config.DomainRules.Any(r => r.Rule.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ProblemDetails {
                Title = "Domain rule already configured.",
                Detail = "This domain rule is already configured for this channel.",
                Status = StatusCodes.Status409Conflict
            });

        var rule = new TwitchChannelDomainRule {
            TwitchChannelConfigurationId = config.Id,
            Rule = normalizedRule
        };

        config.DomainRules.Add(rule);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => linkProtectionService.RefreshScopeAsync(twitchUserId, CancellationToken.None), cancellationToken);

        return Ok(new TwitchChannelDomainRuleResponse(rule.Id, rule.Rule));
    }

    [HttpDelete("{twitchUserId:long}/domain-rules/{ruleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveDomainRule(
        long twitchUserId,
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

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.DomainRules)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null)
            return NotFound(new ProblemDetails {
                Title = "Domain rule not found.",
                Detail = "No domain rule with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        var rule = config.DomainRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return NotFound(new ProblemDetails {
                Title = "Domain rule not found.",
                Detail = "No domain rule with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        config.DomainRules.Remove(rule);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        _ = Task.Run(() => linkProtectionService.RefreshScopeAsync(twitchUserId, CancellationToken.None), cancellationToken);

        return NoContent();
    }

    [HttpGet("{twitchUserId:long}/banned-words")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchBannedWordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TwitchBannedWordResponse>>> GetBannedWords(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var words = await dbContext.TwitchChannelBannedWords
            .Where(x => x.TwitchChannelConfiguration.TwitchChannelInstance.TwitchUserId == twitchUserId)
            .Select(x => new TwitchBannedWordResponse(x.Id, x.Word, x.IsRegex))
            .ToListAsync(cancellationToken);

        return Ok(words);
    }

    [HttpPost("{twitchUserId:long}/banned-words")]
    [ProducesResponseType(typeof(TwitchBannedWordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TwitchBannedWordResponse>> AddBannedWord(
        long twitchUserId,
        [FromBody] AddTwitchBannedWordRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.BannedWords)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null) {
            config = new TwitchChannelConfiguration {
                TwitchChannelInstanceId = instance.Id,
                UpdatedAtUtc = DateTime.UtcNow
            };
            instance.Configuration = config;
            dbContext.TwitchChannelConfigurations.Add(config);
        }

        var word = request.Word.Trim();
        if (string.IsNullOrWhiteSpace(word) || word.Length > 100)
            return BadRequest(new ProblemDetails {
                Title = "Invalid word.",
                Detail = "The word must be between 1 and 100 characters.",
                Status = StatusCodes.Status400BadRequest
            });

        if (request.IsRegex) {
            try {
                _ = Regex.Match(string.Empty, word, RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));
            } catch (ArgumentException) {
                return BadRequest(new ProblemDetails {
                    Title = "Invalid regex pattern.",
                    Detail = "The provided regex pattern is not valid.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        } else {
            word = word.ToLowerInvariant();
        }

        if (config.BannedWords.Any(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new ProblemDetails {
                Title = "Word already configured.",
                Detail = "This word is already in the banned words list for this channel.",
                Status = StatusCodes.Status409Conflict
            });

        var entry = new TwitchChannelBannedWord {
            TwitchChannelConfigurationId = config.Id,
            Word = word,
            IsRegex = request.IsRegex
        };

        config.BannedWords.Add(entry);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new TwitchBannedWordResponse(entry.Id, entry.Word, entry.IsRegex));
    }

    [HttpDelete("{twitchUserId:long}/banned-words/{wordId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBannedWord(
        long twitchUserId,
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

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Configuration!.BannedWords)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var config = instance.Configuration;
        if (config is null)
            return NotFound(new ProblemDetails {
                Title = "Banned word not found.",
                Detail = "No banned word with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        var entry = config.BannedWords.FirstOrDefault(w => w.Id == wordId);
        if (entry is null)
            return NotFound(new ProblemDetails {
                Title = "Banned word not found.",
                Detail = "No banned word with this ID exists for this channel.",
                Status = StatusCodes.Status404NotFound
            });

        config.BannedWords.Remove(entry);
        config.UpdatedAtUtc = DateTime.UtcNow;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("{twitchUserId:long}/bans")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchChannelBanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TwitchChannelBanResponse>>> GetBans(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(instance.Bans
            .Select(b => new TwitchChannelBanResponse(b.Id, b.TwitchUserId, b.Username, b.Reason, b.BannedAtUtc))
            .OrderByDescending(b => b.BannedAtUtc)
            .ToList());
    }

    [HttpPost("{twitchUserId:long}/bans/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SyncBans(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        try {
            var count = await twitchBanSyncService.SyncBansAsync(twitchUserId, cancellationToken);
            return Ok(new { Message = $"Synced {count} bans.", Count = count });
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to sync bans for Twitch channel {TwitchUserId}", twitchUserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails {
                Title = "Failed to sync bans.",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    [HttpPut("{twitchUserId:long}/bans/share")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateShareBanList(
        long twitchUserId,
        [FromBody] UpdateTwitchShareBanListRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        instance.ShareBanList = request.ShareBanList;
        instance.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("{twitchUserId:long}/bans/shared-channels")]
    [ProducesResponseType(typeof(IReadOnlyList<SharedBanListTwitchChannelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SharedBanListTwitchChannelResponse>>> GetSharedBanListChannels(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var sharedChannels = await dbContext.TwitchChannelInstances
            .Where(x => x.IsActive && x.ShareBanList && x.TwitchUserId != twitchUserId)
            .Select(x => new {
                x.TwitchUserId,
                x.Username,
                x.DisplayName,
                x.ProfileImageUrl,
                BanCount = x.Bans.Count
            })
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return Ok(sharedChannels
            .Select(x => new SharedBanListTwitchChannelResponse(x.TwitchUserId, x.Username, x.DisplayName, x.ProfileImageUrl, x.BanCount))
            .ToList());
    }

    [HttpGet("{twitchUserId:long}/bans/source/{sourceTwitchUserId:long}")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchChannelBanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TwitchChannelBanResponse>>> GetSourceChannelBans(
        long twitchUserId,
        long sourceTwitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var sourceInstance = await dbContext.TwitchChannelInstances
            .Include(x => x.Bans)
            .FirstOrDefaultAsync(x => x.TwitchUserId == sourceTwitchUserId && x.IsActive, cancellationToken);

        if (sourceInstance is null)
            return NotFound(new ProblemDetails {
                Title = "Source channel not found.",
                Detail = "No Twitch channel instance exists for the source user ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (!sourceInstance.ShareBanList)
            return Forbid();

        return Ok(sourceInstance.Bans
            .Select(b => new TwitchChannelBanResponse(b.Id, b.TwitchUserId, b.Username, b.Reason, b.BannedAtUtc))
            .OrderByDescending(b => b.BannedAtUtc)
            .ToList());
    }

    [HttpPost("{twitchUserId:long}/bans/import")]
    [ProducesResponseType(typeof(ImportTwitchBansResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImportTwitchBansResultResponse>> ImportBans(
        long twitchUserId,
        [FromBody] ImportTwitchBansRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        if (request.SourceTwitchUserId == twitchUserId)
            return BadRequest(new ProblemDetails {
                Title = "Cannot import from self.",
                Detail = "The source channel cannot be the same as the target channel.",
                Status = StatusCodes.Status400BadRequest
            });

        var result = await twitchBanSyncService.ImportBansAsync(
            twitchUserId, request.SourceTwitchUserId, request.TwitchUserIds, cancellationToken);

        return Ok(new ImportTwitchBansResultResponse(result.Imported, result.Skipped, result.Failed, result.Errors));
    }

    [HttpGet("{twitchUserId:long}/bans/auto-sync")]
    [ProducesResponseType(typeof(IReadOnlyList<TwitchBanSyncRuleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TwitchBanSyncRuleResponse>>> GetBanSyncRules(
        long twitchUserId,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.BanSyncRules)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var sourceIds = instance.BanSyncRules.Select(r => r.SourceTwitchUserId).ToHashSet();
        var sourceChannels = await dbContext.TwitchChannelInstances
            .Where(x => sourceIds.Contains(x.TwitchUserId))
            .ToDictionaryAsync(x => x.TwitchUserId, cancellationToken);

        return Ok(instance.BanSyncRules
            .Select(r => new TwitchBanSyncRuleResponse(
                r.Id,
                r.SourceTwitchUserId,
                sourceChannels.TryGetValue(r.SourceTwitchUserId, out var src) ? src.DisplayName : "Unknown",
                r.IsEnabled,
                r.LastSyncedAtUtc == DateTime.MinValue ? null : r.LastSyncedAtUtc
            ))
            .OrderBy(r => r.SourceChannelName)
            .ToList());
    }

    [HttpPost("{twitchUserId:long}/bans/auto-sync")]
    [ProducesResponseType(typeof(TwitchBanSyncRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TwitchBanSyncRuleResponse>> AddBanSyncRule(
        long twitchUserId,
        [FromBody] AddTwitchBanSyncRuleRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        if (request.SourceTwitchUserId == twitchUserId)
            return BadRequest(new ProblemDetails {
                Title = "Cannot auto-sync from self.",
                Detail = "The source channel cannot be the same as the target channel.",
                Status = StatusCodes.Status400BadRequest
            });

        var instance = await dbContext.TwitchChannelInstances
            .Include(x => x.BanSyncRules)
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var sourceChannel = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == request.SourceTwitchUserId && x.IsActive, cancellationToken);

        if (sourceChannel is null)
            return BadRequest(new ProblemDetails {
                Title = "Source channel not found.",
                Detail = "The source channel does not exist or is not active.",
                Status = StatusCodes.Status400BadRequest
            });

        if (!sourceChannel.ShareBanList)
            return BadRequest(new ProblemDetails {
                Title = "Ban list not shared.",
                Detail = "The source channel does not have ban list sharing enabled.",
                Status = StatusCodes.Status400BadRequest
            });

        if (instance.BanSyncRules.Any(r => r.SourceTwitchUserId == request.SourceTwitchUserId))
            return BadRequest(new ProblemDetails {
                Title = "Rule already exists.",
                Detail = "An auto-sync rule for this source channel already exists.",
                Status = StatusCodes.Status400BadRequest
            });

        var rule = new TwitchChannelBanSyncRule {
            TargetTwitchChannelInstanceId = instance.Id,
            SourceTwitchUserId = request.SourceTwitchUserId,
            IsEnabled = true
        };

        dbContext.TwitchChannelBanSyncRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new TwitchBanSyncRuleResponse(rule.Id, rule.SourceTwitchUserId, sourceChannel.DisplayName, rule.IsEnabled, null));
    }

    [HttpPut("{twitchUserId:long}/bans/auto-sync/{ruleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBanSyncRule(
        long twitchUserId,
        long ruleId,
        [FromBody] UpdateTwitchBanSyncRuleRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var rule = await dbContext.TwitchChannelBanSyncRules
            .FirstOrDefaultAsync(x => x.Id == ruleId && x.TargetTwitchChannelInstanceId == instance.Id, cancellationToken);

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

    [HttpDelete("{twitchUserId:long}/bans/auto-sync/{ruleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBanSyncRule(
        long twitchUserId,
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

        var canOpen = await twitchChannelService.CanOpenChannelAsync(appUserId.Value, twitchUserId, cancellationToken);
        if (!canOpen)
            return Forbid();

        var instance = await dbContext.TwitchChannelInstances
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (instance is null)
            return NotFound(new ProblemDetails {
                Title = "Channel not found.",
                Detail = "No Twitch channel instance exists for this user ID.",
                Status = StatusCodes.Status404NotFound
            });

        var rule = await dbContext.TwitchChannelBanSyncRules
            .FirstOrDefaultAsync(x => x.Id == ruleId && x.TargetTwitchChannelInstanceId == instance.Id, cancellationToken);

        if (rule is null)
            return NotFound(new ProblemDetails {
                Title = "Auto-sync rule not found.",
                Detail = "No auto-sync rule exists with the specified ID.",
                Status = StatusCodes.Status404NotFound
            });

        dbContext.TwitchChannelBanSyncRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }
}
