using System.Text;
using System.Text.Json;
using Blackwall.Core.Configuration;
using Blackwall.Core.DTOs;
using Blackwall.Web.Components;
using Blackwall.Web.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

var webPort = builder.Configuration["WEB:PORT"];
if (!string.IsNullOrWhiteSpace(webPort))
    builder.WebHost.UseUrls($"http://*:{webPort}");

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");

var apiOptions = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()
    ?? throw new InvalidOperationException("API configuration is missing.");

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<SafeBrowsingOptions>(builder.Configuration.GetSection(SafeBrowsingOptions.SectionName));
builder.Services.Configure<AppConfiguration>(builder.Configuration.GetSection(AppConfiguration.SectionName));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters {
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

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient<BlackwallApiService>(client => {
    client.BaseAddress = new Uri(apiOptions.BaseUrl.TrimEnd('/') + '/');
    if (apiOptions.ProtectionEnabled && !string.IsNullOrEmpty(apiOptions.Key))
        client.DefaultRequestHeaders.Add("X-API-Key", apiOptions.Key);
});

builder.Services.AddSingleton<DiscordAppInfoService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await LoadDiscordAppInfoAsync(app.Services, builder.Configuration);

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.Use(async (context, next) => {
    var token = context.Request.Cookies["blackwall_jwt"];
    if (token is not null && !context.Request.Headers.ContainsKey("Authorization"))
        context.Request.Headers.Authorization = $"Bearer {token}";
    await next(context);
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/login", async (HttpContext ctx) => {
    using var client = new HttpClient();
    if (apiOptions.ProtectionEnabled && !string.IsNullOrEmpty(apiOptions.Key))
        client.DefaultRequestHeaders.Add("X-API-Key", apiOptions.Key);
    var response = await client.GetFromJsonAsync<LoginResponse>(
        $"{apiOptions.BaseUrl.TrimEnd('/')}/api/auth/discord");

    return response?.Url is not null
        ? Results.Redirect(response.Url)
        : Results.Redirect("/?error=auth_failed");
}).AllowAnonymous();

app.MapGet("/auth/callback", async (string? code, string? error, HttpContext ctx) => {
    if (!string.IsNullOrEmpty(error))
        return Results.Redirect($"/?error={Uri.EscapeDataString(error)}");

    if (string.IsNullOrEmpty(code))
        return Results.Redirect("/?error=auth_failed");

    using var client = new HttpClient();
    if (apiOptions.ProtectionEnabled && !string.IsNullOrEmpty(apiOptions.Key))
        client.DefaultRequestHeaders.Add("X-API-Key", apiOptions.Key);
    var response = await client.PostAsJsonAsync(
        $"{apiOptions.BaseUrl.TrimEnd('/')}/api/auth/exchange",
        new AuthExchangeRequest(code));

    if (!response.IsSuccessStatusCode)
        return Results.Redirect("/?error=auth_failed");

    var result = await response.Content.ReadFromJsonAsync<AuthExchangeResponse>();

    if (result?.Token is null)
        return Results.Redirect("/?error=auth_failed");

    ctx.Response.Cookies.Append("blackwall_jwt", result.Token, new CookieOptions {
        HttpOnly = true,
        Secure = !app.Environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });

    return Results.Redirect("/dashboard");
}).AllowAnonymous();

app.MapGet("/auth/logout", (HttpContext ctx) => {
    ctx.Response.Cookies.Delete("blackwall_jwt");
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapGet("/bot/invite", async (long guildId, BlackwallApiService api) => {
    var url = await api.GetBotInviteUrlAsync(guildId);
    return url is not null
        ? Results.Redirect(url)
        : Results.Redirect("/dashboard");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();

return;

async Task LoadDiscordAppInfoAsync(IServiceProvider services, IConfiguration config) {
    var clientId = config["DISCORD:CLIENT_ID"];
    var appInfo = services.GetRequiredService<DiscordAppInfoService>();
    appInfo.ClientId = clientId ?? "N/A";

    if (string.IsNullOrWhiteSpace(clientId))
        return;

    try {
        using var http = new HttpClient();
        var json = await http.GetFromJsonAsync<JsonElement>(
            $"https://discord.com/api/v10/applications/{clientId}/rpc");
        if (json.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { } name)
            appInfo.AppName = name;
    } catch {
        // Leave defaults if the API call fails
    }
}