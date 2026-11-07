namespace Playtrough.Api.Models;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];

    internal static bool IsOrigin(string origin) =>
        !string.IsNullOrWhiteSpace(origin) && !origin.Contains('*') &&
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
