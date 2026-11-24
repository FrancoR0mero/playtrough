using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

internal enum NotificationCategory { Friends, Interactions, Groups, Account, Moderation }

public sealed class NotificationPreferences(AppDbContext db)
{
    private sealed record Category(NotificationCategory Kind, string Name, bool Editable);

    private static readonly Category[] Categories =
    [
        new(NotificationCategory.Friends, "friends", true),
        new(NotificationCategory.Interactions, "interactions", true),
        new(NotificationCategory.Groups, "groups", true),
        new(NotificationCategory.Account, "account", false),
        new(NotificationCategory.Moderation, "moderation", false),
    ];

    internal async Task<IReadOnlyList<NotificationPreferenceDto>> ListAsync(int user, CancellationToken ct)
    {
        var stored = await db.NotificationPreferences.AsNoTracking().Where(row => row.UserId == user)
            .ToDictionaryAsync(row => row.Category, row => row.Enabled, ct);
        return Categories.Select(category => new NotificationPreferenceDto(category.Name,
            !category.Editable || stored.GetValueOrDefault(category.Name, true), category.Editable)).ToArray();
    }

    internal async Task<bool> IsEnabledAsync(int user, NotificationCategory kind, CancellationToken ct)
    {
        var category = Categories.Single(category => category.Kind == kind);
        return !category.Editable || await Preference(user, category.Name)
            .Select(row => (bool?)row.Enabled).SingleOrDefaultAsync(ct) != false;
    }

    internal async Task<NotificationPreferenceDto?> SaveAsync(int user, string name, bool enabled, CancellationToken ct)
    {
        var category = Categories.SingleOrDefault(category => category.Name == name);
        if (category is null || !category.Editable) return null;

        var query = Preference(user, category.Name);
        var result = new NotificationPreferenceDto(category.Name, enabled, category.Editable);
        if (await UpdateExistingAsync()) return result;

        var row = new NotificationPreference { UserId = user, Category = category.Name, Enabled = enabled };
        db.NotificationPreferences.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.Entries.Count == 1 && error.Entries[0].Entity == row)
        {
            // The composite primary key arbitrates concurrent first writes from different sessions.
            if (!await UpdateExistingAsync()) throw;
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
        return result;

        async Task<bool> UpdateExistingAsync()
            => await query.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Enabled, enabled), ct) > 0
                || await query.AnyAsync(item => item.Enabled == enabled, ct);
    }

    private IQueryable<NotificationPreference> Preference(int user, string category)
        => db.NotificationPreferences.AsNoTracking().Where(row => row.UserId == user && row.Category == category);
}
