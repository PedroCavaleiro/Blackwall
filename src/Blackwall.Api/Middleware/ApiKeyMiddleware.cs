using Blackwall.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Blackwall.Api.Middleware;

public sealed class ApiKeyMiddleware(
    RequestDelegate next,
    IOptions<ApiOptions> apiOptions
) {
    private const string ApiKeyHeader = "X-API-Key";
    private static readonly string[] ExemptPaths = [
        "/api/auth/discord/callback",
        "/api/auth/twitch/callback",
        "/api/system/health",
        "/health"
    ];

    public async Task InvokeAsync(HttpContext context) {
        var options = apiOptions.Value;

        if (!options.ProtectionEnabled) {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (ExemptPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            string.IsNullOrWhiteSpace(providedKey) ||
            !string.Equals(providedKey, options.Key, StringComparison.Ordinal)) {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"title\":\"Unauthorized\",\"detail\":\"Missing or invalid API key.\",\"status\":401}");
            return;
        }

        await next(context);
    }
}
