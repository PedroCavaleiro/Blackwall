namespace Blackwall.Core.DTOs;

public sealed record SafeBrowsingTestResponse(
    string Url,
    string Result,
    bool GlobalCacheSynced
);
