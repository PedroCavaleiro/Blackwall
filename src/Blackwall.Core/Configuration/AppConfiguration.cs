using Microsoft.Extensions.Configuration;
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Blackwall.Core.Configuration;

public sealed record AppConfiguration {
    public const string SectionName = "APP";
    
    [ConfigurationKeyName("ENC_KEY")]
    public string EncryptionKey { get; set; } = string.Empty;

    [ConfigurationKeyName("ENC_IV")]
    public string EncryptionIv { get; set; } = string.Empty;

    [ConfigurationKeyName("DISABLE_NEW_USERS")]
    public bool DisableNewUsers { get; set; }

    [ConfigurationKeyName("PRIVATE_INSTANCE")]
    public bool PrivateInstance { get; set; }

    [ConfigurationKeyName("INSTANCE_OWNER")]
    public string InstanceOwner { get; set; } = string.Empty;
}