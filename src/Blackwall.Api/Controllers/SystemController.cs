using Blackwall.Bot.Services;
using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class SystemController(
    BlackwallDbContext dbContext,
    IConnectionMultiplexer redis,
    SafeBrowsingService safeBrowsingService,
    SafeBrowsingSyncService safeBrowsingSyncService
): ControllerBase {

    /// <summary>
    /// Retrieves the health status of the API and its underlying dependencies.
    /// </summary>
    /// <remarks>
    /// This endpoint checks the connectivity to the PostgreSQL database and the Redis cache.
    ///
    /// **Status states:**
    /// * `healthy`: Both database and cache are reachable.
    /// * `degraded`: One or more dependencies cannot be reached.
    /// </remarks>
    /// <response code="200">Returns the current health status, individual dependency states, and the current UTC time.</response>
    [HttpGet("health")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth() {
        var dbCanConnect = await dbContext.Database.CanConnectAsync();
        var redisConnected = redis.IsConnected;

        return Ok(new HealthCheckResponse(
            dbCanConnect && redisConnected ? "healthy" : "degraded",
            dbCanConnect,
            redisConnected,
            DateTime.UtcNow
        ));
    }

    /// <summary>
    /// Tests a URL against Google Safe Browsing and returns the result.
    /// </summary>
    /// <remarks>
    /// Pass any URL to check it. If no URL is provided, Google's official test malware URL
    /// (<c>https://testsafebrowsing.appspot.com/s/malware.html</c>) is used.
    /// The response includes the check result, whether the Global Cache is synced, and the
    /// number of entries in the Global Cache and threat lists.
    /// </remarks>
    /// <param name="url">The URL to check. Defaults to Google's Safe Browsing test malware URL.</param>
    /// <response code="200">Returns the Safe Browsing check result and sync status.</response>
    [HttpGet("safe-browsing/test")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(List<SafeBrowsingTestResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSafeBrowsing([FromQuery] string? url) {
        if (!string.IsNullOrWhiteSpace(url)) {
            var synced = await safeBrowsingSyncService.IsSyncedAsync();
            var result = await safeBrowsingService.CheckUrlAsync(url);
            return Ok(new List<SafeBrowsingTestResponse> { new(url, result.ToString(), synced) });
        }

        var unsafeTest = "https://testsafebrowsing.appspot.com/s/malware.html";
        var safeTest = "github.com/PedroCavaleiro/Blackwall";

        var synced = await safeBrowsingSyncService.IsSyncedAsync();
        var unsafeResult = await safeBrowsingService.CheckUrlAsync(unsafeTest);
        var safeResult = await safeBrowsingService.CheckUrlAsync(safeTest);

        return Ok(new List<SafeBrowsingTestResponse> {
            new(
                unsafeTest,
                unsafeResult.ToString(),
                synced
            ),
            new(
                safeTest,
                safeResult.ToString(),
                synced
            )
        });
    }

    /// <summary>
    /// Manually triggers a Safe Browsing hash list sync and returns the result.
    /// </summary>
    /// <remarks>
    /// Use this to diagnose sync failures. The response includes whether the sync succeeded,
    /// any error message, and the current number of entries in the Global Cache and threat lists.
    /// </remarks>
    /// <response code="200">Returns the sync result and current Redis state.</response>
    [HttpPost("safe-browsing/sync")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SafeBrowsingSyncResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TriggerSafeBrowsingSync() {
        var db = redis.GetDatabase();

        string? error = null;
        var success = false;

        try {
            await safeBrowsingSyncService.SyncCoreAsync();
            success = true;
        } catch (Exception ex) {
            error = ex.ToString();
        }

        var synced = await safeBrowsingSyncService.IsSyncedAsync();
        var globalCacheEntries = await db.SetLengthAsync("sb:globalcache");
        var threatEntries = await db.SetLengthAsync("sb:threats");

        return Ok(new SafeBrowsingSyncResponse(
            success,
            error,
            globalCacheEntries,
            threatEntries,
            synced
        ));
    }

}