namespace Blackwall.Core.DTOs;

public sealed record SafeBrowsingSyncResponse(
    bool Success,
    string? Error,
    long GlobalCacheEntries,
    long ThreatEntries,
    bool Synced
);
