namespace Playtrough.Api.Models;

public enum EmailSecurity { None, StartTls, SslOnConnect }

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public EmailSecurity SecureSocketMode { get; set; } = EmailSecurity.StartTls;
    public int TimeoutSeconds { get; set; } = 15;
}
