// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelConfiguration : EntityBase {
    public long TwitchChannelInstanceId { get; set; }
    public TwitchChannelInstance TwitchChannelInstance { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;
    public bool IsDryRun { get; set; } = false;
    public bool AutoAddManagers { get; set; } = true;
    public string CommandTrigger { get; set; } = "!";

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<TwitchAllowedBot> AllowedBots { get; set; } = [];
}
