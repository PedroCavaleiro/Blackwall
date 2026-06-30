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

    /// <summary>
    /// Retrieves all text channels visible to the bot in the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A read-only list of text channels, or an empty list if the request fails.</returns>
    public async Task<IReadOnlyList<DiscordChannelDto>> GetChannelsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/channels");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DiscordChannelDto>>(ct) ?? [];
    }

    /// <summary>
    /// Removes the bot from the specified Discord guild and deactivates the guild instance.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task RemoveBotAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Activates a lockdown on the specified guild, denying Send Messages for @@everyone in all text channels.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task LockdownAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/lockdown");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Lifts the lockdown on the specified guild, restoring Send Messages permissions.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task UnlockAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/unlock");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DefaultBlacklistResponse>> GetDefaultBlacklistsAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/guilds/blacklists/defaults");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DefaultBlacklistResponse>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<BlacklistResponse>> GetBlacklistsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/blacklists");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BlacklistResponse>>(ct) ?? [];
    }

    public async Task<BlacklistResponse?> AddBlacklistAsync(long discordGuildId, string url, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/blacklists");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBlacklistRequest(url));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlacklistResponse>(ct);
    }

    public async Task RemoveBlacklistAsync(long discordGuildId, long blacklistId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/blacklists/{blacklistId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RefreshBlacklistsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/blacklists/refresh");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<BlacklistDomainResponse>> GetBlacklistDomainsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/blacklists/domains");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BlacklistDomainResponse>>(ct) ?? [];
    }

    public async Task<BlacklistDomainResponse?> AddBlacklistDomainAsync(long discordGuildId, string domain, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/blacklists/domains");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBlacklistDomainRequest(domain));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlacklistDomainResponse>(ct);
    }

    public async Task RemoveBlacklistDomainAsync(long discordGuildId, long domainId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/blacklists/domains/{domainId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
