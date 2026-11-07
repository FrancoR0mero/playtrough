namespace Playtrough.Api.Models;

public sealed class GameBrainOptions
{
    public const string SectionName = "GameBrain";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.gamebrain.co/v1";
    public string ApiKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int SearchCacheMinutes { get; set; } = 10;
    public int DetailCacheMinutes { get; set; } = 1440;
    public int OffersCacheMinutes { get; set; } = 15;
}
