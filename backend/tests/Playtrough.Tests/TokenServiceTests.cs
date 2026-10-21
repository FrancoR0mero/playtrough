using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class TokenServiceTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("multiple")]
    [InlineData("unknown")]
    public async Task Create_WithInconsistentRoles_RejectsSessionWithoutPersistingTokens(string scenario)
    {
        // Arrange
        var repo = new Mock<IRefreshTokenRepository>();
        var service = new TokenService(Options.Create(_settings), repo.Object);
        IList<string> roles = scenario switch
        {
            "none" => [], "multiple" => ["User", "Admin"], _ => ["Unknown"],
        };

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new ApplicationUser { Id = 42 }, roles));
        repo.Verify(repository => repository.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private readonly JwtSettings _settings = new()
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7,
    };

    [Fact]
    public async Task Create_ValidSignedJwtAndOnlyHashedRefreshTokenAreProduced()
    {
        var repo = new Mock<IRefreshTokenRepository>();
        RefreshToken? saved = null;
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((token, _) => saved = token)
            .Returns(Task.CompletedTask);
        var before = DateTime.UtcNow;
        var tokens = await new TokenService(Options.Create(_settings), repo.Object).CreateAsync(
            new ApplicationUser { Id = 42, UserName = "ana", Email = "ana@example.com" }, ["Admin"]);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(tokens.AccessToken, Validation(), out var validated);
        Assert.Equal("42", principal.FindFirst("sub")?.Value);
        Assert.Equal("ana", principal.FindFirst("unique_name")?.Value);
        Assert.Equal("ana@example.com", principal.FindFirst("email")?.Value);
        Assert.False(principal.IsInRole("User"));
        Assert.True(principal.IsInRole("Admin"));
        Assert.Single(principal.FindAll("role"));
        Assert.Equal(SecurityAlgorithms.HmacSha256, ((JwtSecurityToken)validated).Header.Alg);
        Assert.InRange(tokens.ExpiresAt, before.AddMinutes(15), DateTime.UtcNow.AddMinutes(15));
        Assert.InRange((tokens.ExpiresAt - validated.ValidTo).TotalSeconds, 0, 1);
        Assert.Equal(64, Convert.FromBase64String(tokens.RefreshToken).Length);
        Assert.NotNull(saved);
        Assert.Equal(42, saved.UserId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokens.RefreshToken))), saved.TokenHash);
        Assert.NotEqual(tokens.RefreshToken, saved.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), saved.ExpiresAt - saved.CreatedAt);
        Assert.Null(saved.RevokedAt);
        Assert.Null(saved.ReplacedByToken);
    }

    [Fact]
    public async Task Create_EachLoginGetsDifferentAccessAndRefreshTokens()
    {
        var repo = new Mock<IRefreshTokenRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new TokenService(Options.Create(_settings), repo.Object);
        var user = new ApplicationUser { Id = 1, UserName = "ana", Email = "ana@example.com" };
        var first = await service.CreateAsync(user, ["User"]);
        var second = await service.CreateAsync(user, ["User"]);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expiry")]
    public async Task Validate_RejectsWrongIssuerAudienceSignatureAndExpiredToken(string invalid)
    {
        var repo = new Mock<IRefreshTokenRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tokens = await new TokenService(Options.Create(_settings), repo.Object).CreateAsync(
            new ApplicationUser { Id = 1 }, ["User"]);
        var validation = Validation();
        var accessToken = tokens.AccessToken;
        switch (invalid)
        {
            case "issuer": validation.ValidIssuer = "OtherIssuer"; break;
            case "audience": validation.ValidAudience = "OtherAudience"; break;
            case "signature": validation.IssuerSigningKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)); break;
            case "expiry":
                accessToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                    issuer: _settings.Issuer, audience: _settings.Audience,
                    notBefore: DateTime.UtcNow.AddMinutes(-16), expires: DateTime.UtcNow.AddMinutes(-1),
                    signingCredentials: new SigningCredentials(
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)), SecurityAlgorithms.HmacSha256)));
                break;
        }
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(accessToken, validation, out _));
    }

    private TokenValidationParameters Validation() => new()
    {
        ValidateIssuer = true, ValidIssuer = _settings.Issuer,
        ValidateAudience = true, ValidAudience = _settings.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        RoleClaimType = "role",
    };
}
