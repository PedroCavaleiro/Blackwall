using System.Reflection;
using System.Text;
using Blackwall.Api.Configuration;
using Blackwall.Api.Helpers;
using Blackwall.Api.Services;
using Blackwall.Infrastructure;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.AddControllers(options => {
    options.Conventions.Insert(0, new GlobalRoutePrefixConvention("api"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.AddHttpClient<DiscordOAuthService>();

builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.Configure<WebOptions>(
    builder.Configuration.GetSection(WebOptions.SectionName));

var jwtOptions = builder.Configuration
                        .GetSection(JwtOptions.SectionName)
                        .Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is missing.");

builder.Services
       .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

app.MapGet("/health", () => Results.Redirect("/api/system/health"))
   .WithTags("System")
   .WithSummary("Convenience alias for the API health check")
   .WithDescription("This endpoint acts as an alias and returns a 302 Redirect to the primary health endpoint at `/api/system/health`.")
   .Produces(StatusCodes.Status302Found);;

app.MapControllers();

if (builder.Configuration.GetValue<bool>("ENABLE_DOCS")) {
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.Run();