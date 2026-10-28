using System.Data;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class LibraryPersistenceTests
{
    [Theory]
    [InlineData(LibraryStatus.Playing, "playing")]
    [InlineData(LibraryStatus.Backlog, "backlog")]
    [InlineData(LibraryStatus.Beaten, "beaten")]
    [InlineData(LibraryStatus.Completed, "completed")]
    public async Task StatusAndEventValues_ArePersistedAsExactContractText(LibraryStatus status, string text)
    {
        using var context = new LibraryTestContext();
        await context.AddAsync(status);
        Assert.Equal(status, LibraryValues.ParseStatus(text));
        await using var db = context.CreateDb();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Status FROM LibraryEntries";
        Assert.Equal(text, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT Type || ':' || ToStatus FROM LibraryEvents";
        Assert.Equal("added:" + text, await command.ExecuteScalarAsync());
        Assert.Equal(status, (await context.EntryAsync())!.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Playing")]
    [InlineData(" playing ")]
    [InlineData("finished")]
    [InlineData("0")]
    public void UnknownStatusText_IsInvalidStatus(string? text)
        => Assert.Equal("invalid_status", Assert.Throws<LibraryException>(() => LibraryValues.ParseStatus(text)).Code);

    [Theory]
    [InlineData(typeof(LibraryEntry), nameof(LibraryEntry.StatusChangedOn))]
    [InlineData(typeof(LibraryEvent), nameof(LibraryEvent.OccurredOn))]
    public void MySqlDateMapping_UsesConnectorDateTimeAndPreservesDate(Type entity, string propertyName)
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var property = db.Model.FindEntityType(entity)!.FindProperty(propertyName)!;
        var mapping = property.GetRelationalTypeMapping();
        using var table = new DataTable();
        table.Columns.Add(propertyName, typeof(DateTime));
        table.Rows.Add(new DateTime(2020, 2, 29));
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var providerValue = mapping.GetDataReaderMethod().Invoke(reader, [0]);
        Assert.Equal(new DateOnly(2020, 2, 29), mapping.Converter!.ConvertFromProvider(providerValue));
        Assert.Equal(new DateTime(2020, 2, 29), mapping.Converter.ConvertToProvider(new DateOnly(2020, 2, 29)));
        Assert.Equal("date", property.GetColumnType());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task GameCannotBeDeleted_WithLiveEntryOrWithOnlyHistoricalEvents()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        await using (var db = context.CreateDb())
        {
            db.Games.Remove((await db.Games.FindAsync(context.GameId))!);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await context.RemoveAsync();
        await using (var db = context.CreateDb())
        {
            db.Games.Remove((await db.Games.FindAsync(context.GameId))!);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        Assert.Equal(2, (await context.EventsAsync()).Count);
    }

    [Fact]
    public void MySqlModel_HasRequiredIndexesConcurrencyTokenAndNoEventToEntryForeignKey()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var entry = db.Model.FindEntityType(typeof(LibraryEntry))!;
        var change = db.Model.FindEntityType(typeof(LibraryEvent))!;
        Assert.True(entry.FindProperty(nameof(LibraryEntry.ConcurrencyToken))!.IsConcurrencyToken);
        Assert.Contains(entry.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "UserId", "GameId" }));
        Assert.Contains(entry.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "UserId", "Status" }));
        Assert.Contains(change.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "UserId", "OccurredOn", "Id" }));
        Assert.Contains(change.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "UserId", "GameId" }));
        Assert.DoesNotContain(change.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LibraryEntry));
    }
}
