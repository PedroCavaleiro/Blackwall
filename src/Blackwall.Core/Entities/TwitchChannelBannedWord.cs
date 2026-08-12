// ReSharper disable NullableWarningSuppressionIsUsed
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Blackwall.Core.Entities;

public sealed class TwitchChannelBannedWord : EntityBase {
    public long TwitchChannelConfigurationId { get; set; }
    public TwitchChannelConfiguration TwitchChannelConfiguration { get; set; } = null!;

    public string Word { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
}
