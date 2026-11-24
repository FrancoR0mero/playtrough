using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services;

/// <summary>Shared database locks serialize friendship and block transitions, across app instances.</summary>
internal static class UserPairTransaction
{
    public static Task<T> RunAsync<T>(AppDbContext db, int first, int second, Func<Task<T>> action, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var low = Math.Min(first, second);
                var high = Math.Max(first, second);
                await db.Users.Where(u => u.Id == low).ExecuteUpdateAsync(update =>
                    update.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount), ct);
                if (high != low)
                    await db.Users.Where(u => u.Id == high).ExecuteUpdateAsync(update =>
                        update.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount), ct);
                var result = await action();
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is FriendRequest or Friendship or UserBlock).ToArray())
                    entry.State = EntityState.Detached;
                throw;
            }
        });
}
