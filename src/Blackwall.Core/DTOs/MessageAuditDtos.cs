using Blackwall.Core.Entities;

namespace Blackwall.Core.DTOs;

public sealed record MessageAuditEventSummaryDto(
    long Id,
    long DiscordUserId,
    string Username,
    long DiscordChannelId,
    string ChannelName,
    string Violations,
    InfractionAction Action,
    bool IsDryRun,
    DateTime CreatedAtUtc,
    int MessageCount
);

public sealed record MessageAuditMessageDto(
    long Id,
    long DiscordMessageId,
    long DiscordUserId,
    string Username,
    long DiscordChannelId,
    string ChannelName,
    string Content,
    List<EmbedDataDto> Embeds,
    DateTime MessageTimestampUtc
);

public sealed record MessageAuditEventDetailDto(
    long Id,
    long DiscordUserId,
    string Username,
    string? AvatarHash,
    long DiscordChannelId,
    string ChannelName,
    string Violations,
    InfractionAction Action,
    bool IsDryRun,
    DateTime CreatedAtUtc,
    List<MessageAuditMessageDto> Messages
);
