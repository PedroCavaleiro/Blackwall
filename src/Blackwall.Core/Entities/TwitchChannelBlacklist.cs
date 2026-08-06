// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelBlacklist : EntityBase {
    public long TwitchChannelConfigurationId { get; set; }
    public TwitchChannelConfiguration TwitchChannelConfiguration { get; set; } = null!;

    public string Url { get; set; } = string.Empty;
}
