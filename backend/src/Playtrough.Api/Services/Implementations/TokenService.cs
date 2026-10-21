using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Playtrough.Api.Authorization;

namespace Playtrough.Api.Services.Implementations;

public class TokenService(
    IOptions<JwtSettings> options,
    IRefreshTokenRepository refreshTokens) : ITokenService
{
    public string HashRefreshToken(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    public async Task<TokenPair> CreateAsync(ApplicationUser user, IList<string> roles, CancellationToken ct = default)
    {
        // Refuse inconsistent role data rather than silently selecting a more privileged role.
        if (roles.Count != 1 || !AppRoles.IsValid(roles[0]))
            throw new InvalidOperationException("Exactly one supported role is required to issue a session.");
        var settings = options.Value;
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(settings.AccessTokenExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.Add(new Claim("role", roles[0]));

        var jwt = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256));
        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        await refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(settings.RefreshTokenExpirationDays),
        }, ct);

        return new TokenPair(accessToken, refreshToken, expiresAt);
    }
}
