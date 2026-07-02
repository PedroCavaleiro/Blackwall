// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class GuildBan : EntityBase {
    public long GuildInstanceId { get; set; }
    public GuildInstance GuildInstance { get; set; } = null!;

    public long DiscordUserId { get; set; }
    public string? Username { get; set; }
    public string? Reason { get; set; }
    public DateTime? BannedAtUtc { get; set; }
}
