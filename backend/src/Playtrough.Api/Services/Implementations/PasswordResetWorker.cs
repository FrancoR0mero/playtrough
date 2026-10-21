using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class PasswordResetWorker(
    IPasswordResetQueue queue, IServiceScopeFactory scopes, ILogger<PasswordResetWorker> logger) : BackgroundService
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
                        .ProcessForgotPasswordAsync(email, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError("Password recovery background work failed ({ExceptionType}).", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
