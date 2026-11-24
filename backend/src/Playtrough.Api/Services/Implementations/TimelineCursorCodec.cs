using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

public sealed record TimelinePosition(DateOnly OccurredOn, DateTime RecordedAt, long Id);

public sealed class TimelineCursorCodec(IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("Playtrough.Timeline.Cursor.v1");
    private sealed record Payload(int Version, string Context, int Day, long Ticks, long Id);

    public string Encode(TimelinePosition position, string context)
        => "v1." + WebEncoders.Base64UrlEncode(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(
            new Payload(1, context, position.OccurredOn.DayNumber, position.RecordedAt.Ticks, position.Id))));

    public TimelinePosition Decode(string value, string context)
    {
        try
        {
            if (value.Length is < 4 or > 4096 || !value.StartsWith("v1.", StringComparison.Ordinal)
                || value.AsSpan(3).ContainsAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"))
                throw new TimelineException("invalid_cursor");
            var bytes = WebEncoders.Base64UrlDecode(value[3..]);
            if (WebEncoders.Base64UrlEncode(bytes) != value[3..]) throw new TimelineException("invalid_cursor");
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(bytes));
            if (payload is null || payload.Version != 1 || payload.Context != context || payload.Id <= 0)
                throw new TimelineException("invalid_cursor");
            return new(DateOnly.FromDayNumber(payload.Day), new DateTime(payload.Ticks, DateTimeKind.Utc), payload.Id);
        }
        catch (Exception error) when (error is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            throw new TimelineException("invalid_cursor");
        }
    }
}
