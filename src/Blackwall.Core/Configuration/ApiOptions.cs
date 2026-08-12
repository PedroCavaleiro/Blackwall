using Microsoft.Extensions.Configuration;
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace Blackwall.Core.Configuration;

public sealed class ApiOptions {
    public const string SectionName = "API";

    [ConfigurationKeyName("BASE_URL")]
    public required string BaseUrl { get; set; }

    [ConfigurationKeyName("PROTECTION_ENABLED")]
    public bool ProtectionEnabled { get; set; }

    [ConfigurationKeyName("KEY")]
    public string? Key { get; set; }
}
