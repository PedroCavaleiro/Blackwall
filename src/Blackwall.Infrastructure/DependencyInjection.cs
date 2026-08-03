using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Blackwall.Infrastructure;

public static class DependencyInjection {

    /// <summary>
    /// Registers infrastructure services into the dependency injection container,
    /// including the PostgreSQL <see cref="BlackwallDbContext"/>, a Redis
    /// <see cref="IConnectionMultiplexer"/>, and the <see cref="SpamConfigurationCache"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configuration">The application configuration containing connection strings.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the PostgreSQL or Redis connection string is missing from configuration.
    /// </exception>
    public static void AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        var dbConnection = configuration["POSTGRES:CONNECTION_STRING"]
                           ?? throw new InvalidOperationException("POSTGRES__CONNECTION_STRING is missing.");

        var redisConnection = configuration["REDIS:CONNECTION_STRING"]
                              ?? throw new InvalidOperationException("REDIS__CONNECTION_STRING is missing.");

        services.AddDbContext<BlackwallDbContext>(options => options.UseNpgsql(dbConnection));
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddScoped<SpamConfigurationCache>();
        services.AddScoped<NetWatchSnareChannelCache>();
        services.AddScoped<AiSentinelCache>();
    }

}