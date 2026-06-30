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
