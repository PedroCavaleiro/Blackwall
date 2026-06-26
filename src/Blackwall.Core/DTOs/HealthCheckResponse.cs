namespace Blackwall.Core.DTOs;

public sealed record HealthCheckResponse(
    string Status,
    bool Postgres,
    bool Redis,
    DateTime Utc
);