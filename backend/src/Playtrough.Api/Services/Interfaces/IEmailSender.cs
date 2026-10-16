namespace Playtrough.Api.Services.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string htmlBody, string textBody, CancellationToken ct = default);
}
