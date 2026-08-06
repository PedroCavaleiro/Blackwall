// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelDomainRule : EntityBase {
    public long TwitchChannelConfigurationId { get; set; }
    public TwitchChannelConfiguration TwitchChannelConfiguration { get; set; } = null!;

    public string Rule { get; set; } = string.Empty;
}
