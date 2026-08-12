using Microsoft.Extensions.Configuration;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Blackwall.Core.Configuration;

public sealed class SafeBrowsingOptions {
    public const string SectionName = "SAFE_BROWSING";

    [ConfigurationKeyName("ENABLED")]
    public bool Enabled { get; set; } = true;

    [ConfigurationKeyName("API_KEY")]
    public string? ApiKey { get; set; }

    [ConfigurationKeyName("BASE_URL")]
    public string BaseUrl { get; set; } = "https://safebrowsing.googleapis.com/v5";
}
