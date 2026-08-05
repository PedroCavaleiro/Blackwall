// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Core.Entities;

public class TwitchChannelManager : EntityBase {
    public long TwitchChannelInstanceId { get; set; }
    public TwitchChannelInstance TwitchChannelInstance { get; set; } = null!;

    public long UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public bool IsAdmin { get; set; }
}
