using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class PasswordResetSettings
{
    public const string SectionName = "PasswordReset";
    [Required] public string FrontendBaseUrl { get; set; } = string.Empty;
    [Range(1, 1440)] public int TokenLifespanMinutes { get; set; } = 60;
}
