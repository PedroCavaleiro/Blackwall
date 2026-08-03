using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed record AppConfiguration {
    public const string SectionName = "APP";
    
    [ConfigurationKeyName("ENC_KEY")]
    public string EncryptionKey { get; set; } = string.Empty;

    [ConfigurationKeyName("ENC_IV")]
    public string EncryptionIv { get; set; } = string.Empty;

    [ConfigurationKeyName("DISABLE_NEW_USERS")]
    public bool DisableNewUsers { get; set; } = false;

    [ConfigurationKeyName("PRIVATE_INSTANCE")]
    public bool PrivateInstance { get; set; } = false;

    [ConfigurationKeyName("INSTANCE_OWNER")]
    public string InstanceOwner { get; set; } = string.Empty;

    [ConfigurationKeyName("AI_SENTINEL_ENABLED")]
    public bool AiSentinelEnabled { get; set; } = true;
}