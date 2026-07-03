using Blackwall.Bot.Services;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Bot.Handlers;

public sealed class GuildMemberHandler(
    IServiceScopeFactory scopeFactory,
    RaidDetectionService raidDetectionService,
    ILogger<GuildMemberHandler> logger
) {
    /// <summary>
    /// Called whenever a user joins a guild. Checks if anti-raid is enabled and whether
    /// the join rate has crossed the configured threshold. If a raid is detected and the
    /// guild is not already in lockdown, all active invite links are deleted and the event
    /// is logged to the configured log channel.
    /// Additionally, if account scoring is enabled, the user's metadata is evaluated and
    /// medium/high risk accounts trigger a mod alert with an optional automatic timeout.
    /// </summary>
    /// <param name="user">The guild member that joined.</param>
    public async Task OnUserJoinedAsync(SocketGuildUser user) {
        var discordGuildId = (long)user.Guild.Id;

        Core.DTOs.SpamConfigurationDto? config;
        using (var scope = scopeFactory.CreateScope()) {
            var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
            config = await cache.GetByDiscordGuildIdAsync(discordGuildId);
        }

        if (config is null || !config.IsEnabled)
            return;

        if (config.IsAccountScoringEnabled)
            await EvaluateAccountScoreAsync(user, config);

        if (!config.IsAntiRaidEnabled)
            return;

        if (await raidDetectionService.IsInLockdownAsync(discordGuildId))
            return;

        var raidDetected = await raidDetectionService.RecordJoinAsync(
            discordGuildId,
            config.AntiRaidJoinThreshold,
            config.AntiRaidWindowSeconds
        );

        if (!raidDetected)
            return;

        logger.LogWarning(
            "Raid detected in guild {GuildId}: {Threshold} joins in {Window}s. Pausing invites",
            discordGuildId, config.AntiRaidJoinThreshold, config.AntiRaidWindowSeconds);

        await raidDetectionService.SetLockdownAsync(discordGuildId, config.AntiRaidCooldownMinutes);

        if (!config.IsDryRun)
            await PauseInvitesAsync(user.Guild);

        if (config.LogChannelId.HasValue)
            await SendRaidLogAsync(user.Guild, config, user);
    }

    /// <summary>
    /// Scores the joining user's account metadata and alerts moderators if the threat level
    /// is medium or high. Optionally applies a timeout if configured for the risk level.
    /// </summary>
    /// <param name="user">The guild member whose account is being scored.</param>
    /// <param name="config">The spam configuration for the guild, containing scoring and timeout settings.</param>
    private async Task EvaluateAccountScoreAsync(SocketGuildUser user, Core.DTOs.SpamConfigurationDto config) {
        var scoreResult = AccountScoringService.ScoreUser(user);

        if (scoreResult.ThreatLevel == ThreatLevel.Low)
            return;

        logger.LogInformation(
            "Account scoring for user {UserId} in guild {GuildId}: score={Score}, level={Level}",
            user.Id, user.Guild.Id, scoreResult.Score, scoreResult.ThreatLevel);

        var shouldTimeout = scoreResult.ThreatLevel switch {
            ThreatLevel.High => config.AutoTimeoutHighRiskOnJoin,
            ThreatLevel.Medium => config.AutoTimeoutMediumRiskOnJoin,
            _ => false
        };

        if (shouldTimeout && !config.IsDryRun) {
            try {
                await user.SetTimeOutAsync(TimeSpan.FromMinutes(config.AccountScoringTimeoutMinutes));
                logger.LogInformation(
                    "Applied timeout ({Minutes}m) to user {UserId} in guild {GuildId} due to {Level} risk score",
                    config.AccountScoringTimeoutMinutes, user.Id, user.Guild.Id, scoreResult.ThreatLevel);
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to timeout user {UserId} in guild {GuildId} after account scoring",
                    user.Id, user.Guild.Id);
            }
        }

        if (!config.LogChannelId.HasValue) {
            logger.LogWarning(
                "Cannot send scoring log for user {UserId} in guild {GuildId}: no log channel configured",
                user.Id, user.Guild.Id);
            return;
        }

        await SendScoringLogAsync(user.Guild, config, user, scoreResult, shouldTimeout);
    }

    /// <summary>
    /// Deletes all active invite links in the guild to prevent further access during a raid.
    /// </summary>
    /// <param name="guild">The guild whose invites should be deleted.</param>
    private async Task PauseInvitesAsync(SocketGuild guild) {
        try {
            var invites = await guild.GetInvitesAsync();
            foreach (var invite in invites) {
                try {
                    await invite.DeleteAsync();
                } catch (Exception ex) {
                    logger.LogWarning(ex, "Failed to delete invite {Code} in guild {GuildId}",
                        invite.Code, guild.Id);
                }
            }

            logger.LogInformation("Deleted {Count} invite(s) in guild {GuildId} due to raid detection",
                invites.Count, guild.Id);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to retrieve invite list for guild {GuildId}", guild.Id);
        }
    }

    /// <summary>
    /// Sends a raid-alert embed to the configured log channel.
    /// </summary>
    /// <param name="guild">The guild where the raid was detected.</param>
    /// <param name="config">The spam configuration containing the log channel and raid settings.</param>
    /// <param name="triggeringUser">The guild member whose join triggered the raid detection.</param>
    private async Task SendRaidLogAsync(
        SocketGuild guild,
        Core.DTOs.SpamConfigurationDto config,
        SocketGuildUser triggeringUser
    ) {
        var channel = guild.GetTextChannel((ulong)config.LogChannelId!.Value);
        if (channel is null) {
            logger.LogWarning(
                "Cannot send raid log: channel {ChannelId} not found in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
            return;
        }

        var actionLine = config.IsDryRun
            ? "**[DRY RUN]** Invites would have been paused — no action taken."
            : $"All active invite links deleted. Invites remain paused for **{config.AntiRaidCooldownMinutes} minute(s)**.";

        var embed = new EmbedBuilder()
            .WithColor(Color.Orange)
            .WithTitle("🚨 Raid Detected — Invites Paused")
            .AddField("Threshold", $"{config.AntiRaidJoinThreshold} joins in {config.AntiRaidWindowSeconds}s", true)
            .AddField("Cooldown", $"{config.AntiRaidCooldownMinutes} minute(s)", true)
            .AddField("Triggering user", $"{triggeringUser.Mention} (`{triggeringUser.Id}`)")
            .AddField("Action", actionLine)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        try {
            await channel.SendMessageAsync(embed: embed);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to send raid log to channel {ChannelId} in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
        }
    }

    /// <summary>
    /// Sends an account-scoring alert embed to the configured log channel when a medium
    /// or high risk account joins the guild.
    /// </summary>
    /// <param name="guild">The guild where the suspicious account joined.</param>
    /// <param name="config">The spam configuration containing the log channel and scoring settings.</param>
    /// <param name="user">The guild member whose account was flagged.</param>
    /// <param name="scoreResult">The scoring result containing the threat level, score, and risk factors.</param>
    /// <param name="wasTimedOut">Whether a timeout was applied to the user.</param>
    private async Task SendScoringLogAsync(
        SocketGuild guild,
        Core.DTOs.SpamConfigurationDto config,
        SocketGuildUser user,
        AccountScoreResult scoreResult,
        bool wasTimedOut
    ) {
        var channel = guild.GetTextChannel((ulong)config.LogChannelId!.Value);
        if (channel is null) {
            logger.LogWarning(
                "Cannot send scoring log: channel {ChannelId} not found in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
            return;
        }

        var color = scoreResult.ThreatLevel == ThreatLevel.High ? Color.Red : Color.Gold;
        var levelEmoji = scoreResult.ThreatLevel == ThreatLevel.High ? "🔴" : "🟡";
        var actionLine = wasTimedOut
            ? $"Timeout applied ({config.AccountScoringTimeoutMinutes} min)."
            : config.IsDryRun
                ? "**[DRY RUN]** No action taken."
                : "Alert only — no timeout applied.";

        var embed = new EmbedBuilder()
            .WithColor(color)
            .WithTitle($"{levelEmoji} Suspicious Account Joined — {scoreResult.ThreatLevel} Risk")
            .AddField("User", $"{user.Mention} (`{user.Id}`)", true)
            .AddField("Score", scoreResult.Score.ToString(), true)
            .AddField("Account age", $"{(int)(DateTimeOffset.UtcNow - user.CreatedAt).TotalDays} day(s)", true)
            .AddField("Risk factors", string.Join("\n", scoreResult.Factors))
            .AddField("Action", actionLine)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        try {
            await channel.SendMessageAsync(embed: embed);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to send scoring log to channel {ChannelId} in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
        }
    }
}
