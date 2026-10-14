namespace Playtrought.Api.Dtos.Auth;

public record LoginResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    LoginUserDto User);

public record LoginUserDto(int Id, string Username, string Email, string Role);
