// ReSharper disable NullableWarningSuppressionIsUsed

using Microsoft.Extensions.Configuration;

namespace Blackwall.Core.Configuration;

public class JwtOptions {
    public const string SectionName = "JWT";

    [ConfigurationKeyName("ISSUER")]
    public string Issuer { get; set; } = null!;
    [ConfigurationKeyName("AUDIENCE")]
    public string Audience { get; set; } = null!;
    [ConfigurationKeyName("SECRET")]
    public string Secret { get; set; } = null!;
}