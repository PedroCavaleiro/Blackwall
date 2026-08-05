using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Api.Services.Twitch;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Blackwall.TwitchBot;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Api;

namespace Blackwall.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public sealed class TwitchChannelsController(
    TwitchOAuthService twitchOAuthService,
    TwitchChannelService twitchChannelService,
    TwitchBotService twitchBotService,
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
            config.CommandTrigger
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
        config.CommandTrigger = request.CommandTrigger;
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
            config.CommandTrigger
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

        await twitchBotService.RefreshChannelsAsync();

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

            var api = new TwitchAPI();
            api.Settings.ClientId = opts.ClientId;
            api.Settings.AccessToken = accessToken;

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

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }
}
