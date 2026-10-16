using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public enum EmailSecurity { None, StartTls, SslOnConnect }

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    [Required] public string Host { get; set; } = string.Empty;
    [Range(1, 65535)] public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    [Required, EmailAddress] public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = "Playtrough";
    public EmailSecurity Security { get; set; } = EmailSecurity.StartTls;
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 10;
}
