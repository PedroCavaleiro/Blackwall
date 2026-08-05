using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class UsersController(
    AccountLinkingService accountLinkingService
) : ControllerBase {

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me() {
        return Ok(new {
            id = User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            discordUserId = User.FindFirstValue("discord_user_id"),
            twitchUserId = User.FindFirstValue("twitch_user_id"),
            username = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName),
            displayName = User.FindFirstValue("display_name")
        });
    }

    [Authorize]
    [HttpGet("accounts")]
    [ProducesResponseType(typeof(LinkedAccountsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LinkedAccountsResponse>> GetAccounts(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        try {
            var result = await accountLinkingService.GetLinkedAccountsAsync(appUserId.Value, cancellationToken);
            return Ok(result);
        } catch (InvalidOperationException ex) {
            return NotFound(new ProblemDetails {
                Title = "User not found.",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
    }

    [Authorize]
    [HttpPut("accounts/display-name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateDisplayNameProvider(
        [FromBody] UpdateDisplayNameProviderRequest request,
        CancellationToken cancellationToken
    ) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        try {
            await accountLinkingService.UpdateDisplayNameProviderAsync(appUserId.Value, request.Provider, cancellationToken);
            return NoContent();
        } catch (InvalidOperationException ex) {
            return BadRequest(new ProblemDetails {
                Title = "Cannot update display name provider.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        } catch (ArgumentException ex) {
            return BadRequest(new ProblemDetails {
                Title = "Invalid provider.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [Authorize]
    [HttpGet("accounts/unlink/discord/check")]
    [ProducesResponseType(typeof(UnlinkAccountWarningResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UnlinkAccountWarningResponse>> CheckUnlinkDiscord(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        try {
            var result = await accountLinkingService.CheckUnlinkDiscordAsync(appUserId.Value, cancellationToken);
            return Ok(result);
        } catch (InvalidOperationException ex) {
            return BadRequest(new ProblemDetails {
                Title = "Cannot check unlink status.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [Authorize]
    [HttpDelete("accounts/discord")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnlinkDiscord(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        try {
            await accountLinkingService.UnlinkDiscordAsync(appUserId.Value, true, cancellationToken);
            return NoContent();
        } catch (InvalidOperationException ex) {
            return BadRequest(new ProblemDetails {
                Title = "Cannot unlink Discord account.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [Authorize]
    [HttpDelete("accounts/twitch")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnlinkTwitch(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        try {
            await accountLinkingService.UnlinkTwitchAsync(appUserId.Value, cancellationToken);
            return NoContent();
        } catch (InvalidOperationException ex) {
            return BadRequest(new ProblemDetails {
                Title = "Cannot unlink Twitch account.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [Authorize]
    [HttpPost("accounts/link-warning/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DismissLinkWarning(CancellationToken cancellationToken) {
        var appUserId = GetCurrentUserId();
        if (appUserId is null)
            return Unauthorized(new ProblemDetails {
                Title = "Invalid user identity.",
                Detail = "The authenticated token did not contain a valid user id.",
                Status = StatusCodes.Status401Unauthorized
            });

        try {
            await accountLinkingService.DismissLinkAccountsWarningAsync(appUserId.Value, cancellationToken);
            return NoContent();
        } catch (InvalidOperationException ex) {
            return NotFound(new ProblemDetails {
                Title = "User not found.",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
    }

    private long? GetCurrentUserId() {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

        return long.TryParse(userIdClaim, out var appUserId)
            ? appUserId
            : null;
    }
}