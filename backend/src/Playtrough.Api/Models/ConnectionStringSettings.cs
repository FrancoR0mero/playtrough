namespace Playtrough.Api.Models;

public sealed class ConnectionStringSettings
{
    public const string SectionName = "ConnectionStrings";
    public string DefaultConnection { get; set; } = string.Empty;
}
