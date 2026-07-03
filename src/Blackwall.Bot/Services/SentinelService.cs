using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Services;

public sealed class SentinelService(
    SentinelChannelCache sentinelCache,
    ILogger<SentinelService> logger
) {
    /// <summary>
    /// Checks whether the given channel is an active sentinel (trap) channel for the guild.
    /// Returns the matching sentinel configuration if the channel is trapped and enabled.
    /// </summary>
    public async Task<SentinelChannelDto?> GetTriggeredSentinelAsync(
        long discordGuildId,
        ulong channelId
    ) {
        var channels = await sentinelCache.GetByDiscordGuildIdAsync(discordGuildId);
        if (channels is null || channels.Count == 0)
            return null;

        return channels.FirstOrDefault(s => s.DiscordChannelId == (long)channelId && s.IsEnabled);
    }

    /// <summary>
    /// Applies the configured sentinel action against the user who triggered the trap channel.
    /// </summary>
    public async Task ApplySentinelActionAsync(
        SocketUserMessage message,
        SocketGuild guild,
        SentinelChannelDto sentinel
    ) {
        var guildUser = guild.GetUser(message.Author.Id);
        if (guildUser is null)
            return;

        var pruneDays = Math.Clamp(sentinel.MessageDeleteDays, 0, 7);
        var timeout = TimeSpan.FromMinutes(Math.Max(1, sentinel.TimeoutMinutes));

        try {
            switch (sentinel.Action) {
                case SentinelAction.SoftBan:
                    await guild.AddBanAsync(guildUser, pruneDays, "Sentinel trap triggered — Blackwall.");
                    await guild.RemoveBanAsync(guildUser);
                    break;
                case SentinelAction.Ban:
                    await guild.AddBanAsync(guildUser, pruneDays, "Sentinel trap triggered — Blackwall.");
                    break;
                case SentinelAction.Timeout:
                    await guildUser.SetTimeOutAsync(timeout);
                    break;
                case SentinelAction.AssignRole:
                    if (sentinel.AssignRoleId.HasValue) {
                        var role = guild.GetRole((ulong)sentinel.AssignRoleId.Value);
                        if (role is not null)
                            await guildUser.AddRoleAsync(role);
                        else
                            logger.LogWarning("Sentinel role {RoleId} not found in guild {GuildId}", sentinel.AssignRoleId.Value, guild.Id);
                    }
                    break;
            }

            logger.LogInformation(
                "Sentinel triggered in guild {GuildId} channel {ChannelId} by user {UserId}: action={Action}",
                guild.Id, sentinel.DiscordChannelId, message.Author.Id, sentinel.Action);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to apply sentinel action {Action} against user {UserId} in guild {GuildId}",
                sentinel.Action, message.Author.Id, guild.Id);
        }
    }

    /// <summary>
    /// Sends a sentinel trigger log embed to the configured log channel.
    /// </summary>
    public async Task SendSentinelLogAsync(
        SocketUserMessage message,
        SocketGuild guild,
        SentinelChannelDto sentinel,
        long? logChannelId
    ) {
        if (logChannelId is null)
            return;

        var channel = guild.GetTextChannel((ulong)logChannelId.Value);
        if (channel is null)
            return;

        var actionLabel = sentinel.Action switch {
            SentinelAction.SoftBan => "Soft Ban",
            SentinelAction.Ban => "Ban",
            SentinelAction.Timeout => $"Timeout ({sentinel.TimeoutMinutes}m)",
            SentinelAction.AssignRole => "Assign Role",
            _ => sentinel.Action.ToString()
        };

        var embed = new EmbedBuilder()
            .WithColor(Color.Red)
            .WithTitle("🪤 Sentinel Trap Triggered")
            .AddField("User", $"{message.Author.Mention} (`{message.Author.Id}`)", true)
            .AddField("Trap Channel", $"<#{sentinel.DiscordChannelId}>", true)
            .AddField("Action", $"**{actionLabel}**", true)
            .AddField("Message", message.Content.Length > 1024
                ? message.Content[..1021] + "..."
                : message.Content)
            .WithTimestamp(message.Timestamp)
            .Build();

        try {
            await channel.SendMessageAsync(embed: embed);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to send sentinel log to channel {ChannelId} in guild {GuildId}",
                logChannelId.Value, guild.Id);
        }
    }
}
