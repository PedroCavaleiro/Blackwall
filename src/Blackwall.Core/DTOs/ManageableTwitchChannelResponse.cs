// ReSharper disable NotAccessedPositionalProperty.Global
namespace Blackwall.Core.DTOs;

public sealed record ManageableTwitchChannelResponse(
    long TwitchUserId,
    string Username,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsOwner,
    bool BotInstalled,
    bool CanOpen
);
