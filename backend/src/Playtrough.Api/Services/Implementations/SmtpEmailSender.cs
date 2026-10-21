using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptions<EmailSettings> _options;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly Func<ISmtpClient> _createClient;

    public SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger)
        : this(options, logger, () => new SmtpClient()) { }

    // Tests substitute only the transport; no socket or SMTP server is required.
    internal SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger, Func<ISmtpClient> createClient)
        => (_options, _logger, _createClient) = (options, logger, createClient);

    internal static SecureSocketOptions MapSecurity(EmailSecurity mode) => mode switch
    {
        EmailSecurity.None => SecureSocketOptions.None,
        EmailSecurity.StartTls => SecureSocketOptions.StartTls,
        EmailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("Invalid SMTP security configuration."),
    };

    internal static MimeMessage CreateMessage(EmailSettings settings, string recipient, string subject, string htmlBody, string textBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();
        return message;
    }

    public async Task SendAsync(string recipient, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        var settings = _options.Value;
        if (!settings.Enabled)
        {
            _logger.LogInformation("Email delivery is disabled; message not sent.");
            return;
        }

        try
        {
            using var message = CreateMessage(settings, recipient, subject, htmlBody, textBody);
            using var client = _createClient();
            client.Timeout = settings.TimeoutSeconds * 1000;
            // Bound the complete async exchange as well as MailKit's socket timeout.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            await client.ConnectAsync(settings.Host, settings.Port, MapSecurity(settings.SecureSocketMode), timeout.Token);
            if (!string.IsNullOrWhiteSpace(settings.Username))
                await client.AuthenticateAsync(settings.Username, settings.Password, timeout.Token);
            await client.SendAsync(message, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
            // Dispose also closes the connection when connect/auth/send/disconnect throws.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Never log raw exception messages, protocol transcripts or the exception object:
            // servers may echo credentials, recipients or message contents in their replies.
            var safeMessage = ex switch
            {
                MailKit.Security.AuthenticationException => "SMTP authentication failed; verify the application password.",
                SmtpCommandException command => $"SMTP request rejected (status {(int)command.StatusCode}).",
                SmtpProtocolException => "SMTP protocol failure.",
                OperationCanceledException => "SMTP operation timed out.",
                IOException => "SMTP connection failed.",
                _ => "SMTP delivery failed.",
            };
            _logger.LogError("Email delivery failed ({ExceptionType}): {ErrorMessage}", ex.GetType().Name, safeMessage);
            // Keep the existing contract: recovery's background worker handles failures.
            throw;
        }
    }
}
