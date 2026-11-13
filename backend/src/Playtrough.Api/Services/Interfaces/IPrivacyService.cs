using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Interfaces;

public interface IPrivacyService
{
    Task<bool> IsBlockedAsync(int firstId, int secondId, CancellationToken ct = default);
    Task<bool> CanViewAsync(int viewerId, int ownerId, PrivacyScope scope, CancellationToken ct = default);
    // Reuse data already read in this request. Never cache relationship or visibility across requests.
    bool CanView(int viewerId, int ownerId, Visibility visibility, bool areFriends, bool blocked = false);
    Task<PrivacyDto> GetAsync(int userId, CancellationToken ct = default);
    Task<PrivacyDto> UpdateAsync(int userId, UpdatePrivacyRequest request, CancellationToken ct = default);
}
