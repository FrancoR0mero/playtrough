using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;

namespace Playtrough.Api.Services.Implementations;

internal sealed record GroupClassificationSelection(string? GenreId, string[] SubgenreIds, string[] TagIds);

public sealed class GroupClassificationService(AppDbContext db)
{
    public async Task<GroupClassificationDto> GetAsync(CancellationToken ct)
    {
        var genres = await db.GroupGenres.OrderBy(item => item.Name)
            .Select(item => new GroupGenreDto(item.Id, item.Name)).ToArrayAsync(ct);
        var subgenres = await db.GroupSubgenres.OrderBy(item => item.Name)
            .Select(item => new GroupSubgenreDto(item.Id, item.Name, item.GenreId, item.Alias)).ToArrayAsync(ct);
        var tags = await db.GroupTags.OrderBy(item => item.Name)
            .Select(item => new GroupTagDto(item.Id, item.Name, item.Category)).ToArrayAsync(ct);
        return new(genres, subgenres, tags);
    }

    internal async Task<GroupClassificationSelection> ValidateAsync(string? genreId, string[]? subgenreIds,
        string[]? tagIds, ModelStateDictionary errors, bool forWrite, CancellationToken ct,
        bool allowExistingUnclassified = false)
    {
        genreId = genreId?.Trim().ToLowerInvariant();
        var validGenre = IsId(genreId) && await db.GroupGenres.AnyAsync(item => item.Id == genreId, ct);
        if ((forWrite || genreId is not null) && !validGenre)
            errors.AddModelError("genreId", "Elegí un género válido.");

        var subgenres = Normalize(subgenreIds);
        if (forWrite && subgenres.Length == 0 && !allowExistingUnclassified)
            errors.AddModelError("subgenreIds", "Elegí al menos un subgénero.");
        if (subgenres.Length > 0)
        {
            var validSubgenres = await MatchesAsync(subgenres, db.GroupSubgenres
                .Where(item => (genreId == null || item.GenreId == genreId) && subgenres.Contains(item.Id))
                .Select(item => item.Id), ct);
            if (!validSubgenres)
                errors.AddModelError("subgenreIds", "Elegí subgéneros válidos y distintos que correspondan al género seleccionado.");
        }

        var tags = Normalize(tagIds);
        if (tags.Length > 0 && !await MatchesAsync(tags,
                db.GroupTags.Where(item => tags.Contains(item.Id)).Select(item => item.Id), ct))
            errors.AddModelError("tagIds", "Elegí etiquetas válidas y distintas.");

        return new(genreId, subgenres, tags);
    }

    internal static GroupClassificationSelection FromLegacyTopic(string? topic)
    {
        var normalized = topic?.Trim().ToLowerInvariant();
        var tags = normalized is "cooperativo" or "competitivo" or "indie" or "retro" ? new[] { normalized } : [];
        return new(GroupClassificationCatalog.OtherGenre, [], tags);
    }

    private static string[] Normalize(string[]? ids)
        => ids?.Select(id => id?.Trim().ToLowerInvariant() ?? "").ToArray() ?? [];

    private static bool IsId(string? id) => id is { Length: > 0 and <= GroupClassificationConfiguration.IdLength }
        && id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    private static async Task<bool> MatchesAsync(string[] ids, IQueryable<string> matchingIds, CancellationToken ct)
        => ids.Length <= 100 && ids.All(IsId) && ids.Distinct().Count() == ids.Length
            && await matchingIds.CountAsync(ct) == ids.Length;
}
