// ReSharper disable NullableWarningSuppressionIsUsed
namespace Blackwall.Api.Configuration;

public class JwtOptions {
    public const string SectionName = "JWT";

    [ConfigurationKeyName("ISSUER")]
    public string Issuer { get; set; } = null!;
    [ConfigurationKeyName("AUDIENCE")]
    public string Audience { get; set; } = null!;
    [ConfigurationKeyName("SECRET")]
    public string Secret { get; set; } = null!;
}