namespace Playtrough.Api.Models;

public sealed class RawgSettings
{
    public const string SectionName = "RawgSettings";
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.rawg.io/api";
    public int MaxRequestsPerSecond { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int SearchCacheMinutes { get; set; } = 10;
    public int DetailCacheMinutes { get; set; } = 24 * 60;
}
