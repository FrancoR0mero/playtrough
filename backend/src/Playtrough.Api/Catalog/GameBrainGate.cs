namespace Playtrough.Api.Catalog;

// Shared across typed clients: hold the lease through response body consumption.
public sealed class GameBrainGate(TimeProvider time) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset next;
    public async Task EnterAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var delay = next - time.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, time, ct);
            next = time.GetUtcNow().AddSeconds(1);
        }
        catch { gate.Release(); throw; }
    }
    public void Exit() => gate.Release();
    public void Dispose() => gate.Dispose();
}
