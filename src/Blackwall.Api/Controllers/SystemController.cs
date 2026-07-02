using Blackwall.Bot.Services;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
using Discord;
using Discord.WebSocket;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Blackwall.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class SystemController(
    BlackwallDbContext dbContext,
    IConnectionMultiplexer redis,
    SafeBrowsingService safeBrowsingService,
    SafeBrowsingSyncService safeBrowsingSyncService,
    DiscordSocketClient discordClient,
    AccountScoringService accountScoringService,
    SpamConfigurationCache spamConfigurationCache
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

        var synced = await safeBrowsingSyncService.IsSyncedAsync();
        if (!string.IsNullOrWhiteSpace(url)) {
            var result = await safeBrowsingService.CheckUrlAsync(url);
            return Ok(new List<SafeBrowsingTestResponse> { new(url, result.ToString(), synced) });
        }

        const string unsafeTest = "https://testsafebrowsing.appspot.com/s/malware.html";
        const string safeTest = "github.com/PedroCavaleiro/Blackwall";

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
    /// Tests the threat level of a Discord user by running the account scoring service.
    /// </summary>
    /// <remarks>
    /// Pass a Discord user ID to evaluate the user's account metadata (account age, avatar,
    /// username patterns). If a guild ID is provided, the user is looked up in that specific
    /// guild; otherwise the bot searches all guilds it is a member of. The response includes
    /// the numeric score, threat level, and the list of contributing risk factors.
    /// When <c>notify</c> is set to <c>true</c>, a test embed is sent to the audit (log) channel
    /// of every guild the user is a member of that has a log channel configured.
    /// </remarks>
    /// <param name="userId">The Discord user ID to evaluate.</param>
    /// <param name="guildId">Optional Discord guild ID to narrow the lookup.</param>
    /// <param name="notify">If true, sends a test notification embed to the audit channel of each guild where the user is found.</param>
    /// <response code="200">Returns the threat level assessment for the user.</response>
    /// <response code="404">The user was not found in any guild the bot can see.</response>
    [HttpGet("threat-level/test")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ThreatLevelTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestThreatLevel(
        [FromQuery] ulong userId,
        [FromQuery] ulong? guildId,
        [FromQuery] bool notify = false
    ) {
        var matchingGuilds = new List<(IGuild Guild, IGuildUser User)>();

        if (guildId.HasValue) {
            var guild = discordClient.GetGuild(guildId.Value);
            var guildUser = guild?.GetUser(userId) ?? (IGuildUser?)await discordClient.Rest.GetGuildUserAsync(guildId.Value, userId);
            if (guild is not null && guildUser is not null)
                matchingGuilds.Add((guild, guildUser));
        } else {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var guild in discordClient.Guilds) {
                var guildUser = guild.GetUser(userId);
                if (guildUser is not null)
                    matchingGuilds.Add((guild, guildUser));
            }

            if (matchingGuilds.Count == 0) {
                foreach (var guild in discordClient.Guilds) {
                    var guildUser = await discordClient.Rest.GetGuildUserAsync(guild.Id, userId);
                    if (guildUser is not null)
                        matchingGuilds.Add((guild, guildUser));
                }
            }
        }

        if (matchingGuilds.Count == 0) {
            return NotFound(new ProblemDetails {
                Title = "User not found.",
                Detail = "The specified user was not found in any guild the bot is a member of.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var firstUser = matchingGuilds[0].User;
        var result = AccountScoringService.ScoreUser(firstUser);

        var notifiedGuilds = new List<ThreatLevelTestNotifiedGuild>();

        if (!notify)
            return Ok(new ThreatLevelTestResponse(
                userId,
                result.Score,
                result.ThreatLevel.ToString(),
                result.Factors,
                notifiedGuilds
            ));
        
        foreach (var (guild, user) in matchingGuilds) {
            var config = await spamConfigurationCache.GetByDiscordGuildIdAsync((long)guild.Id);
            ulong? logChannelId = config?.LogChannelId is { } cid ? (ulong)cid : null;
            var sent = false;

            if (logChannelId.HasValue) {
                if (await guild.GetChannelAsync(logChannelId.Value) is ITextChannel channel) {
                    var embed = BuildThreatLevelTestEmbed(user, result);
                    try {
                        await channel.SendMessageAsync(embed: embed);
                        sent = true;
                    } catch {
                        // Channel may be inaccessible or bot lacks send permissions
                    }
                }
            }

            notifiedGuilds.Add(new ThreatLevelTestNotifiedGuild(
                guild.Id,
                guild.Name,
                logChannelId,
                sent
            ));
        }

        return Ok(new ThreatLevelTestResponse(
            userId,
            result.Score,
            result.ThreatLevel.ToString(),
            result.Factors,
            notifiedGuilds
        ));
    }

    /// <summary>
    /// Builds the embed that is sent to a guild's audit channel when testing the threat level.
    /// </summary>
    private static Embed BuildThreatLevelTestEmbed(IGuildUser user, AccountScoreResult result) {
        var color = result.ThreatLevel switch {
            ThreatLevel.High => Color.Red,
            ThreatLevel.Medium => Color.Gold,
            _ => Color.Green
        };

        var levelEmoji = result.ThreatLevel switch {
            ThreatLevel.High => "🔴",
            ThreatLevel.Medium => "🟡",
            _ => "🟢"
        };

        return new EmbedBuilder()
            .WithColor(color)
            .WithTitle($"{levelEmoji} Threat Level Test — {result.ThreatLevel} Risk")
            .AddField("User", $"{user.Mention} (`{user.Id}`)", true)
            .AddField("Score", result.Score.ToString(), true)
            .AddField("Account age", $"{(int)(DateTimeOffset.UtcNow - user.CreatedAt).TotalDays} day(s)", true)
            .AddField("Risk factors", result.Factors.Count > 0 ? string.Join("\n", result.Factors) : "None", false)
            .WithFooter("Manual test via API")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
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