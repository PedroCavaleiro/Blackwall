using System.Net.Http.Headers;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Microsoft.AspNetCore.Mvc;

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

    public async Task<BannedWordResponse?> AddBannedWordAsync(long discordGuildId, string word, bool isRegex = false, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/banned-words");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddBannedWordRequest(word, isRegex));
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

    public async Task<IReadOnlyList<MessageAuditEventSummaryDto>> GetAuditEventsAsync(long discordGuildId, int page = 1, int pageSize = 20, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/audit/events?page={page}&pageSize={pageSize}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<MessageAuditEventSummaryDto>>(ct) ?? [];
    }

    public async Task<MessageAuditEventDetailDto?> GetAuditEventDetailAsync(long discordGuildId, long eventId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/audit/events/{eventId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<MessageAuditEventDetailDto>(ct);
    }

    public async Task DeleteAuditEventAsync(long discordGuildId, long eventId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/audit/events/{eventId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAuditRecordAsync(long discordGuildId, long eventId, long recordId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/audit/events/{eventId}/records/{recordId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<NetWatchSnareChannelDto>> GetNetWatchSnareChannelsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/netWatchSnares");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<NetWatchSnareChannelDto>>(ct) ?? [];
    }

    public async Task<NetWatchSnareChannelDto?> CreateNetWatchSnareChannelAsync(long discordGuildId, CreateNetWatchSnareChannelRequest dto, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/netWatchSnares");
        ApplyAuth(request);
        request.Content = JsonContent.Create(dto);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NetWatchSnareChannelDto>(ct);
    }

    public async Task<NetWatchSnareChannelDto?> UpdateNetWatchSnareChannelAsync(long discordGuildId, long netWatchSnareId, UpdateNetWatchSnareChannelRequest dto, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/guilds/{discordGuildId}/netWatchSnares/{netWatchSnareId}");
        ApplyAuth(request);
        request.Content = JsonContent.Create(dto);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NetWatchSnareChannelDto>(ct);
    }

    public async Task DeleteNetWatchSnareChannelAsync(long discordGuildId, long netWatchSnareId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/netWatchSnares/{netWatchSnareId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAllNetWatchSnareChannelsAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/netWatchSnares");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<GuildModuleInstallationDto>> GetModulesAsync(long discordGuildId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/guilds/{discordGuildId}/modules");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<GuildModuleInstallationDto>>(ct) ?? [];
    }

    public async Task<GuildModuleInstallationDto?> InstallModuleAsync(long discordGuildId, string gitUrl, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/modules/install");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new InstallModuleRequest(gitUrl));
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            throw new Exception(problem?.Detail ?? problem?.Title ?? $"HTTP {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<GuildModuleInstallationDto>(ct);
    }

    public async Task UninstallModuleAsync(long discordGuildId, string moduleName, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/guilds/{discordGuildId}/modules/{Uri.EscapeDataString(moduleName)}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<GuildModuleInstallationDto?> UpdateModuleAsync(long discordGuildId, string moduleName, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/guilds/{discordGuildId}/modules/{Uri.EscapeDataString(moduleName)}/update");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            throw new Exception(problem?.Detail ?? problem?.Title ?? $"HTTP {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<GuildModuleInstallationDto>(ct);
    }

    public async Task SetModuleEnabledAsync(long discordGuildId, string moduleName, bool isEnabled, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/guilds/{discordGuildId}/modules/{Uri.EscapeDataString(moduleName)}/enabled");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateModuleEnabledRequest(isEnabled));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateModuleSettingsAsync(long discordGuildId, string moduleName, string settingsJson, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/guilds/{discordGuildId}/modules/{Uri.EscapeDataString(moduleName)}/settings");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateModuleSettingsRequest(settingsJson));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LinkedAccountsResponse?> GetLinkedAccountsAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/users/accounts");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LinkedAccountsResponse>(ct);
    }

    public async Task UpdateDisplayNameProviderAsync(string provider, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/users/accounts/display-name");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateDisplayNameProviderRequest(provider));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UnlinkAccountWarningResponse?> CheckUnlinkDiscordAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/users/accounts/unlink/discord/check");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UnlinkAccountWarningResponse>(ct);
    }

    public async Task UnlinkDiscordAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/users/accounts/discord");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnlinkTwitchAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/users/accounts/twitch");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LoginResponse?> GetTwitchLinkUrlAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/twitch/link");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
    }

    public async Task<LoginResponse?> GetDiscordLinkUrlAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/discord/link");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
    }

    public async Task DismissLinkAccountsWarningAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/users/accounts/link-warning/dismiss");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ManageableTwitchChannelResponse>?> GetTwitchChannelsAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/twitchchannels");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ManageableTwitchChannelResponse>>(ct);
    }

    public async Task<string?> GetTwitchBotInstallUrlAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/twitchchannels/install");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<TwitchBotInstallResponse>(ct);
        return result?.Url;
    }

    public async Task<TwitchChannelSettingsResponse?> GetTwitchChannelSettingsAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/settings");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TwitchChannelSettingsResponse>(ct);
    }

    public async Task<TwitchChannelSettingsResponse?> UpdateTwitchChannelSettingsAsync(long twitchUserId, UpdateTwitchChannelSettingsRequest body, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/twitchchannels/{twitchUserId}/settings");
        ApplyAuth(request);
        request.Content = JsonContent.Create(body);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchChannelSettingsResponse>(ct);
    }

    public async Task<IReadOnlyList<TwitchAllowedBotResponse>> GetTwitchAllowedBotsAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/allowed-bots");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchAllowedBotResponse>>(ct) ?? [];
    }

    public async Task<TwitchAllowedBotResponse?> AddTwitchAllowedBotAsync(long twitchUserId, string botUsername, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/allowed-bots");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddTwitchAllowedBotRequest(botUsername));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchAllowedBotResponse>(ct);
    }

    public async Task RemoveTwitchAllowedBotAsync(long twitchUserId, long botId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/allowed-bots/{botId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveTwitchBotAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TwitchChannelManagerResponse>> GetTwitchChannelManagersAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/managers");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchChannelManagerResponse>>(ct) ?? [];
    }

    public async Task<TwitchChannelManagerResponse?> AddTwitchChannelManagerAsync(long twitchUserId, string username, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/managers");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddTwitchChannelManagerRequest(username));
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            throw new Exception(problem?.Detail ?? problem?.Title ?? $"HTTP {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<TwitchChannelManagerResponse>(ct);
    }

    public async Task RemoveTwitchChannelManagerAsync(long twitchUserId, long managerId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/managers/{managerId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DefaultBlacklistResponse>> GetTwitchDefaultBlacklistsAsync(CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/twitchchannels/blacklists/defaults");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DefaultBlacklistResponse>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<TwitchChannelBlacklistResponse>> GetTwitchBlacklistsAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/blacklists");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchChannelBlacklistResponse>>(ct) ?? [];
    }

    public async Task<TwitchChannelBlacklistResponse?> AddTwitchBlacklistAsync(long twitchUserId, string url, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/blacklists");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddTwitchChannelBlacklistRequest(url));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchChannelBlacklistResponse>(ct);
    }

    public async Task RemoveTwitchBlacklistAsync(long twitchUserId, long blacklistId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/blacklists/{blacklistId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RefreshTwitchBlacklistsAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/blacklists/refresh");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TwitchChannelDomainRuleResponse>> GetTwitchDomainRulesAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/domain-rules");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchChannelDomainRuleResponse>>(ct) ?? [];
    }

    public async Task<TwitchChannelDomainRuleResponse?> AddTwitchDomainRuleAsync(long twitchUserId, string rule, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/domain-rules");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddTwitchChannelDomainRuleRequest(rule));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchChannelDomainRuleResponse>(ct);
    }

    public async Task RemoveTwitchDomainRuleAsync(long twitchUserId, long ruleId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/domain-rules/{ruleId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TwitchBannedWordResponse>> GetTwitchBannedWordsAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/banned-words");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchBannedWordResponse>>(ct) ?? [];
    }

    public async Task<TwitchBannedWordResponse?> AddTwitchBannedWordAsync(long twitchUserId, string word, bool isRegex = false, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/banned-words");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddTwitchBannedWordRequest(word, isRegex));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchBannedWordResponse>(ct);
    }

    public async Task RemoveTwitchBannedWordAsync(long twitchUserId, long wordId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/banned-words/{wordId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TwitchChannelBanResponse>> GetTwitchChannelBansAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/bans");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchChannelBanResponse>>(ct) ?? [];
    }

    public async Task SyncTwitchBansAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/bans/sync");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new Exception(detail);
        }
    }

    public async Task UpdateTwitchShareBanListAsync(long twitchUserId, bool shareBanList, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/twitchchannels/{twitchUserId}/bans/share");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateTwitchShareBanListRequest(shareBanList));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<SharedBanListTwitchChannelResponse>> GetSharedBanListTwitchChannelsAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/bans/shared-channels");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SharedBanListTwitchChannelResponse>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<TwitchChannelBanResponse>> GetSourceTwitchChannelBansAsync(long twitchUserId, long sourceTwitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/bans/source/{sourceTwitchUserId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchChannelBanResponse>>(ct) ?? [];
    }

    public async Task<ImportTwitchBansResultResponse?> ImportTwitchBansAsync(long twitchUserId, long sourceTwitchUserId, IReadOnlyList<long>? twitchUserIds = null, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/bans/import");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new ImportTwitchBansRequest(sourceTwitchUserId, twitchUserIds));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ImportTwitchBansResultResponse>(ct);
    }

    public async Task<IReadOnlyList<TwitchBanSyncRuleResponse>> GetTwitchBanSyncRulesAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/bans/auto-sync");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchBanSyncRuleResponse>>(ct) ?? [];
    }

    public async Task<TwitchBanSyncRuleResponse?> AddTwitchBanSyncRuleAsync(long twitchUserId, long sourceTwitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/bans/auto-sync");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new AddTwitchBanSyncRuleRequest(sourceTwitchUserId));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchBanSyncRuleResponse>(ct);
    }

    public async Task UpdateTwitchBanSyncRuleAsync(long twitchUserId, long ruleId, bool isEnabled, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/twitchchannels/{twitchUserId}/bans/auto-sync/{ruleId}");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateTwitchBanSyncRuleRequest(isEnabled));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTwitchBanSyncRuleAsync(long twitchUserId, long ruleId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/bans/auto-sync/{ruleId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TwitchMessageAuditEventSummaryDto>> GetTwitchAuditEventsAsync(long twitchUserId, int page = 1, int pageSize = 20, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/audit/events?page={page}&pageSize={pageSize}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchMessageAuditEventSummaryDto>>(ct) ?? [];
    }

    public async Task<TwitchMessageAuditEventDetailDto?> GetTwitchAuditEventDetailAsync(long twitchUserId, long eventId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/audit/events/{eventId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TwitchMessageAuditEventDetailDto>(ct);
    }

    public async Task DeleteTwitchAuditEventAsync(long twitchUserId, long eventId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/audit/events/{eventId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteTwitchAuditRecordAsync(long twitchUserId, long eventId, long recordId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/audit/events/{eventId}/records/{recordId}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TwitchChannelModuleInstallationDto>> GetTwitchModulesAsync(long twitchUserId, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/twitchchannels/{twitchUserId}/modules");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<TwitchChannelModuleInstallationDto>>(ct) ?? [];
    }

    public async Task<TwitchChannelModuleInstallationDto?> InstallTwitchModuleAsync(long twitchUserId, string gitUrl, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/modules/install");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new InstallModuleRequest(gitUrl));
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            throw new Exception(problem?.Detail ?? problem?.Title ?? $"HTTP {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<TwitchChannelModuleInstallationDto>(ct);
    }

    public async Task UninstallTwitchModuleAsync(long twitchUserId, string moduleName, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/twitchchannels/{twitchUserId}/modules/{Uri.EscapeDataString(moduleName)}");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TwitchChannelModuleInstallationDto?> UpdateTwitchModuleAsync(long twitchUserId, string moduleName, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/twitchchannels/{twitchUserId}/modules/{Uri.EscapeDataString(moduleName)}/update");
        ApplyAuth(request);
        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
            throw new Exception(problem?.Detail ?? problem?.Title ?? $"HTTP {response.StatusCode}");
        }
        return await response.Content.ReadFromJsonAsync<TwitchChannelModuleInstallationDto>(ct);
    }

    public async Task SetTwitchModuleEnabledAsync(long twitchUserId, string moduleName, bool isEnabled, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/twitchchannels/{twitchUserId}/modules/{Uri.EscapeDataString(moduleName)}/enabled");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateModuleEnabledRequest(isEnabled));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateTwitchModuleSettingsAsync(long twitchUserId, string moduleName, string settingsJson, CancellationToken ct = default) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/twitchchannels/{twitchUserId}/modules/{Uri.EscapeDataString(moduleName)}/settings");
        ApplyAuth(request);
        request.Content = JsonContent.Create(new UpdateModuleSettingsRequest(settingsJson));
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
