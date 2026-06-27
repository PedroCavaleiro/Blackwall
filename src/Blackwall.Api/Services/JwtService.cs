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

    /// <summary>
    /// Generates a signed JWT for the given user, valid for 7 days.
    /// Includes the user's internal ID, Discord user ID, and username as claims.
    /// The display name claim is included only when present.
    /// </summary>
    /// <param name="user">The user to generate a token for.</param>
    /// <returns>A signed JWT string.</returns>
    public string GenerateToken(AppUser user) {
        
        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("discord_user_id", user.DiscordUserId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username)
        };

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            claims.Add(new Claim("display_name", user.DisplayName));

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