using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public sealed record AppConfiguration {
    public const string SectionName = "APP";
    
    [ConfigurationKeyName("ENC_KEY")]
    public string EncryptionKey { get; set; } = string.Empty;

    [ConfigurationKeyName("ENC_IV")]
    public string EncryptionIv { get; set; } = string.Empty;
}