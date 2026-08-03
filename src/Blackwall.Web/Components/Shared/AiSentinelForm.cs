using Blackwall.Core.Entities;

namespace Blackwall.Web.Components.Shared;

public sealed class AiSentinelForm {
    public bool IsEnabled { get; set; }
    public bool IsDryRun { get; set; } = true;
    public bool IsTrainingMode { get; set; } = true;
    public AiSentinelProvider Provider { get; set; } = AiSentinelProvider.OpenAI;
    public string? ApiKey { get; set; }
    public string? OllamaUrl { get; set; }
    public string? OllamaHeader1Key { get; set; }
    public string? OllamaHeader1Value { get; set; }
    public string? OllamaHeader2Key { get; set; }
    public string? OllamaHeader2Value { get; set; }
    public string? OllamaHeader3Key { get; set; }
    public string? OllamaHeader3Value { get; set; }
    public string? Model { get; set; }
    public InfractionAction Action { get; set; } = InfractionAction.DeleteOnly;
    public bool AutoLockdown { get; set; }
    public int TimeoutMinutes { get; set; } = 10;
    public int MessageDeleteDays { get; set; }
}
