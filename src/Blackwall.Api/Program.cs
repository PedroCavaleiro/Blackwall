using System.Reflection;
using System.Text;
using Blackwall.Api.Helpers;
using Blackwall.Api.Middleware;
using Blackwall.Api.Services;
using Blackwall.Api.Services.Discord;
using Blackwall.Bot.Discord;
using Blackwall.Bot.Discord.Background;
using Blackwall.Bot.Discord.Handlers;
using Blackwall.Bot.Discord.Services;
using Blackwall.Modules.DetectionMatrix;
using Blackwall.Modules.ContentGuard;
using Blackwall.Bot.Twitch;
using Blackwall.Modules.LinkProtection;
using Blackwall.Modules.Banlist;
using Blackwall.Modules.LinkProtection.Background;
using Blackwall.Modules.LinkProtection.Services;
using Blackwall.Api.Services.Twitch;
using Blackwall.Core.Configuration;
using Blackwall.Core.Services;
using Blackwall.Infrastructure;
using Discord;
using Discord.WebSocket;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using StackExchange.Redis;

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POSTGRES__CONNECTION_STRING")))
    try { Env.TraversePath().Load(); } catch { /* .env not readable — vars injected by systemd */ }

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var apiPort = builder.Configuration["API:PORT"];
if (!string.IsNullOrWhiteSpace(apiPort))
    builder.WebHost.UseUrls($"http://*:{apiPort}");

builder.Services.AddControllers(options => {
    options.Conventions.Insert(0, new GlobalRoutePrefixConvention("api"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.AddHttpClient<DiscordOAuthService>();

builder.Services.Configure<TwitchOptions>(builder.Configuration.GetSection(TwitchOptions.SectionName));
builder.Services.AddHttpClient<TwitchOAuthService>();
builder.Services.Configure<TwitchSyncOptions>(builder.Configuration.GetSection(TwitchSyncOptions.SectionName));

builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.Configure<WebOptions>(
    builder.Configuration.GetSection(WebOptions.SectionName));

builder.Services.Configure<AppConfiguration>(
    builder.Configuration.GetSection(AppConfiguration.SectionName));

builder.Services.Configure<GuildSyncOptions>(
    builder.Configuration.GetSection(GuildSyncOptions.SectionName));

builder.Services.Configure<BlacklistOptions>(
    builder.Configuration.GetSection(BlacklistOptions.SectionName));

builder.Services.Configure<SafeBrowsingOptions>(
    builder.Configuration.GetSection(SafeBrowsingOptions.SectionName));

var jwtOptions = builder.Configuration
                        .GetSection(JwtOptions.SectionName)
                        .Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is missing.");

_ = builder.Configuration.GetSection(AppConfiguration.SectionName)
    .Get<AppConfiguration>() ?? throw new InvalidOperationException("App configuration is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddSingleton<DiscordSocketClient>(_ => new DiscordSocketClient(new DiscordSocketConfig {
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers
}));
builder.Services.AddSingleton<GuildHandler>();
builder.Services.AddSingleton<MessageHandler>();
builder.Services.AddSingleton<GuildMemberHandler>();
builder.Services.AddSingleton<RaidDetectionService>();
builder.Services.AddSingleton<AccountScoringService>();
builder.Services.AddSingleton<LockdownService>();
builder.Services.AddSingleton<InteractionHandler>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthHandoffService>();
builder.Services.AddScoped<GuildClaimService>();
builder.Services.AddScoped<AccountLinkingService>();
builder.Services.AddScoped<GuildPermissionSyncService>();
builder.Services.AddSingleton<DiscordGuildCacheService>();
builder.Services.AddKeyedSingleton<IBanPlatformProvider, DiscordBanPlatformProvider>("discord");
builder.Services.AddKeyedSingleton<IBanSyncDataAccess, DiscordBanSyncDataAccess>("discord");
builder.Services.AddKeyedSingleton<BanSyncService>("discord", (sp, _) => {
    var provider = sp.GetRequiredKeyedService<IBanPlatformProvider>("discord");
    var dataAccess = sp.GetRequiredKeyedService<IBanSyncDataAccess>("discord");
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<BanSyncService>>();
    return new BanSyncService(provider, dataAccess, scopeFactory, logger);
});
builder.Services.AddSingleton<DiscordBanSyncService>(sp => {
    var inner = sp.GetRequiredKeyedService<BanSyncService>("discord");
    return new DiscordBanSyncService(inner);
});
builder.Services.AddSingleton<SafeBrowsingService>();
builder.Services.AddSingleton<ISafeBrowsingService>(sp => sp.GetRequiredService<SafeBrowsingService>());
builder.Services.AddSingleton<ContentGuardService>();
builder.Services.AddSingleton<AllowedBotService>();
builder.Services.AddSingleton<NetWatchSnareService>();
builder.Services.AddScoped<SafeBrowsingSyncService>();
builder.Services.AddScoped<MessageAuditService>();
builder.Services.AddSingleton<ModuleRunnerService>();
builder.Services.AddScoped<ModuleInstallationService>();
builder.Services.AddScoped<TwitchChannelService>();
builder.Services.AddSingleton<TwitchBotService>();
builder.Services.AddKeyedSingleton<IBanPlatformProvider, TwitchBanPlatformProvider>("twitch");
builder.Services.AddKeyedSingleton<IBanSyncDataAccess, TwitchBanSyncDataAccess>("twitch");
builder.Services.AddKeyedSingleton<BanSyncService>("twitch", (sp, _) => {
    var provider = sp.GetRequiredKeyedService<IBanPlatformProvider>("twitch");
    var dataAccess = sp.GetRequiredKeyedService<IBanSyncDataAccess>("twitch");
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<BanSyncService>>();
    return new BanSyncService(provider, dataAccess, scopeFactory, logger);
});
builder.Services.AddSingleton<TwitchBanSyncService>(sp => {
    var inner = sp.GetRequiredKeyedService<BanSyncService>("twitch");
    return new TwitchBanSyncService(inner);
});
builder.Services.AddKeyedSingleton<DetectionService>("discord", (sp, _) => {
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new DetectionService("spam:", redis);
});
builder.Services.AddKeyedSingleton<DetectionService>("twitch", (sp, _) => {
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new DetectionService("twitch:", redis);
});
builder.Services.AddKeyedSingleton<ILinkConfigProvider, DiscordLinkConfigProvider>("discord");
builder.Services.AddKeyedSingleton<ILinkConfigProvider, TwitchLinkConfigProvider>("twitch");
builder.Services.AddKeyedSingleton<LinkProtectionService>("discord", (sp, _) => {
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var configProvider = sp.GetRequiredKeyedService<ILinkConfigProvider>("discord");
    var blacklistOptions = sp.GetRequiredService<IOptions<BlacklistOptions>>();
    var options = new LinkProtectionOptions { RedisKeyPrefix = "blacklist" };
    var logger = sp.GetRequiredService<ILogger<LinkProtectionService>>();
    return new LinkProtectionService(redis, configProvider, blacklistOptions, options, logger);
});
builder.Services.AddKeyedSingleton<LinkProtectionService>("twitch", (sp, _) => {
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var configProvider = sp.GetRequiredKeyedService<ILinkConfigProvider>("twitch");
    var blacklistOptions = sp.GetRequiredService<IOptions<BlacklistOptions>>();
    var options = new LinkProtectionOptions { RedisKeyPrefix = "twitch" };
    var logger = sp.GetRequiredService<ILogger<LinkProtectionService>>();
    return new LinkProtectionService(redis, configProvider, blacklistOptions, options, logger);
});
builder.Services.AddHostedService<SafeBrowsingSyncBackgroundService>();
builder.Services.AddHostedService<MessageAuditPurgeBackgroundService>();
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddHostedService<GuildPermissionSyncBackgroundService>();
builder.Services.AddHostedService<BlacklistRefreshBackgroundService>();
builder.Services.AddHostedService<DiscordBanSyncBackgroundService>();
builder.Services.AddHostedService<TwitchBotWorker>();
builder.Services.AddHostedService<TwitchBanSyncBackgroundService>();

builder.Services.AddAuthorization();

builder.Services.AddOpenApi(options => {
    options.AddSchemaTransformer((schema, context, _) => {
        if (schema.Properties is null || schema.Required is null)
            return Task.CompletedTask;

        var properties = context.JsonTypeInfo.Type
                                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties) {
            var isNullable =
                Nullable.GetUnderlyingType(prop.PropertyType) is not null ||
                NullabilityHelper.IsNullableReferenceType(prop);

            if (!isNullable)
                continue;

            var schemaKey = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
            schema.Required.Remove(schemaKey);
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

ScrubSecretEnvironmentVariables();

void ScrubSecretEnvironmentVariables() {
    var secretKeys = new[] {
        "POSTGRES__CONNECTION_STRING",
        "REDIS__CONNECTION_STRING",
        "DISCORD__BOT_TOKEN",
        "DISCORD__CLIENT_SECRET",
        "TWITCH__CLIENT_SECRET",
        "JWT__SECRET",
        "APP__ENC_KEY",
        "APP__ENC_IV",
        "SAFE_BROWSING__API_KEY",
        "API__KEY"
    };

    foreach (var key in secretKeys)
        Environment.SetEnvironmentVariable(key, null);
}

using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<BlackwallDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Redirect("/api/system/health"))
   .WithTags("System")
   .WithSummary("Convenience alias for the API health check")
   .WithDescription("This endpoint acts as an alias and returns a 302 Redirect to the primary health endpoint at `/api/system/health`.")
   .Produces(StatusCodes.Status302Found);

app.MapControllers();

if (builder.Configuration.GetValue<bool>("ENABLE_DOCS")) {
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.Run();