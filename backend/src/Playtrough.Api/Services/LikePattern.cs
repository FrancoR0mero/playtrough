namespace Playtrough.Api.Services;

internal static class LikePattern
{
    // A backslash escape is emitted as ESCAPE '\' by the MySQL provider, which
    // is invalid under MySQL's default SQL mode. Use a portable single character.
    public const string EscapeCharacter = "!";

    public static string Escape(string text) => text.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
}
