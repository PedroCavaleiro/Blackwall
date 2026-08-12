namespace Blackwall.Core.DTOs;

public sealed record BlacklistResponse(
    long Id,
    string Url
);

public sealed record AddBlacklistRequest(
    string Url
);

public sealed record DefaultBlacklistResponse(
    string Url
);

public sealed record BlacklistDomainResponse(
    long Id,
    string Domain
);

public sealed record AddBlacklistDomainRequest(
    string Domain
);

public sealed record BannedWordResponse(
    long Id,
    string Word,
    bool IsRegex
);

public sealed record AddBannedWordRequest(
    string Word,
    bool IsRegex = false
);

public sealed record AllowedBotResponse(
    long Id,
    long DiscordBotId,
    string BotUsername
);

public sealed record AddAllowedBotRequest(
    long DiscordBotId,
    string BotUsername
);
