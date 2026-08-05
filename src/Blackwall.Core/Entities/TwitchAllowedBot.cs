// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class TwitchAllowedBot : EntityBase {
    public long TwitchChannelConfigurationId { get; set; }
    public TwitchChannelConfiguration TwitchChannelConfiguration { get; set; } = null!;

    public string BotUsername { get; set; } = string.Empty;
}
