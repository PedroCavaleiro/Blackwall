using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Blackwall.Core.DTOs;
using Blackwall.Core.Entities;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Bot.Services;

public sealed record AiSentinelAnalysisResult(
    AiSentinelClassification Classification,
    string Reasoning
);

public sealed class AiSentinelService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiSentinelService> logger
) {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = null
    };

    private static readonly HttpClient HttpClient = new() {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string SystemPrompt = """
        You are an AI content moderation assistant for a Discord server.
        Analyze the following message and determine if it is malicious.
        Classify the message as exactly one of: "clean", "spam", "duplicate", "virus", or "scam".
        - "spam": Unsolicited repetitive advertising, mass mentions, or rate-limit abuse.
        - "duplicate": Repeated identical or near-identical content posted across channels.
        - "virus": Links or attachments intended to distribute malware or harmful payloads.
        - "scam": Phishing, social engineering, fake giveaways, or deceptive links targeting users.
        - "clean": The message does not appear to be malicious.
        Respond with ONLY a JSON object in this exact format:
        {"classification":"<one of clean|spam|duplicate|virus|scam>","reasoning":"<brief explanation>"}
        Do not include any other text, markdown, or formatting outside the JSON.
        """;

    /// <summary>
    /// Lists available models for the given provider configuration.
    /// Returns an empty list if the request fails.
    /// </summary>
    public async Task<IReadOnlyList<AiSentinelModelDto>> ListModelsAsync(
        AiSentinelProvider provider,
        string? apiKey,
        string? ollamaUrl,
        string? ollamaHeader1Key, string? ollamaHeader1Value,
        string? ollamaHeader2Key, string? ollamaHeader2Value,
        string? ollamaHeader3Key, string? ollamaHeader3Value,
        CancellationToken cancellationToken = default
    ) {
        try {
            return provider switch {
                AiSentinelProvider.OpenAi => await ListOpenAiModelsAsync(apiKey, cancellationToken),
                AiSentinelProvider.Anthropic => await ListAnthropicModelsAsync(apiKey, cancellationToken),
                AiSentinelProvider.GoogleGemini => await ListGeminiModelsAsync(apiKey, cancellationToken),
                AiSentinelProvider.Ollama => await ListOllamaModelsAsync(ollamaUrl, ollamaHeader1Key, ollamaHeader1Value, ollamaHeader2Key, ollamaHeader2Value, ollamaHeader3Key, ollamaHeader3Value, cancellationToken),
                _ => []
            };
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to list models for provider {Provider}", provider);
            return [];
        }
    }

    /// <summary>
    /// Analyzes a Discord message using the configured AI provider.
    /// Returns the classification and reasoning, or null if analysis fails.
    /// </summary>
    public async Task<AiSentinelAnalysisResult?> AnalyzeMessageAsync(
        SocketUserMessage message,
        AiSentinelConfigurationDto config,
        CancellationToken cancellationToken = default
    ) {
        var fullContent = SpamDetectionService.ExtractFullContent(message);

        if (string.IsNullOrWhiteSpace(fullContent))
            return new AiSentinelAnalysisResult(AiSentinelClassification.Clean, "Message has no content.");

        try {
            return config.Provider switch {
                AiSentinelProvider.OpenAi => await AnalyzeOpenAiAsync(fullContent, config, cancellationToken),
                AiSentinelProvider.Anthropic => await AnalyzeAnthropicAsync(fullContent, config, cancellationToken),
                AiSentinelProvider.GoogleGemini => await AnalyzeGeminiAsync(fullContent, config, cancellationToken),
                AiSentinelProvider.Ollama => await AnalyzeOllamaAsync(fullContent, config, cancellationToken),
                _ => null
            };
        } catch (Exception ex) {
            logger.LogWarning(ex, "AI analysis failed for provider {Provider} in guild", config.Provider);
            return null;
        }
    }

    /// <summary>
    /// Records an AI sentinel analysis result to the log store.
    /// </summary>
    public async Task LogAnalysisAsync(
        long discordGuildId,
        SocketUserMessage message,
        AiSentinelConfigurationDto config,
        AiSentinelAnalysisResult result,
        bool wouldAction,
        bool isDryRun,
        int retentionDays,
        CancellationToken cancellationToken = default
    ) {
        try {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

            var guildInstance = await dbContext.GuildInstances
                .Include(g => g.AiSentinelConfiguration)
                .FirstOrDefaultAsync(g => g.DiscordGuildId == discordGuildId, cancellationToken);

            if (guildInstance?.AiSentinelConfiguration is null)
                return;

            var channelName = (message.Channel as SocketGuildChannel)?.Name ?? "unknown";
            var author = message.Author;
            var expiresAt = DateTime.UtcNow.AddDays(Math.Clamp(retentionDays, 7, 90));
            var embeds = ExtractEmbeds(message);

            var log = new AiSentinelLog {
                AiSentinelConfigurationId = guildInstance.AiSentinelConfiguration.Id,
                DiscordMessageId = (long)message.Id,
                DiscordUserId = (long)author.Id,
                Username = $"{author.Username}#{author.Discriminator}",
                AvatarHash = author.AvatarId,
                DiscordChannelId = (long)message.Channel.Id,
                ChannelName = channelName,
                Content = message.Content ?? "",
                EmbedsJson = JsonSerializer.Serialize(embeds, JsonOptions),
                Classification = result.Classification,
                Reasoning = result.Reasoning,
                Provider = config.Provider,
                Model = config.Model ?? "",
                IsDryRun = isDryRun,
                WouldAction = wouldAction,
                MessageTimestampUtc = message.Timestamp.UtcDateTime,
                ExpiresAtUtc = expiresAt
            };

            dbContext.AiSentinelLogs.Add(log);
            await dbContext.SaveChangesAsync(cancellationToken);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to log AI sentinel analysis for guild {GuildId}", discordGuildId);
        }
    }

    /// <summary>
    /// Updates the training feedback for a specific AI sentinel log entry.
    /// </summary>
    public async Task UpdateTrainingFeedbackAsync(
        long discordGuildId,
        long logId,
        AiSentinelTrainingFeedback feedback,
        CancellationToken cancellationToken = default
    ) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

        var log = await dbContext.AiSentinelLogs
            .Include(l => l.AiSentinelConfiguration)
            .ThenInclude(c => c!.GuildInstance)
            .FirstOrDefaultAsync(l => l.Id == logId
                && l.AiSentinelConfiguration.GuildInstance.DiscordGuildId == discordGuildId,
                cancellationToken);

        if (log is null)
            return;

        log.TrainingFeedback = feedback;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Purges all AI sentinel logs that have expired past their retention period.
    /// </summary>
    public async Task PurgeExpiredAsync(CancellationToken cancellationToken = default) {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.BlackwallDbContext>();

        var now = DateTime.UtcNow;
        var expiredIds = await dbContext.AiSentinelLogs
            .Where(l => l.ExpiresAtUtc < now)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count > 0) {
            await dbContext.AiSentinelLogs
                .Where(l => expiredIds.Contains(l.Id))
                .ExecuteDeleteAsync(cancellationToken);
            logger.LogInformation("Purged {Count} expired AI sentinel logs", expiredIds.Count);
        }
    }

    #region Model Listing

    private static async Task<IReadOnlyList<AiSentinelModelDto>> ListOpenAiModelsAsync(
        string? apiKey, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiSentinelModelDto>();
        if (doc.RootElement.TryGetProperty("data", out var data)) {
            foreach (var item in data.EnumerateArray()) {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id is not null)
                    models.Add(new AiSentinelModelDto(id, id));
            }
        }

        return models.OrderBy(m => m.Name).ToList();
    }

    private static async Task<IReadOnlyList<AiSentinelModelDto>> ListAnthropicModelsAsync(
        string? apiKey, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=100");
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiSentinelModelDto>();
        if (doc.RootElement.TryGetProperty("data", out var data)) {
            foreach (var item in data.EnumerateArray()) {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var name = item.TryGetProperty("display_name", out var nameEl) ? nameEl.GetString() : id;
                if (id is not null)
                    models.Add(new AiSentinelModelDto(id, name ?? id));
            }
        }

        return models.OrderBy(m => m.Name).ToList();
    }

    private static async Task<IReadOnlyList<AiSentinelModelDto>> ListGeminiModelsAsync(
        string? apiKey, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiSentinelModelDto>();
        if (doc.RootElement.TryGetProperty("models", out var modelsEl)) {
            foreach (var item in modelsEl.EnumerateArray()) {
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var displayName = item.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : name;
                if (name is not null) {
                    var id = name.StartsWith("models/") ? name["models/".Length..] : name;
                    models.Add(new AiSentinelModelDto(id, displayName ?? id));
                }
            }
        }

        return models.OrderBy(m => m.Name).ToList();
    }

    private static async Task<IReadOnlyList<AiSentinelModelDto>> ListOllamaModelsAsync(
        string? ollamaUrl,
        string? h1k, string? h1v,
        string? h2k, string? h2v,
        string? h3k, string? h3v,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(ollamaUrl))
            return [];

        var url = ollamaUrl.TrimEnd('/') + "/api/tags";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyOllamaHeaders(req, h1k, h1v, h2k, h2v, h3k, h3v);

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiSentinelModelDto>();
        if (doc.RootElement.TryGetProperty("models", out var modelsEl)) {
            foreach (var item in modelsEl.EnumerateArray()) {
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (name is not null)
                    models.Add(new AiSentinelModelDto(name, name));
            }
        }

        return models.OrderBy(m => m.Name).ToList();
    }

    #endregion

    #region Analysis

    private static async Task<AiSentinelAnalysisResult?> AnalyzeOpenAiAsync(
        string content, AiSentinelConfigurationDto config, CancellationToken ct) {
        var payload = new {
            model = config.Model,
            messages = new object[] {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content }
            },
            temperature = 0
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var responseContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return ParseAnalysisResponse(responseContent);
    }

    private static async Task<AiSentinelAnalysisResult?> AnalyzeAnthropicAsync(
        string content, AiSentinelConfigurationDto config, CancellationToken ct) {
        var payload = new {
            model = config.Model,
            max_tokens = 300,
            system = SystemPrompt,
            messages = new object[] {
                new { role = "user", content }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var responseContent = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return ParseAnalysisResponse(responseContent);
    }

    private static async Task<AiSentinelAnalysisResult?> AnalyzeGeminiAsync(
        string content, AiSentinelConfigurationDto config, CancellationToken ct) {
        var payload = new {
            system_instruction = new {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = new object[] {
                new {
                    role = "user",
                    parts = new[] { new { text = content } }
                }
            },
            generationConfig = new { temperature = 0 }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent?key={config.ApiKey}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var responseContent = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return ParseAnalysisResponse(responseContent);
    }

    private static async Task<AiSentinelAnalysisResult?> AnalyzeOllamaAsync(
        string content, AiSentinelConfigurationDto config, CancellationToken ct) {
        var payload = new {
            model = config.Model,
            messages = new object[] {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content }
            },
            stream = false,
            options = new { temperature = 0 }
        };

        var url = (config.OllamaUrl ?? "").TrimEnd('/') + "/api/chat";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyOllamaHeaders(req,
            config.OllamaHeader1Key, config.OllamaHeader1Value,
            config.OllamaHeader2Key, config.OllamaHeader2Value,
            config.OllamaHeader3Key, config.OllamaHeader3Value);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var responseContent = doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return ParseAnalysisResponse(responseContent);
    }

    #endregion

    private static void ApplyOllamaHeaders(
        HttpRequestMessage req,
        string? h1k, string? h1v,
        string? h2k, string? h2v,
        string? h3k, string? h3v) {
        if (!string.IsNullOrWhiteSpace(h1k) && h1v is not null)
            req.Headers.TryAddWithoutValidation(h1k, h1v);
        if (!string.IsNullOrWhiteSpace(h2k) && h2v is not null)
            req.Headers.TryAddWithoutValidation(h2k, h2v);
        if (!string.IsNullOrWhiteSpace(h3k) && h3v is not null)
            req.Headers.TryAddWithoutValidation(h3k, h3v);
    }

    private static AiSentinelAnalysisResult? ParseAnalysisResponse(string? response) {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        try {
            var json = ExtractJson(response);
            using var doc = JsonDocument.Parse(json);

            var classificationStr = doc.RootElement.TryGetProperty("classification", out var cls)
                ? cls.GetString()
                : null;
            var reasoning = doc.RootElement.TryGetProperty("reasoning", out var rsn)
                ? rsn.GetString()
                : null;

            var classification = classificationStr?.ToLowerInvariant().Trim() switch {
                "spam" => AiSentinelClassification.Spam,
                "duplicate" => AiSentinelClassification.Duplicate,
                "virus" => AiSentinelClassification.Virus,
                "scam" => AiSentinelClassification.Scam,
                _ => AiSentinelClassification.Clean
            };

            return new AiSentinelAnalysisResult(classification, reasoning ?? "");
        } catch {
            return new AiSentinelAnalysisResult(AiSentinelClassification.Clean, "Failed to parse AI response.");
        }
    }

    private static string ExtractJson(string response) {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start >= 0 && end > start)
            return response[start..(end + 1)];
        return response;
    }

    private static List<EmbedDataDto> ExtractEmbeds(IMessage message) {
        var result = new List<EmbedDataDto>();
        foreach (var embed in message.Embeds) {
            result.Add(new EmbedDataDto(
                Title: embed.Title,
                Description: embed.Description,
                Url: embed.Url,
                Color: embed.Color is { } c ? (int)c.RawValue : null,
                AuthorName: embed.Author?.Name,
                AuthorIconUrl: embed.Author?.IconUrl,
                FooterText: embed.Footer?.Text,
                FooterIconUrl: embed.Footer?.IconUrl,
                ThumbnailUrl: embed.Thumbnail?.Url,
                ImageUrl: embed.Image?.Url,
                Timestamp: embed.Timestamp?.UtcDateTime,
                Fields: embed.Fields.Select(f => new EmbedFieldDto(f.Name, f.Value, f.Inline)).ToList()
            ));
        }
        return result;
    }
}
