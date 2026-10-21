using Microsoft.AspNetCore.WebUtilities;
using Playtrough.Api.Dtos.Auth;

namespace Playtrough.Tests;

public record RecoveryEmail(string Recipient, string Subject, string HtmlBody, string TextBody)
{
    public const string NewPassword = "N3wPassword456!";
    public Uri ResetLink => new(TextBody.Split('\n').Select(line => line.Trim())
        .Single(line => line.StartsWith("http://", StringComparison.Ordinal) || line.StartsWith("https://", StringComparison.Ordinal)));

    public ResetPasswordRequestDto Request(string newPassword = NewPassword)
    {
        var query = QueryHelpers.ParseQuery(ResetLink.Query);
        return new ResetPasswordRequestDto
        {
            Email = query["email"].ToString(), Token = query["token"].ToString(),
            NewPassword = newPassword, ConfirmPassword = newPassword,
        };
    }
}
