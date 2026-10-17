using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class EmailConfirmationWorker(
    IEmailConfirmationQueue queue, IServiceScopeFactory scopes, ILogger<EmailConfirmationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var email in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IAuthService>()
                        .ProcessResendConfirmationAsync(email, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError("Email confirmation background work failed ({ExceptionType}).", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
