using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Blackwall.Infrastructure.Persistence;

public sealed class BlackwallDbContextFactory : IDesignTimeDbContextFactory<BlackwallDbContext> {
    public BlackwallDbContext CreateDbContext(string[] args) {
        var optionsBuilder = new DbContextOptionsBuilder<BlackwallDbContext>();
        optionsBuilder.UseNpgsql(
            Environment.GetEnvironmentVariable("DB__CONNECTION_STRING")
            ?? "Host=localhost;Database=blackwall;Username=postgres;Password=postgres"
        );
        return new BlackwallDbContext(optionsBuilder.Options);
    }
}
