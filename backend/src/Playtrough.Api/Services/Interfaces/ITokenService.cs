using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

public interface ITokenService
{
    Task<TokenPair> CreateAsync(ApplicationUser user, IList<string> roles, CancellationToken ct = default);
}
