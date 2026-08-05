// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public sealed class TwitchRemovedManager : EntityBase {
    public long TwitchChannelInstanceId { get; set; }
    public long UserId { get; set; }
}
