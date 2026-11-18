using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class TimelineCursorAndSqlTests
{
    [Theory]
    [InlineData(2, 1, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, int.MaxValue, 1, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, long.MaxValue, 1)]
    [InlineData(1, 1, 1, 0)]
    public void ProtectedButMalformedPayloads_AreRejectedStrictly(int version, int day, long ticks, long id)
    {
        var provider = new EphemeralDataProtectionProvider();
        var codec = new TimelineCursorCodec(provider);
        var bytes = provider.CreateProtector("Playtrough.Timeline.Cursor.v1").Protect(JsonSerializer.SerializeToUtf8Bytes(
            new { Version = version, Context = "test", Day = day, Ticks = ticks, Id = id }));
        var value = "v1." + WebEncoders.Base64UrlEncode(bytes);
        Assert.Equal("invalid_cursor", Assert.Throws<TimelineException>(() => codec.Decode(value, "test")).Code);
    }

    [Fact]
    public void OracleMySql_TranslatesKeysetDateAndMultipleTypesWithoutOpeningAConnection()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var clock = new CatalogPersistenceClock();
        var query = new TimelineQuery(db, Options.Create(new TimelineSettings()), clock);
        var sql = query.Entries(1, false, query.Filters(2026, 2, "added,status_changed"),
            new(new DateOnly(2026, 2, 10), clock.UtcNow.UtcDateTime, 42), 20, 2).ToQueryString();
        Assert.Contains("TimelineEntries", sql);
        Assert.Contains("ORDER BY", sql); Assert.Contains("LIMIT", sql);
        Assert.Contains("LEFT JOIN", sql); Assert.Contains("status_changed", sql);
        Assert.DoesNotContain("ProviderDetailsJson", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        var monthOnly = query.Entries(1, false, query.Filters(null, 2, null), null, 20, 2).ToQueryString();
        Assert.Matches(@"(?i)\bMONTH(?:\(|\s+FROM\s+)[^\r\n]*OccurredOn", monthOnly);
    }

    [Fact]
    public void LargeOrNonCanonicalCursor_IsRejected_AndValidCursorRetainsAllTicksAndLongId()
    {
        var codec = new TimelineCursorCodec(new EphemeralDataProtectionProvider());
        var position = new TimelinePosition(new DateOnly(2020, 2, 29), new DateTime(638900000000000001, DateTimeKind.Utc), long.MaxValue);
        var cursor = codec.Encode(position, "owner-filter");
        Assert.Equal(position, codec.Decode(cursor, "owner-filter"));
        Assert.Throws<TimelineException>(() => codec.Decode("v1." + new string('a', 4096), "owner-filter"));
        Assert.Throws<TimelineException>(() => codec.Decode(cursor + "=", "owner-filter"));
        Assert.Throws<TimelineException>(() => codec.Decode(cursor, "other-owner"));
    }
}
