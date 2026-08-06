// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class MessageAuditRecord : EntityBase {
    public long EventId { get; set; }
    public MessageAuditEvent Event { get; set; } = null!;

    public long DiscordMessageId { get; set; }
    public long DiscordUserId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarHash { get; set; }

    public long DiscordChannelId { get; set; }
    public string ChannelName { get; set; } = "";

    public string Content { get; set; } = "";
    public string EmbedsJson { get; set; } = "[]";

    public DateTime MessageTimestampUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
