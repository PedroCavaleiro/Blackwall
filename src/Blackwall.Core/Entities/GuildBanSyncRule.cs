// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class GuildBanSyncRule : EntityBase {
    public long TargetGuildInstanceId { get; set; }
    public GuildInstance TargetGuildInstance { get; set; } = null!;

    public long SourceDiscordGuildId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime LastSyncedAtUtc { get; set; }
}
