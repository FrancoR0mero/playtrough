using System.Data;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class GameMySqlDateMappingTests
{
    [Theory]
    [InlineData(1999, 6, 24)]
    [InlineData(2020, 2, 29)]
    public void MySqlMapping_ReadsProviderDateTimeAsDateOnly_AndPreservesCalendarDate(int year, int month, int day)
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var property = db.Model.FindEntityType(typeof(Game))!.FindProperty(nameof(Game.FirstReleaseDate))!;
        var mapping = property.GetRelationalTypeMapping();

        // Exercise the actual MySQL provider's reader selection with the value type
        // returned by Connector/NET. A direct GetFieldValue<DateOnly> fails here.
        using var table = new DataTable();
        table.Columns.Add("FirstReleaseDate", typeof(DateTime));
        table.Rows.Add(new DateTime(year, month, day));
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var providerValue = mapping.GetDataReaderMethod().Invoke(reader, [0]);
        var date = Assert.IsType<DateOnly>(mapping.Converter!.ConvertFromProvider(providerValue));
        Assert.Equal(new DateOnly(year, month, day), date);
        Assert.Equal(new DateTime(year, month, day), mapping.Converter.ConvertToProvider(date));
        Assert.Equal("date", property.GetColumnType());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void MySqlMapping_PreservesNullReleaseDate()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var property = db.Model.FindEntityType(typeof(Game))!.FindProperty(nameof(Game.FirstReleaseDate))!;
        var converter = property.GetRelationalTypeMapping().Converter!;
        Assert.True(property.IsNullable);
        Assert.Null(converter.ConvertFromProvider(null));
        Assert.Null(converter.ConvertToProvider(null));
    }
}
