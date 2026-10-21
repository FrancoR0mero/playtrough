namespace Playtrough.Api.Services.Contracts;

public record TokenPair(string AccessToken, string RefreshToken, DateTime ExpiresAt);
