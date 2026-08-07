using Blackwall.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blackwall.Modules.LinkProtection;

public static class DependencyInjection {
    public static IServiceCollection AddLinkProtection(
        this IServiceCollection services,
        string redisKeyPrefix,
        string serviceKey
    ) {
        services.Configure<BlacklistOptions>(options => { });
        services.AddKeyedSingleton<LinkProtectionOptions>(serviceKey, new LinkProtectionOptions {
            RedisKeyPrefix = redisKeyPrefix
        });
        services.AddKeyedSingleton<LinkProtectionService>(serviceKey);
        return services;
    }
}
