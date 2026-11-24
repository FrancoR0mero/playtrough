using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class TimelineConfigurationTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("maximum")]
    [InlineData("inverted")]
    [InlineData("excerpt")]
    [InlineData("backdate")]
    public void Startup_RejectsInvalidTimelineAndFeedSettings(string setting)
    {
        using var c = new GamesApiTestContext();
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<TimelineSettings>(s =>
            {
                if (setting == "default") s.DefaultLimit = 0;
                if (setting == "maximum") s.MaxLimit = 51;
                if (setting == "inverted") s.MaxLimit = 10;
                if (setting == "excerpt") s.ExcerptLength = 0;
            });
            services.PostConfigure<FeedSettings>(s => { if (setting == "backdate") s.MaxBackdateDays = -1; });
        }));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
