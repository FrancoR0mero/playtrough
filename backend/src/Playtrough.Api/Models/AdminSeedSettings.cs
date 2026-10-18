namespace Playtrough.Api.Models;

public sealed class AdminSeedSettings
{
    public const string SectionName = "AdminSeed";
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
