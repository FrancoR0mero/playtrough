using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Non-durable, in-process work only; pending requests are lost on application restart.</summary>
public sealed class EmailConfirmationQueue : EmailRequestQueue, IEmailConfirmationQueue;
