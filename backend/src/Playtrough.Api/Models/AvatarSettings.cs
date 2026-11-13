using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class AvatarSettings
{
    public const string SectionName = "Avatar";
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRequestBytes = MaxFileBytes + 64 * 1024;
    public const int Size = 256;
    [Required] public string StoragePath { get; set; } = "App_Data/avatars";
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AvatarUploadAttribute : Attribute { }

public static class AvatarUrls
{
    public static string? FromFile(string? fileName) => fileName is null ? null : $"/api/avatars/{fileName}";
    public static bool IsFileName(string fileName) => fileName.Length == 37 &&
        fileName.EndsWith(".webp", StringComparison.Ordinal) && Guid.TryParseExact(fileName[..32], "N", out _);
}
