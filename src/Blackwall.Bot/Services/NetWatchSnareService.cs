using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Blackwall.Infrastructure.Cache;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Blackwall.Bot.Services;

public sealed class NetWatchSnareService(
    NetWatchSnareChannelCache netWatchSnareCache,
    ILogger<NetWatchSnareService> logger
) {
    /// <summary>
    /// Checks whether the given channel is an active netWatchSnare (trap) channel for the guild.
    /// Returns the matching netWatchSnare configuration if the channel is trapped and enabled.
    /// </summary>
    public async Task<NetWatchSnareChannelDto?> GetTriggeredNetWatchSnareAsync(
        long discordGuildId,
        ulong channelId
    ) {
        var channels = await netWatchSnareCache.GetByDiscordGuildIdAsync(discordGuildId);
        if (channels is null || channels.Count == 0)
            return null;

        return channels.FirstOrDefault(s => s.DiscordChannelId == (long)channelId && s.IsEnabled);
    }

    /// <summary>
    /// Applies the configured netWatchSnare action against the user who triggered the trap channel.
    /// </summary>
    public async Task ApplyNetWatchSnareActionAsync(
        SocketUserMessage message,
        SocketGuild guild,
        NetWatchSnareChannelDto netWatchSnare
    ) {
        var guildUser = guild.GetUser(message.Author.Id);
        if (guildUser is null)
            return;

        var pruneDays = Math.Clamp(netWatchSnare.MessageDeleteDays, 0, 7);
        var timeout = TimeSpan.FromMinutes(Math.Max(1, netWatchSnare.TimeoutMinutes));

        try {
            switch (netWatchSnare.Action) {
                case InfractionAction.Timeout:
                    await guildUser.SetTimeOutAsync(timeout);
                    break;
                case InfractionAction.Kick:
                    await guildUser.KickAsync("NetWatch Snare trap triggered — Blackwall.");
                    break;
                case InfractionAction.Ban:
                    await guild.AddBanAsync(guildUser, pruneDays, "NetWatch Snare trap triggered — Blackwall.");
                    break;
                case InfractionAction.DeleteOnly:
                default:
                    break;
            }

            logger.LogInformation(
                "NetWatchSnare triggered in guild {GuildId} channel {ChannelId} by user {UserId}: action={Action}",
                guild.Id, netWatchSnare.DiscordChannelId, message.Author.Id, netWatchSnare.Action);
        } catch (Exception ex) {
            logger.LogWarning(ex,
                "Failed to apply netWatchSnare action {Action} against user {UserId} in guild {GuildId}",
                netWatchSnare.Action, message.Author.Id, guild.Id);
        }
    }

    /// <summary>
    /// Sends a netWatchSnare trigger log embed to the configured log channel.
    /// </summary>
    public async Task SendNetWatchSnareLogAsync(
        SocketUserMessage message,
        SocketGuild guild,
        NetWatchSnareChannelDto netWatchSnare,
        long? logChannelId
    ) {
        if (logChannelId is null)
            return;

        var channel = guild.GetTextChannel((ulong)logChannelId.Value);
        if (channel is null)
            return;

        var actionLabel = netWatchSnare.Action switch {
            InfractionAction.Timeout => $"Timeout ({netWatchSnare.TimeoutMinutes}m)",
            InfractionAction.Kick => "Kick",
            InfractionAction.Ban => "Ban",
            _ => netWatchSnare.Action.ToString()
        };

        var embed = new EmbedBuilder()
            .WithColor(Color.Red)
            .WithTitle("🪤 NetWatchSnare Trap Triggered")
            .AddField("User", $"{message.Author.Mention} (`{message.Author.Id}`)", true)
            .AddField("Trap Channel", $"<#{netWatchSnare.DiscordChannelId}>", true)
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
                "Failed to send netWatchSnare log to channel {ChannelId} in guild {GuildId}",
                logChannelId.Value, guild.Id);
        }
    }
}
