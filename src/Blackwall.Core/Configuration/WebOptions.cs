using Microsoft.Extensions.Configuration;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.Core.Configuration;

public class WebOptions {
    public const string SectionName = "WEB";

    [ConfigurationKeyName("BASEURL")]
    public string BaseUrl { get; set; } = null!;
}