using System.Text;

namespace Playtrough.Api.Services;

public static class PlainText
{
    public static string? Sanitize(string? text)
    {
        if (text is null) return null;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
            if (!char.IsControl(character) || character is '\n' or '\t') result.Append(character);
        var clean = result.ToString().Trim();
        return clean.Length == 0 ? null : clean;
    }
}
