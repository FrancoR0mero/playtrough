using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class EmailConfirmationSettings
{
    public const string SectionName = "EmailConfirmation";
    [Range(1, 10080)] public int TokenLifespanMinutes { get; set; } = 1440;
    [Range(1, 86400)] public int ResendCooldownSeconds { get; set; } = 60;
}
