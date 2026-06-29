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

        if (await spamDetectionService.IsRateLimitedAsync(
                discordGuildId, discordUserId,
                config.MaxMessagesPerWindow, config.RateLimitWindowSeconds))
            violations.Add("rate_limit");

        if (await spamDetectionService.IsDuplicateAsync(
                discordGuildId, discordUserId,
                (long)message.Channel.Id,
                message.Content, config.DuplicateMessageThreshold,
                config.DuplicateWindowSeconds, config.DuplicateCrossChannelEnabled))
            violations.Add("duplicate");

        if (SpamDetectionService.ExceedsMentionLimit(message, config.MentionLimit))
            violations.Add("mention_limit");

        if (config.BlockInviteLinks && SpamDetectionService.ContainsInviteLink(message.Content))
            violations.Add("invite_link");

        if (config.BlockSuspiciousLinks && SpamDetectionService.ContainsSuspiciousLink(message.Content))
            violations.Add("suspicious_link");

        if (violations.Count == 0)
            return;

        var violationSummary = string.Join(", ", violations);

        logger.LogInformation(
            "Spam detected in guild {GuildId} from user {UserId}: {Violations} (DryRun={DryRun})",
            discordGuildId, discordUserId, violationSummary, config.IsDryRun);

        if (!config.IsDryRun) {
            try {
                await message.DeleteAsync();
            } catch (Exception ex) {
                logger.LogWarning(ex,
                    "Failed to delete spam message {MessageId} in guild {GuildId}",
                    message.Id, discordGuildId);
            }

            await ApplyActionAsync(message, guildChannel.Guild, config.Action, config.MessageDeleteDays);
        }

        if (config.LogChannelId.HasValue)
            await SendLogMessageAsync(message, guildChannel.Guild, violations, config.Action, config.IsDryRun);
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
            .AddField("Triggers", string.Join(", ", violations), false)
            .AddField("Message", message.Content.Length > 1024
                ? message.Content[..1021] + "..."
                : message.Content, false)
            .AddField("Action", actionLabel, false)
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
