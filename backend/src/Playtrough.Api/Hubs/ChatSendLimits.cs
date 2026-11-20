using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;

namespace Playtrough.Api.Hubs;

public sealed class ChatSendLimits(IOptions<ChatSettings> settings) : IDisposable
{
    private readonly PartitionedRateLimiter<int> accounts = PartitionedRateLimiter.Create<int, int>(userId =>
        RateLimitPartition.GetSlidingWindowLimiter(userId, _ => Options(settings.Value, settings.Value.MessagesPerUser)));
    private readonly SlidingWindowRateLimiter global = new(Options(settings.Value, settings.Value.GlobalMessages));

    internal bool TryAcquire(int userId)
    {
        using var user = accounts.AttemptAcquire(userId);
        if (!user.IsAcquired) return false;
        using var total = global.AttemptAcquire();
        return total.IsAcquired;
    }

    private static SlidingWindowRateLimiterOptions Options(ChatSettings settings, int permits) => new()
    {
        PermitLimit = permits, QueueLimit = 0, Window = TimeSpan.FromSeconds(settings.WindowSeconds),
        SegmentsPerWindow = 10, AutoReplenishment = true,
    };

    public void Dispose()
    {
        accounts.Dispose();
        global.Dispose();
    }
}
