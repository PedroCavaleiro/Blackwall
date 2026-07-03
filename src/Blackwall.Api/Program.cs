using System.Reflection;
using System.Text;
using Blackwall.Api.Helpers;
using Blackwall.Api.Middleware;
using Blackwall.Api.Services;
using Blackwall.Bot;
using Blackwall.Bot.Background;
using Blackwall.Bot.Handlers;
using Blackwall.Bot.Services;
using Blackwall.Core.Configuration;
using Blackwall.Infrastructure;
using Discord;
using Discord.WebSocket;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Blackwall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

Env.TraversePath().Load();

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
builder.Services.AddSingleton<SpamDetectionService>();
builder.Services.AddSingleton<RaidDetectionService>();
builder.Services.AddSingleton<AccountScoringService>();
builder.Services.AddSingleton<LockdownService>();
builder.Services.AddSingleton<InteractionHandler>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthHandoffService>();
builder.Services.AddScoped<GuildClaimService>();
builder.Services.AddScoped<GuildPermissionSyncService>();
builder.Services.AddSingleton<DiscordGuildCacheService>();
builder.Services.AddScoped<BlacklistService>();
builder.Services.AddSingleton<BanSyncService>();
builder.Services.AddSingleton<SafeBrowsingService>();
builder.Services.AddSingleton<ContentGuardService>();
builder.Services.AddSingleton<AllowedBotService>();
builder.Services.AddSingleton<SentinelService>();
builder.Services.AddScoped<SafeBrowsingSyncService>();
builder.Services.AddScoped<MessageAuditService>();
builder.Services.AddHostedService<SafeBrowsingSyncBackgroundService>();
builder.Services.AddHostedService<MessageAuditPurgeBackgroundService>();
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddHostedService<GuildPermissionSyncBackgroundService>();
builder.Services.AddHostedService<BlacklistRefreshBackgroundService>();
builder.Services.AddHostedService<BanSyncBackgroundService>();

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