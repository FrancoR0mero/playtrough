namespace Playtrough.Api.Dtos.Auth;

public record RefreshResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    LoginUserDto User);
