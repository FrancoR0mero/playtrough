using System.Threading.Channels;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Non-durable, in-process work only; pending requests are lost on application restart.</summary>
public sealed class PasswordResetQueue : IPasswordResetQueue
{
    private readonly Channel<string> _requests = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true, AllowSynchronousContinuations = false,
    });

    public void Enqueue(string email) => _requests.Writer.TryWrite(email);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _requests.Reader.ReadAllAsync(ct);
}
