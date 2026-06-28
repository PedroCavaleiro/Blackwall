using Blackwall.Infrastructure.Cache;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Blackwall.Infrastructure;

public static class DependencyInjection {

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
    }

}