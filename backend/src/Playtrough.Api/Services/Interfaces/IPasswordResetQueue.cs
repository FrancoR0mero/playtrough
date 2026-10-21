namespace Playtrough.Api.Services.Interfaces;

public interface IPasswordResetQueue
{
    void Enqueue(string email);
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct);
}
