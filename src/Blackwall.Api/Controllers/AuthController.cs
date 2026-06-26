using Blackwall.Api.Configuration;
using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    DiscordOAuthService discordOAuthService,
    JwtService jwtService,
    BlackwallDbContext dbContext,
    IOptions<WebOptions> webOptions
) : ControllerBase {

    /// <summary>
    /// Builds and returns the Discord OAuth2 authorization URL to initiate login.
    /// </summary>
    [HttpGet("discord")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login(CancellationToken ct) {
        var state = await discordOAuthService.CreateAsync();
        var url = discordOAuthService.BuildLoginUrl(state);

        return Ok(new LoginResponse(url));
    }

    /// <summary>
    /// Handles the Discord OAuth2 callback. Validates the code and state parameters, exchanges the
    /// authorization code for an access token, retrieves the current user and guilds, and creates or
    /// updates the local user record.
    /// </summary>
    /// <param name="code">The authorization code returned by Discord.</param>
    /// <param name="state">The OAuth2 state parameter used for CSRF protection.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The authenticated user profile and their Discord guilds.</returns>
    /// <response code="200">Returns the authenticated user and guild list.</response>
    /// <response code="400">The code or state is missing, invalid, or expired.</response>
    [HttpGet("discord/callback")]
    [ProducesResponseType(typeof(DiscordCallbackResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DiscordCallbackResponse>> Callback(
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

        var accessToken = await discordOAuthService.ExchangeCodeAsync(code, cancellationToken);
        var discordUser = await discordOAuthService.GetCurrentUserAsync(accessToken, cancellationToken);
        var guilds = await discordOAuthService.GetCurrentUserGuildsAsync(accessToken, cancellationToken);

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
            user = new Core.Entities.AppUser {
                DiscordUserId = discordUserId,
                Username = discordUser.Username,
                DisplayName = discordUser.GlobalName
            };

            dbContext.AppUsers.Add(user);
        } else {
            user.Username = discordUser.Username;
            user.DisplayName = discordUser.GlobalName;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var token = jwtService.GenerateToken(user);
        var redirectUrl = $"{webOptions.Value.BaseUrl.TrimEnd('/')}/auth/callback?token={Uri.EscapeDataString(token)}";

        return Redirect(redirectUrl);
    }
}