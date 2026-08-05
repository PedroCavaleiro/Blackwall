using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Blackwall.Core.Configuration;
using Blackwall.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Blackwall.Api.Services;

public sealed class JwtService(IOptions<JwtOptions> options) {
    private readonly JwtOptions _options = options.Value;

    public string GenerateToken(AppUser user) {
        var displayName = AccountLinkingService.ResolveDisplayName(user);

        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username)
        };

        if (user.DiscordUserId != 0)
            claims.Add(new Claim("discord_user_id", user.DiscordUserId.ToString()));

        if (user.TwitchUserId.HasValue)
            claims.Add(new Claim("twitch_user_id", user.TwitchUserId.Value.ToString()));

        if (!string.IsNullOrWhiteSpace(displayName))
            claims.Add(new Claim("display_name", displayName));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}