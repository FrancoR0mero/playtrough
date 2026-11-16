using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class JwtStartupRegressionTests
{
    [Theory]
    [InlineData("RePlAcE")]
    [InlineData("ExAmPlE")]
    [InlineData("EjEmPlO")]
    public async Task ArbitraryRandomBytes_CannotAccidentallyBecomeAPlaceholderInTheTestKey(string prefix)
    {
        // These are valid 64-byte values whose Base64 representation is rejected
        // by the real placeholder validator. Force them instead of relying on chance.
        var bytes = Convert.FromBase64String(prefix + new string('A', 79) + "==");
        Assert.Equal(64, bytes.Length);
        using var factory = new LoginApiFactory(signingKeyBytes: bytes);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(128, factory.SigningKey.Length);
        Assert.Equal(bytes, Convert.FromHexString(factory.SigningKey));
        Assert.True(new JwtSettingsValidator().Validate(null,
            factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value).Succeeded);
    }

    [Fact]
    public async Task IndependentHosts_StartConcurrentlyWithValidIsolatedKeys()
    {
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            using var factory = new LoginApiFactory();
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health");
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal(128, factory.SigningKey.Length);
        })));
    }
}
