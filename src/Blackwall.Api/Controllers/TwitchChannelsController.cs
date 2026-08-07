using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Api.Services.Twitch;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Blackwall.LinkProtection;
using Blackwall.TwitchBot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            config.SuspiciousLinkTimeoutMinutes
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
            config.SuspiciousLinkTimeoutMinutes
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

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }
}
