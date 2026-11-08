namespace Playtrough.Api.Services;

internal static class LikePattern
{
    public static string Escape(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
