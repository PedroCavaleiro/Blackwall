using Blackwall.Api.Services;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Core.Services;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class AuthController(
    DiscordOAuthService discordOAuthService,
    AuthHandoffService authHandoffService,
    GuildClaimService guildClaimService,
    DiscordGuildCacheService guildCache,
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
    public async Task<ActionResult<LoginResponse>> Login() {
        var state = await discordOAuthService.CreateAsync();
        var url = discordOAuthService.BuildLoginUrl(state);

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
    public async Task<IActionResult> Callback(
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

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);

        if (user is null) {
            if (!IsRegistrationAllowed(discordUserId, out var errorCode)) {
                var deniedRedirectUrl = $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?error={Uri.EscapeDataString(errorCode)}";
                return Redirect(deniedRedirectUrl);
            }
        }

        var key = AesCrypto.GetBytes(appConfiguration.Value.EncryptionKey);
        var iv = AesCrypto.GetBytes(appConfiguration.Value.EncryptionIv);
        var encryptedAccessToken = AesCrypto.EncryptString(tokens.AccessToken, key, iv);
        var encryptedRefreshToken = AesCrypto.EncryptString(tokens.RefreshToken, key, iv);

        if (user is null) {
            user = new Core.Entities.AppUser {
                DiscordUserId = discordUserId,
                Username = discordUser.Username,
                DisplayName = discordUser.GlobalName,
                DiscordAccessToken = encryptedAccessToken,
                DiscordRefreshToken = encryptedRefreshToken,
                DiscordTokenExpiresAtUtc = tokens.ExpiresAt
            };

            dbContext.AppUsers.Add(user);
        } else {
            user.Username = discordUser.Username;
            user.DisplayName = discordUser.GlobalName;
            user.DiscordAccessToken = encryptedAccessToken;
            user.DiscordRefreshToken = encryptedRefreshToken;
            user.DiscordTokenExpiresAtUtc = tokens.ExpiresAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await guildClaimService.ClaimOwnershipAsync(user.Id, guilds, cancellationToken);
        await guildCache.StoreAsync(user.Id, guilds);

        var handoffCode = await authHandoffService.CreateAsync(user);
        var redirectUrl =
            $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?code={Uri.EscapeDataString(handoffCode)}";

        return Redirect(redirectUrl);
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

    private bool IsRegistrationAllowed(long discordUserId, out string errorCode) {
        var config = appConfiguration.Value;

        if (config.DisableNewUsers) {
            if (string.IsNullOrWhiteSpace(config.InstanceOwner)) {
                errorCode = "new_users_disabled";
                return false;
            }

            if (long.TryParse(config.InstanceOwner, out var ownerId) && ownerId == discordUserId) {
                errorCode = string.Empty;
                return true;
            }

            errorCode = "new_users_disabled";
            return false;
        }

        if (config.PrivateInstance) {
            var allowedUsers = configuration.GetSection("AllowedUsers").Get<string[]>() ?? [];
            var allowedSet = new HashSet<string>(allowedUsers, StringComparer.OrdinalIgnoreCase);

            if (allowedSet.Contains(discordUserId.ToString())) {
                errorCode = string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(config.InstanceOwner) &&
                long.TryParse(config.InstanceOwner, out var ownerId) &&
                ownerId == discordUserId) {
                errorCode = string.Empty;
                return true;
            }

            errorCode = "not_allowed";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }
}