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

    /// <summary>
    /// Retrieves the list of default blacklist URLs available for selection.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A read-only list of default blacklists, or an empty list if deserialization returns nothing.</returns>
    public async Task<IReadOnlyList<DefaultBlacklistResponse>> GetDefaultBlacklistsAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/guilds/blacklists/defaults");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DefaultBlacklistResponse>>(ct) ?? [];
    }

    /// <summary>
    /// Retrieves all blacklists configured for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A read-only list of blacklists, or an empty list if deserialization returns nothing.</returns>
    public async Task<IReadOnlyList<BlacklistResponse>> GetBlacklistsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/blacklists");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BlacklistResponse>>(ct) ?? [];
    }

    /// <summary>
    /// Adds a blacklist URL to the specified guild's spam configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="url">The blacklist URL to add.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The created blacklist, or <c>null</c> if deserialization returns nothing.</returns>
    public async Task<BlacklistResponse?> AddBlacklistAsync(long discordGuildId, string url, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/blacklists");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBlacklistRequest(url));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlacklistResponse>(ct);
    }

    /// <summary>
    /// Removes a specific blacklist from the specified guild's spam configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="blacklistId">The ID of the blacklist to remove.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task RemoveBlacklistAsync(long discordGuildId, long blacklistId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/blacklists/{blacklistId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Triggers a refresh of the cached blacklist data for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task RefreshBlacklistsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/blacklists/refresh");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Retrieves all custom blacklist domains configured for the specified guild.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A read-only list of custom blacklist domains, or an empty list if deserialization returns nothing.</returns>
    public async Task<IReadOnlyList<BlacklistDomainResponse>> GetBlacklistDomainsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/blacklists/domains");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BlacklistDomainResponse>>(ct) ?? [];
    }

    /// <summary>
    /// Adds a custom blacklist domain to the specified guild's spam configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="domain">The domain to blacklist.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The created blacklist domain entry, or <c>null</c> if deserialization returns nothing.</returns>
    public async Task<BlacklistDomainResponse?> AddBlacklistDomainAsync(long discordGuildId, string domain, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/blacklists/domains");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBlacklistDomainRequest(domain));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlacklistDomainResponse>(ct);
    }

    /// <summary>
    /// Removes a specific custom blacklist domain from the specified guild's spam configuration.
    /// </summary>
    /// <param name="discordGuildId">The Discord guild ID.</param>
    /// <param name="domainId">The ID of the blacklist domain to remove.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    public async Task RemoveBlacklistDomainAsync(long discordGuildId, long domainId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/blacklists/domains/{domainId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateShareBanListAsync(long discordGuildId, bool shareBanList, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/guilds/{discordGuildId}/bans/share");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateShareBanListRequest(shareBanList));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SharedBanListGuildResponse>> GetSharedBanListGuildsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/bans/shared-guilds");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SharedBanListGuildResponse>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<GuildBanResponse>> GetBansAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/bans");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<GuildBanResponse>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<GuildBanResponse>> GetSourceGuildBansAsync(long discordGuildId, long sourceDiscordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/bans/source/{sourceDiscordGuildId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<GuildBanResponse>>(ct) ?? [];
    }

    public async Task SyncBansAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/bans/sync");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ImportBansResultResponse?> ImportBansAsync(long discordGuildId, long sourceDiscordGuildId, IReadOnlyList<long>? discordUserIds = null, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/bans/import");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new ImportBansRequest(sourceDiscordGuildId, discordUserIds));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ImportBansResultResponse>(ct);
    }

    public async Task<IReadOnlyList<BanSyncRuleResponse>> GetBanSyncRulesAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/bans/auto-sync");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BanSyncRuleResponse>>(ct) ?? [];
    }

    public async Task<BanSyncRuleResponse?> AddBanSyncRuleAsync(long discordGuildId, long sourceDiscordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/bans/auto-sync");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBanSyncRuleRequest(sourceDiscordGuildId));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BanSyncRuleResponse>(ct);
    }

    public async Task UpdateBanSyncRuleAsync(long discordGuildId, long ruleId, bool isEnabled, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/guilds/{discordGuildId}/bans/auto-sync/{ruleId}");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateBanSyncRuleRequest(isEnabled));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteBanSyncRuleAsync(long discordGuildId, long ruleId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/bans/auto-sync/{ruleId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<BannedWordResponse>> GetBannedWordsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/banned-words");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BannedWordResponse>>(ct) ?? [];
    }

    public async Task<BannedWordResponse?> AddBannedWordAsync(long discordGuildId, string word, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/banned-words");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBannedWordRequest(word));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BannedWordResponse>(ct);
    }

    public async Task RemoveBannedWordAsync(long discordGuildId, long wordId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/banned-words/{wordId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AllowedBotResponse>> GetAllowedBotsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/allowed-bots");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<AllowedBotResponse>>(ct) ?? [];
    }

    public async Task<AllowedBotResponse?> AddAllowedBotAsync(long discordGuildId, long discordBotId, string botUsername, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/allowed-bots");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddAllowedBotRequest(discordBotId, botUsername));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AllowedBotResponse>(ct);
    }

    public async Task RemoveAllowedBotAsync(long discordGuildId, long botId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/allowed-bots/{botId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
