namespace Playtrough.Api.Services.Interfaces;

public interface IEmailConfirmationQueue
{
    void Enqueue(string email);
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct);
}
