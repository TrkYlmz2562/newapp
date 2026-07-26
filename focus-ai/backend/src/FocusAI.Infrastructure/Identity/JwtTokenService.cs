using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FocusAI.Infrastructure.Identity;

public sealed class JwtTokenService(
    IOptions<JwtOptions> options,
    IDateTimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AuthTokens Issue(Guid userId, string email, IEnumerable<string> roles)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthTokens(
            new JwtSecurityTokenHandler().WriteToken(token),
            GenerateRefreshToken(),
            accessExpires,
            refreshExpires);
    }

    /// <summary>
    /// Refresh tokens are opaque random strings, stored only as a SHA-256 digest.
    /// A database leak therefore does not yield usable sessions.
    /// </summary>
    public string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
