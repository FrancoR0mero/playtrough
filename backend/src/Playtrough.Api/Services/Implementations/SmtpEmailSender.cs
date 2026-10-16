using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class SmtpEmailSender(IOptions<EmailSettings> options) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        var settings = options.Value;
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();
        var security = settings.Security switch
        {
            EmailSecurity.None => SecureSocketOptions.None,
            EmailSecurity.StartTls => SecureSocketOptions.StartTls,
            EmailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => throw new InvalidOperationException("Invalid SMTP security configuration."),
        };
        using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        await client.ConnectAsync(settings.Host, settings.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password, ct);
        // Exceptions are handled and sanitized by the background recovery flow, never returned to the client.
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
