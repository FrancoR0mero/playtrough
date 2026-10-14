using Playtrought.Api.Models;
using Playtrought.Api.Services.Contracts;

namespace Playtrought.Api.Services.Interfaces;

public interface ITokenService
{
    Task<TokenPair> CreateAsync(ApplicationUser user, IList<string> roles, CancellationToken ct = default);
}
