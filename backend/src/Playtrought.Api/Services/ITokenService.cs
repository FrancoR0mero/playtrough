using Playtrought.Api.Models;

namespace Playtrought.Api.Services;

public interface ITokenService
{
    Task<TokenPair> CreateAsync(ApplicationUser user, IList<string> roles, CancellationToken ct = default);
}

public record TokenPair(string AccessToken, string RefreshToken, DateTime ExpiresAt);
