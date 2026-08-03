using Blackwall.Core.Entities;
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Blackwall.Core.DTOs;

public sealed record AiSentinelConfigurationDto(
    bool IsEnabled,
    bool IsDryRun,
    bool IsTrainingMode,
    AiSentinelProvider Provider,
    string? ApiKey,
    string? OllamaUrl,
    string? OllamaHeader1Key,
    string? OllamaHeader1Value,
    string? OllamaHeader2Key,
    string? OllamaHeader2Value,
    string? OllamaHeader3Key,
    string? OllamaHeader3Value,
    string? Model,
    InfractionAction Action,
    bool AutoLockdown,
    int TimeoutMinutes,
    int MessageDeleteDays
);

public sealed record UpdateAiSentinelConfigurationRequest(
    bool IsEnabled,
    bool IsDryRun,
    bool IsTrainingMode,
    AiSentinelProvider Provider,
    string? ApiKey,
    string? OllamaUrl,
    string? OllamaHeader1Key,
    string? OllamaHeader1Value,
    string? OllamaHeader2Key,
    string? OllamaHeader2Value,
    string? OllamaHeader3Key,
    string? OllamaHeader3Value,
    string? Model,
    InfractionAction Action,
    bool AutoLockdown,
    int TimeoutMinutes,
    int MessageDeleteDays
);

public sealed record AiSentinelModelDto(
    string Id,
    string Name
);

public sealed record AiSentinelLogSummaryDto(
    long Id,
    long DiscordMessageId,
    long DiscordUserId,
    string Username,
    long DiscordChannelId,
    string ChannelName,
    AiSentinelClassification Classification,
    string Reasoning,
    AiSentinelProvider Provider,
    string Model,
    bool IsDryRun,
    bool WouldAction,
    AiSentinelTrainingFeedback TrainingFeedback,
    DateTime CreatedAtUtc
);

public sealed record AiSentinelLogDetailDto(
    long Id,
    long DiscordMessageId,
    long DiscordUserId,
    string Username,
    string? AvatarHash,
    long DiscordChannelId,
    string ChannelName,
    string Content,
    string EmbedsJson,
    AiSentinelClassification Classification,
    string Reasoning,
    AiSentinelProvider Provider,
    string Model,
    bool IsDryRun,
    bool WouldAction,
    AiSentinelTrainingFeedback TrainingFeedback,
    DateTime MessageTimestampUtc,
    DateTime CreatedAtUtc
);

public sealed record UpdateAiSentinelTrainingFeedbackRequest(
    AiSentinelTrainingFeedback Feedback
);

public sealed record ListAiSentinelModelsRequest(
    AiSentinelProvider Provider,
    string? ApiKey,
    string? OllamaUrl,
    string? OllamaHeader1Key,
    string? OllamaHeader1Value,
    string? OllamaHeader2Key,
    string? OllamaHeader2Value,
    string? OllamaHeader3Key,
    string? OllamaHeader3Value
);
