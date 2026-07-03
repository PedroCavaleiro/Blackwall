// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public class MessageAuditEvent : EntityBase {
    public long GuildInstanceId { get; set; }
    public GuildInstance GuildInstance { get; set; } = null!;

    public long DiscordUserId { get; set; }
    public string Username { get; set; } = "";
    public string? AvatarHash { get; set; }

    public long DiscordChannelId { get; set; }
    public string ChannelName { get; set; } = "";

    public string Violations { get; set; } = "";
    public InfractionAction Action { get; set; }
    public bool IsDryRun { get; set; }

    public ICollection<MessageAuditRecord> Records { get; set; } = [];
}
