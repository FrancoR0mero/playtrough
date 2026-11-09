using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class UserSearchService(AppDbContext db, ILookupNormalizer normalizer) : IUserSearchService
{
    public async Task<UserSearchResponse> SearchAsync(int viewerId, UserSearchRequest request, CancellationToken ct = default)
    {
        var query = request.Q?.Trim();
        if (query is null || query.EnumerateRunes().Count() is < 2 or > 50 || request.Page < 1 ||
            request.PageSize is < 1 or > 20 || ((long)request.Page - 1) * request.PageSize > int.MaxValue)
            throw new ProfileException("invalid_query");
        var rows = await Query(viewerId, normalizer.NormalizeName(query)).Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize + 1).ToListAsync(ct);
        var items = rows.Take(request.PageSize).Select(row => new UserSearchItemDto(row.Username, null,
            row.Friends ? new("friends") : row.SentId is { } sent ? new("request_sent", sent) :
            row.ReceivedId is { } received ? new("request_received", received) : new("none"))).ToArray();
        return new(items, request.Page, request.PageSize, rows.Count > request.PageSize);
    }

    // One SQL statement, including indexed relationship subqueries. No per-user service calls.
    internal IQueryable<UserSearchRow> Query(int viewerId, string normalized)
    {
        var literal = LikePattern.Escape(normalized);
        return db.Users.AsNoTracking().Where(u => u.Id != viewerId && u.EmailConfirmed &&
                EF.Functions.Like(u.NormalizedUserName!, "%" + literal + "%", LikePattern.EscapeCharacter))
            .OrderBy(u => u.NormalizedUserName == normalized ? 0 :
                EF.Functions.Like(u.NormalizedUserName!, literal + "%", LikePattern.EscapeCharacter) ? 1 : 2)
            .ThenBy(u => u.NormalizedUserName).ThenBy(u => u.Id)
            .Select(u => new UserSearchRow(u.UserName!,
                db.Friendships.Any(f => f.UserLowId == (viewerId < u.Id ? viewerId : u.Id) &&
                    f.UserHighId == (viewerId < u.Id ? u.Id : viewerId)),
                db.FriendRequests.Where(r => r.PendingPairKey ==
                    (long)(viewerId < u.Id ? viewerId : u.Id) * 4294967296L + (viewerId < u.Id ? u.Id : viewerId) && r.SenderId == viewerId)
                    .Select(r => (int?)r.Id).FirstOrDefault(),
                db.FriendRequests.Where(r => r.PendingPairKey ==
                    (long)(viewerId < u.Id ? viewerId : u.Id) * 4294967296L + (viewerId < u.Id ? u.Id : viewerId) && r.ReceiverId == viewerId)
                    .Select(r => (int?)r.Id).FirstOrDefault()));
    }

    internal sealed record UserSearchRow(string Username, bool Friends, int? SentId, int? ReceivedId);
}
