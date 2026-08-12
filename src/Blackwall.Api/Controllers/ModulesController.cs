using Blackwall.Api.Services;
using Blackwall.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blackwall.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public sealed class ModulesController(
    ModuleRegistryService moduleRegistryService
) : ControllerBase {

    /// <summary>
    /// Returns the list of available modules from the public Blackwall module registry.
    /// Optionally filter by a search term (matches name, description, author, or tags)
    /// and/or by platform (discord, twitch).
    /// </summary>
    /// <param name="search">Optional search term to filter modules.</param>
    /// <param name="platform">Optional platform filter (discord or twitch).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of registry entries matching the filters.</returns>
    /// <response code="200">Returns the list of available modules.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    [HttpGet("registry")]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleRegistryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ModuleRegistryEntryDto>>> GetRegistry(
        [FromQuery] string? search,
        [FromQuery] string? platform,
        CancellationToken cancellationToken
    ) {
        var entries = await moduleRegistryService.GetRegistryAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(platform))
            entries = entries
                .Where(e => e.Platforms.Count == 0 || e.Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
                .ToList();

        if (!string.IsNullOrWhiteSpace(search)) {
            var term = search.Trim();
            entries = entries
                .Where(e =>
                    (e.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.ReadableName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Author?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    e.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))
                )
                .ToList();
        }

        return Ok(entries);
    }

    /// <summary>
    /// Forces a refresh of the module registry cache.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <response code="204">Registry cache invalidated successfully.</response>
    /// <response code="401">The user identity could not be resolved from the JWT.</response>
    [HttpPost("registry/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult RefreshRegistry(CancellationToken cancellationToken) {
        moduleRegistryService.InvalidateCache();
        return NoContent();
    }
}
