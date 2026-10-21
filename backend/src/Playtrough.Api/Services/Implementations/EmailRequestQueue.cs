using System.Threading.Channels;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Non-durable, single-reader work queue. Pending requests are lost on restart.</summary>
public abstract class EmailRequestQueue
{
    private readonly Channel<string> _requests = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true, AllowSynchronousContinuations = false,
    });

    public void Enqueue(string email) => _requests.Writer.TryWrite(email);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _requests.Reader.ReadAllAsync(ct);
}
