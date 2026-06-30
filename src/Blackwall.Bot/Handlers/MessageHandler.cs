using Blackwall.Bot.Services;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Handlers;

public sealed class MessageHandler(
    IServiceScopeFactory scopeFactory,
    SpamDetectionService spamDetectionService,
    LockdownService lockdownService,
    SafeBrowsingService safeBrowsingService,
    ILogger<MessageHandler> logger
) {
    /// <summary>
    /// Evaluates an incoming message against the guild's active spam configuration.
    /// Applies the configured infraction action (or dry-runs it) and logs to the configured channel.
    /// </summary>
    public async Task OnMessageReceivedAsync(SocketMessage rawMessage) {
        if (rawMessage is not SocketUserMessage message)
            return;

        if (message.Author.IsBot || message.Author.IsWebhook)
            return;

        if (message.Channel is not SocketGuildChannel guildChannel)
            return;

        var discordGuildId = (long)guildChannel.Guild.Id;
        var discordUserId = (long)message.Author.Id;

        Core.DTOs.SpamConfigurationDto? config;

        using (var scope = scopeFactory.CreateScope()) {
            var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
            config = await cache.GetByDiscordGuildIdAsync(discordGuildId);
        }

        if (config is null || !config.IsEnabled)
            return;

        var violations = new List<string>(5);
        var triggeredActions = new List<InfractionAction>(5);
        var shouldLockdown = false;

        if (await spamDetectionService.IsRateLimitedAsync(
                discordGuildId, discordUserId,
                config.MaxMessagesPerWindow, config.RateLimitWindowSeconds)) {
            violations.Add("rate_limit");
            triggeredActions.Add(config.RateLimitAction ?? config.Action);
            if ((config.RateLimitAutoLockdown ?? config.AutoLockdownEnabled) && !config.IsLockedDown)
                shouldLockdown = true;
        }

        if (await spamDetectionService.IsDuplicateAsync(
                discordGuildId, discordUserId,
                (long)message.Channel.Id,
                message.Content, config.DuplicateMessageThreshold,
                config.DuplicateWindowSeconds, config.DuplicateCrossChannelEnabled)) {
            violations.Add("duplicate");
            triggeredActions.Add(config.DuplicateAction ?? config.Action);
            if ((config.DuplicateAutoLockdown ?? config.AutoLockdownEnabled) && !config.IsLockedDown)
                shouldLockdown = true;
        }

        if (SpamDetectionService.ExceedsMentionLimit(message, config.MentionLimit)) {
            violations.Add("mention_limit");
            triggeredActions.Add(config.MentionLimitAction ?? config.Action);
            if ((config.MentionLimitAutoLockdown ?? config.AutoLockdownEnabled) && !config.IsLockedDown)
                shouldLockdown = true;
        }

        if (config.BlockInviteLinks && await SpamDetectionService.ContainsInviteLinkWithRedirectAsync(message.Content)) {
            violations.Add("invite_link");
            triggeredActions.Add(config.InviteLinkAction ?? config.Action);
            if ((config.InviteLinkAutoLockdown ?? config.AutoLockdownEnabled) && !config.IsLockedDown)
                shouldLockdown = true;
        }

        if (config.BlockSuspiciousLinks) {
            using var blacklistScope = scopeFactory.CreateScope();
            var blacklistService = blacklistScope.ServiceProvider.GetRequiredService<BlacklistService>();

            var blockedByBlacklist = await SpamDetectionService.ContainsBlacklistedLinkAsync(message.Content, blacklistService, discordGuildId);

            if (blockedByBlacklist) {
                violations.Add("suspicious_link");
                triggeredActions.Add(config.SuspiciousLinkAction ?? config.Action);
                if ((config.SuspiciousLinkAutoLockdown ?? config.AutoLockdownEnabled) && !config.IsLockedDown)
                    shouldLockdown = true;
            } else if (config.SafeBrowsingEnabled) {
                var sbResult = await SpamDetectionService.CheckSafeBrowsingAsync(message.Content, safeBrowsingService);

                if (sbResult == SafeBrowsingResult.Unsafe
                    || (sbResult == SafeBrowsingResult.Unsure && config.SafeBrowsingBlockUnsure)) {
                    violations.Add("safe_browsing");
                    triggeredActions.Add(config.SuspiciousLinkAction ?? config.Action);
                    if ((config.SuspiciousLinkAutoLockdown ?? config.AutoLockdownEnabled) && !config.IsLockedDown)
                        shouldLockdown = true;
                }
            }
        }

        if (violations.Count == 0)
            return;

        var violationSummary = string.Join(", ", violations);
        var effectiveAction = GetMostSevereAction(triggeredActions);

        logger.LogInformation(
            "Spam detected in guild {GuildId} from user {UserId}: {Violations} (DryRun={DryRun}, Action={Action})",
            discordGuildId, discordUserId, violationSummary, config.IsDryRun, effectiveAction);

        if (!config.IsDryRun) {
            try {
                await message.DeleteAsync();
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to delete spam message {MessageId} in guild {GuildId}",
                    message.Id, discordGuildId);
            }

            await ApplyActionAsync(message, guildChannel.Guild, effectiveAction, config.MessageDeleteDays);

            if (shouldLockdown) {
                logger.LogWarning(
                    "Auto-lockdown triggered for guild {GuildId} due to infraction from user {UserId}: {Violations}",
                    discordGuildId, discordUserId, violationSummary);

                _ = Task.Run(() => lockdownService.LockdownAsync(guildChannel.Guild.Id));
            }
        }

        if (config.LogChannelId.HasValue)
            await SendLogMessageAsync(message, guildChannel.Guild, violations, effectiveAction, config.IsDryRun);
    }

    /// <summary>
    /// Returns the most severe action from the list of triggered module actions.
    /// Severity order: DeleteOnly &lt; Timeout &lt; Kick &lt; Ban.
    /// </summary>
    private static InfractionAction GetMostSevereAction(IReadOnlyList<InfractionAction> actions) {
        var max = InfractionAction.DeleteOnly;
        foreach (var action in actions) {
            if (action > max)
                max = action;
        }
        return max;
    }

    /// <summary>
    /// Applies the configured infraction action against the offending user.
    /// </summary>
    private async Task ApplyActionAsync(
        SocketUserMessage message,
        SocketGuild guild,
        InfractionAction action,
        int deleteMessageDays
    ) {
        var guildUser = guild.GetUser(message.Author.Id);
        if (guildUser is null)
            return;

        var pruneDays = Math.Clamp(deleteMessageDays, 0, 7);

        try {
            switch (action) {
                case InfractionAction.Timeout:
                    await guildUser.SetTimeOutAsync(TimeSpan.FromMinutes(10));
                    break;
                case InfractionAction.Kick:
                    await guildUser.KickAsync("Spam violation detected by Blackwall.");
                    break;
                case InfractionAction.Ban:
                    await guild.AddBanAsync(guildUser, pruneDays, "Spam violation detected by Blackwall.");
                    break;
                case InfractionAction.DeleteOnly:
                default:
                    break;
            }
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to apply action {Action} against user {UserId} in guild {GuildId}",
                action, message.Author.Id, guild.Id);
        }
    }

    /// <summary>
    /// Sends an infraction log embed to the configured log channel.
    /// </summary>
    private async Task SendLogMessageAsync(
        SocketUserMessage message,
        SocketGuild guild,
        IReadOnlyList<string> violations,
        InfractionAction action,
        bool isDryRun
    ) {
        using var scope = scopeFactory.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<SpamConfigurationCache>();
        var config = await cache.GetByDiscordGuildIdAsync((long)guild.Id);

        if (config?.LogChannelId is null)
            return;

        var channel = guild.GetTextChannel((ulong)config.LogChannelId.Value);
        if (channel is null)
            return;

        var actionLabel = isDryRun
            ? $"[DRY RUN] Would have applied: **{action}**"
            : $"**{action}**";

        var embed = new EmbedBuilder()
            .WithColor(isDryRun ? Color.Gold : Color.Red)
            .WithTitle(isDryRun ? "⚠️ Dry Run — Infraction Detected" : "🛡️ Infraction Action Taken")
            .AddField("User", $"{message.Author.Mention} (`{message.Author.Id}`)", true)
            .AddField("Channel", $"<#{message.Channel.Id}>", true)
            .AddField("Triggers", string.Join(", ", violations))
            .AddField("Message", message.Content.Length > 1024
                ? message.Content[..1021] + "..."
                : message.Content)
            .AddField("Action", actionLabel)
            .WithTimestamp(message.Timestamp)
            .Build();

        try {
            await channel.SendMessageAsync(embed: embed);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to send log message to channel {ChannelId} in guild {GuildId}",
                config.LogChannelId.Value, guild.Id);
        }
    }
}
