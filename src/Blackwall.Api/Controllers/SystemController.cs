using Blackwall.Core.DTOs;
using Blackwall.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class SystemController(
    BlackwallDbContext dbContext,
    IConnectionMultiplexer redis
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

}