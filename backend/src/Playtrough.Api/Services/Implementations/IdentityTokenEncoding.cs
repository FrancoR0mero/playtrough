using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Playtrough.Api.Services.Implementations;

internal static class IdentityTokenEncoding
{
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    public static string Decode(string token) => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
}
