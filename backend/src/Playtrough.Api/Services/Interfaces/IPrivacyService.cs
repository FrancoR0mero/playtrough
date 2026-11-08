using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Interfaces;

public interface IPrivacyService
{
    Task<bool> CanViewAsync(int viewerId, int ownerId, PrivacyScope scope, CancellationToken ct = default);
    // Reuse data already read in this request. Never cache relationship or visibility across requests.
    bool CanView(int viewerId, int ownerId, Visibility visibility, bool areFriends);
    Task<PrivacyDto> GetAsync(int userId, CancellationToken ct = default);
    Task<PrivacyDto> UpdateAsync(int userId, UpdatePrivacyRequest request, CancellationToken ct = default);
}
