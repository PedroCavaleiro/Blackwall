using System.Net.Http.Headers;
using Blackwall.Core.DTOs;

namespace Blackwall.Web.Services;

public sealed class BlackwallApiService(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor
) {
    /// <summary>
    /// Attaches the JWT from the <c>blackwall_jwt</c> cookie as a Bearer authorization header on the outgoing request.
    /// </summary>
    /// <param name="request">The HTTP request message to authorize.</param>
    private void ApplyAuth(HttpRequestMessage request) {
        var token = httpContextAccessor.HttpContext?.Request.Cookies["blackwall_jwt"];
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Retrieves all Discord guilds the authenticated user can manage.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A read-only list of manageable guilds, or <c>null</c> if deserialization returns nothing.</returns>
    public async Task<IReadOnlyList<ManageableGuildResponse>?> GetGuildsAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/guilds");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ManageableGuildResponse>>(ct);
    }

    /// <summary>
    /// Retrieves the current settings for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID to retrieve settings for.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The guild settings, or <c>null</c> if deserialization returns nothing.</returns>
    public async Task<GuildSettingsResponse?> GetGuildSettingsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/settings");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GuildSettingsResponse>(ct);
    }

    /// <summary>
    /// Updates the spam configuration settings for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID to update settings for.</param>
    /// <param name="dto">The updated settings to apply.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task UpdateGuildSettingsAsync(long discordGuildId, UpdateGuildSettingsRequest dto, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/guilds/{discordGuildId}/settings");
        ApplyAuth(request);
        request.Content = JsonContent.Create(dto);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Retrieves the Discord bot invite URL pre-selected for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild ID to pre-select in the invite flow.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The bot invite URL, or <c>null</c> if the request fails or the response cannot be deserialized.</returns>
    public async Task<string?> GetBotInviteUrlAsync(long guildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/bot/invite?guildId={guildId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<BotInviteResponse>(ct);
        return result?.Url;
    }
}
