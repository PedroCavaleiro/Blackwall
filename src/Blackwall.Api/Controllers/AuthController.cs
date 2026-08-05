using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Api.Services.Discord;
using Blackwall.Api.Services.Twitch;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class AuthController(
    DiscordOAuthService discordOAuthService,
    TwitchOAuthService twitchOAuthService,
    AuthHandoffService authHandoffService,
    GuildClaimService guildClaimService,
    AccountLinkingService accountLinkingService,
    DiscordGuildCacheService guildCache,
    TwitchChannelService twitchChannelService,
    BlackwallDbContext dbContext,
    IOptions<WebOptions> webOptions,
    IOptions<AppConfiguration> appConfiguration,
    IConfiguration configuration
) : ControllerBase {
    /// <summary>
    /// Builds and returns the Discord OAuth2 authorization URL to initiate login.
    /// </summary>
    /// <returns>The Discord authorization URL.</returns>
    /// <response code="200">Returns the Discord authorization URL.</response>
    [HttpGet("discord")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> LoginDiscord() {
        var state = await discordOAuthService.CreateAsync();
        var url = discordOAuthService.BuildLoginUrl(state);

        return Ok(new LoginResponse(url));
    }

    [HttpGet("twitch")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> LoginTwitch() {
        var state = await twitchOAuthService.CreateAsync();
        var url = twitchOAuthService.BuildLoginUrl(state);

        return Ok(new LoginResponse(url));
    }

    /// <summary>
    /// Handles the Discord OAuth2 callback. Validates the authorization code and state, exchanges the
    /// code for an access token, and creates or updates the local user record. Syncs guild ownership
    /// claims for the authenticated user, then redirects to the frontend with a short-lived handoff code.
    /// </summary>
    /// <param name="code">The authorization code returned by Discord.</param>
    /// <param name="state">The OAuth2 state parameter used for CSRF protection.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A redirect to the frontend auth callback URL containing the handoff code.</returns>
    /// <response code="302">Redirects to the frontend with the handoff code.</response>
    /// <response code="400">The code or state is missing, invalid, or expired.</response>
    [HttpGet("discord/callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DiscordCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(code)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid authorization code.",
                Detail = "The OAuth callback did not include a valid code.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(state)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid OAuth state.",
                Detail = "The OAuth callback did not include a valid state.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var linkToUserId = await discordOAuthService.ConsumeWithLinkAsync(state);
        var validState = await discordOAuthService.ConsumeAsync(state);

        if (!validState) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid or expired OAuth state.",
                Detail = "The OAuth state was invalid, expired, or already used.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var tokens = await discordOAuthService.ExchangeCodeAsync(code, cancellationToken);
        var discordUser = await discordOAuthService.GetCurrentUserAsync(tokens.AccessToken, cancellationToken);
        var guilds = await discordOAuthService.GetCurrentUserGuildsAsync(tokens.AccessToken, cancellationToken);

        if (!long.TryParse(discordUser.Id, out var discordUserId)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid Discord user id.",
                Detail = "Discord returned a user id that could not be parsed.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
        var encryptedAccessToken = AesCrypto.EncryptString(tokens.AccessToken, key, iv);
        var encryptedRefreshToken = AesCrypto.EncryptString(tokens.RefreshToken, key, iv);

        AppUser? user = null;

        if (linkToUserId.HasValue) {
            user = await dbContext.AppUsers
                .FirstOrDefaultAsync(x => x.Id == linkToUserId.Value, cancellationToken);

            if (user is not null) {
                var existingDiscordUser = await dbContext.AppUsers
                    .FirstOrDefaultAsync(x => x.DiscordUserId == discordUserId && x.Id != user.Id, cancellationToken);

                if (existingDiscordUser is not null) {
                    user = await accountLinkingService.MergeAccountsAsync(user, existingDiscordUser, cancellationToken);
                }

                user.DiscordUserId = discordUserId;
                user.Username = discordUser.Username;
                user.DisplayName = discordUser.GlobalName;
                user.DiscordAccessToken = encryptedAccessToken;
                user.DiscordRefreshToken = encryptedRefreshToken;
                user.DiscordTokenExpiresAtUtc = tokens.ExpiresAt;
                if (string.IsNullOrWhiteSpace(user.ActiveDisplayNameProvider))
                    user.ActiveDisplayNameProvider = "discord";

                await dbContext.SaveChangesAsync(cancellationToken);

                await guildClaimService.ClaimOwnershipAsync(user.Id, guilds, cancellationToken);
                await guildCache.StoreAsync(user.Id, guilds);

                var handoffCodeLink = await authHandoffService.CreateAsync(user);
                var redirectUrlLink =
                    $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?code={Uri.EscapeDataString(handoffCodeLink)}";
                return Redirect(redirectUrlLink);
            }
        }

        user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);

        if (user is null) {
            if (!IsRegistrationAllowed(discordUserId.ToString(), out var errorCode)) {
                var deniedRedirectUrl = $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?error={Uri.EscapeDataString(errorCode)}";
                return Redirect(deniedRedirectUrl);
            }
        }

        if (user is null) {
            user = new AppUser {
                DiscordUserId = discordUserId,
                Username = discordUser.Username,
                DisplayName = discordUser.GlobalName,
                DiscordAccessToken = encryptedAccessToken,
                DiscordRefreshToken = encryptedRefreshToken,
                DiscordTokenExpiresAtUtc = tokens.ExpiresAt,
                ActiveDisplayNameProvider = "discord"
            };

            dbContext.AppUsers.Add(user);
        } else {
            user.Username = discordUser.Username;
            user.DisplayName = discordUser.GlobalName;
            user.DiscordAccessToken = encryptedAccessToken;
            user.DiscordRefreshToken = encryptedRefreshToken;
            user.DiscordTokenExpiresAtUtc = tokens.ExpiresAt;
            if (string.IsNullOrWhiteSpace(user.ActiveDisplayNameProvider))
                user.ActiveDisplayNameProvider = "discord";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await guildClaimService.ClaimOwnershipAsync(user.Id, guilds, cancellationToken);
        await guildCache.StoreAsync(user.Id, guilds);

        var handoffCode = await authHandoffService.CreateAsync(user);
        var redirectUrl =
            $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?code={Uri.EscapeDataString(handoffCode)}";

        return Redirect(redirectUrl);
    }

    [HttpGet("twitch/callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TwitchCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(code)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid authorization code.",
                Detail = "The OAuth callback did not include a valid code.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(state)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid OAuth state.",
                Detail = "The OAuth callback did not include a valid state.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var linkToUserId = await twitchOAuthService.ConsumeWithLinkAsync(state);
        var validState = await twitchOAuthService.ConsumeAsync(state);

        if (!validState) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid or expired OAuth state.",
                Detail = "The OAuth state was invalid, expired, or already used.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var tokens = await twitchOAuthService.ExchangeCodeAsync(code, cancellationToken);
        var twitchUser = await twitchOAuthService.GetCurrentUserAsync(tokens.AccessToken, cancellationToken);

        if (!long.TryParse(twitchUser.Id, out var twitchUserId)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid Twitch user id.",
                Detail = "Twitch returned a user id that could not be parsed.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
        var encryptedAccessToken = AesCrypto.EncryptString(tokens.AccessToken, key, iv);
        var encryptedRefreshToken = AesCrypto.EncryptString(tokens.RefreshToken, key, iv);

        AppUser? user = null;

        if (linkToUserId.HasValue) {
            user = await dbContext.AppUsers
                .FirstOrDefaultAsync(x => x.Id == linkToUserId.Value, cancellationToken);

            if (user is not null) {
                var existingTwitchUser = await dbContext.AppUsers
                    .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId && x.Id != user.Id, cancellationToken);

                if (existingTwitchUser is not null) {
                    user = await accountLinkingService.MergeAccountsAsync(user, existingTwitchUser, cancellationToken);
                }

                user.TwitchUserId = twitchUserId;
                user.TwitchUsername = twitchUser.Login;
                user.TwitchDisplayName = twitchUser.DisplayName;
                user.TwitchProfileImageUrl = twitchUser.ProfileImageUrl;
                user.TwitchAccessToken = encryptedAccessToken;
                user.TwitchRefreshToken = encryptedRefreshToken;
                user.TwitchTokenExpiresAtUtc = tokens.ExpiresAt;
                if (string.IsNullOrWhiteSpace(user.ActiveDisplayNameProvider))
                    user.ActiveDisplayNameProvider = user.DiscordUserId != 0 ? "discord" : "twitch";

                await dbContext.SaveChangesAsync(cancellationToken);

                var handoffCode = await authHandoffService.CreateAsync(user);
                var redirectUrl =
                    $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?code={Uri.EscapeDataString(handoffCode)}";
                return Redirect(redirectUrl);
            }
        }

        user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);

        if (user is null) {
            if (!IsRegistrationAllowed(twitchUser.Id, out var errorCode)) {
                var deniedRedirectUrl = $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?error={Uri.EscapeDataString(errorCode)}";
                return Redirect(deniedRedirectUrl);
            }
        }

        if (user is null) {
            user = new AppUser {
                TwitchUserId = twitchUserId,
                TwitchUsername = twitchUser.Login,
                TwitchDisplayName = twitchUser.DisplayName,
                TwitchProfileImageUrl = twitchUser.ProfileImageUrl,
                Username = twitchUser.Login,
                DisplayName = twitchUser.DisplayName,
                TwitchAccessToken = encryptedAccessToken,
                TwitchRefreshToken = encryptedRefreshToken,
                TwitchTokenExpiresAtUtc = tokens.ExpiresAt,
                ActiveDisplayNameProvider = "twitch"
            };

            dbContext.AppUsers.Add(user);
        } else {
            user.TwitchUserId = twitchUserId;
            user.TwitchUsername = twitchUser.Login;
            user.TwitchDisplayName = twitchUser.DisplayName;
            user.TwitchProfileImageUrl = twitchUser.ProfileImageUrl;
            user.TwitchAccessToken = encryptedAccessToken;
            user.TwitchRefreshToken = encryptedRefreshToken;
            user.TwitchTokenExpiresAtUtc = tokens.ExpiresAt;
            if (string.IsNullOrWhiteSpace(user.Username))
                user.Username = twitchUser.Login;
            if (string.IsNullOrWhiteSpace(user.DisplayName))
                user.DisplayName = twitchUser.DisplayName;
            if (string.IsNullOrWhiteSpace(user.ActiveDisplayNameProvider))
                user.ActiveDisplayNameProvider = "twitch";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var handoffCode2 = await authHandoffService.CreateAsync(user);
        var redirectUrl2 =
            $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?code={Uri.EscapeDataString(handoffCode2)}";

        return Redirect(redirectUrl2);
    }

    [HttpGet("twitch/bot/callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TwitchBotCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(code)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid authorization code.",
                Detail = "The OAuth callback did not include a valid code.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(state)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid OAuth state.",
                Detail = "The OAuth callback did not include a valid state.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var appUserId = await twitchOAuthService.ConsumeBotInstallStateAsync(state);

        if (appUserId is null) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid or expired OAuth state.",
                Detail = "The OAuth state was invalid, expired, or already used.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == appUserId.Value, cancellationToken);

        if (user is null || !user.TwitchUserId.HasValue) {
            return BadRequest(new ProblemDetails {
                Title = "User not found or Twitch not linked.",
                Detail = "The authenticated user no longer exists or has no linked Twitch account.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var tokens = await twitchOAuthService.ExchangeBotCodeAsync(code, cancellationToken);
        var twitchUser = await twitchOAuthService.GetCurrentUserAsync(tokens.AccessToken, cancellationToken);

        if (!long.TryParse(twitchUser.Id, out var twitchUserId) || twitchUserId != user.TwitchUserId.Value) {
            return BadRequest(new ProblemDetails {
                Title = "Channel mismatch.",
                Detail = "The authorized Twitch channel does not match your linked Twitch account.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        await twitchChannelService.CreateOrUpdateChannelInstanceAsync(user.Id, twitchUser, cancellationToken);

        var redirectUrl = $"{webOptions.Value.BaseUrl.TrimEnd('/')}/dashboard";
        return Redirect(redirectUrl);
    }

    [Authorize]
    [HttpGet("twitch/link")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> LinkTwitch(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var state = await twitchOAuthService.CreateWithLinkAsync(appUserId.Value);
        var url = twitchOAuthService.BuildLoginUrl(state);

        return Ok(new LoginResponse(url));
    }

    [Authorize]
    [HttpGet("discord/link")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> LinkDiscord(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        var state = await discordOAuthService.CreateWithLinkAsync(appUserId.Value);
        var url = discordOAuthService.BuildLoginUrl(state);

        return Ok(new LoginResponse(url));
    }

    /// <summary>
    /// Exchanges a short-lived handoff code for a signed JWT.
    /// Consumes the code atomically so it cannot be reused.
    /// </summary>
    /// <param name="request">The request body containing the handoff code.</param>
    /// <param name="jwtService">The JWT service used to generate the token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A signed JWT for the authenticated user.</returns>
    /// <response code="200">Returns a signed JWT.</response>
    /// <response code="400">The handoff code is missing, invalid, expired, or the associated user no longer exists.</response>
    [HttpPost("exchange")]
    [ProducesResponseType(typeof(AuthExchangeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthExchangeResponse>> Exchange(
        [FromBody] AuthExchangeRequest request,
        [FromServices] JwtService jwtService,
        CancellationToken cancellationToken
    ) {
        if (string.IsNullOrWhiteSpace(request.Code)) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid handoff code.",
                Detail = "The handoff code is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var payload = await authHandoffService.ConsumeAsync(request.Code);

        if (payload is null) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid or expired handoff code.",
                Detail = "The handoff code was invalid, expired, or already used.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.Id == payload.UserId, cancellationToken);

        if (user is null) {
            return BadRequest(new ProblemDetails {
                Title = "User not found.",
                Detail = "The handoff code resolved to a user that no longer exists.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var token = jwtService.GenerateToken(user);

        return Ok(new AuthExchangeResponse(token));
    }

    private bool IsRegistrationAllowed(string externalUserId, out string errorCode) {
        var config = appConfiguration.Value;

        if (config.DisableNewUsers) {
            if (string.IsNullOrWhiteSpace(config.InstanceOwner)) {
                errorCode = "new_users_disabled";
                return false;
            }

            if (config.InstanceOwner == externalUserId) {
                errorCode = string.Empty;
                return true;
            }

            errorCode = "new_users_disabled";
            return false;
        }

        if (config.PrivateInstance) {
            var allowedUsers = configuration.GetSection("AllowedUsers").Get<string[]>() ?? [];
            var allowedSet = new HashSet<string>(allowedUsers, StringComparer.OrdinalIgnoreCase);

            if (allowedSet.Contains(externalUserId) || !string.IsNullOrWhiteSpace(config.InstanceOwner) &&
                config.InstanceOwner == externalUserId) {
                errorCode = string.Empty;
                return true;
            }

            errorCode = "not_allowed";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }
}