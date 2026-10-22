using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class TransportSettings
{
    public const string SectionName = "Transport";
    [Range(typeof(long), "1", "9223372036854775807")]
    public long MaxRequestBodySizeBytes { get; set; } = 1024 * 1024;
    [Range(1, 65535)] public int HttpsPort { get; set; } = 443;
    [Range(1, int.MaxValue)] public int ForwardLimit { get; set; } = 1;
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
}
