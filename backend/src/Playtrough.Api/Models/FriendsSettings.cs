using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class FriendsSettings
{
    public const string SectionName = "Friends";
    [Range(1, 100000)]
    public int MaxPendingSent { get; set; } = 50;
    [Range(0, 36500)]
    public int RejectCooldownDays { get; set; } = 7;
}
