namespace Playtrough.Api.Catalog;

internal static class CatalogInput
{
    public static void ValidateSearch(string query, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Trim().Length is < 2 or > 100) throw new ArgumentException("Query must contain 2 to 100 characters.", nameof(query));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        if (pageSize is < 1 or > 40) throw new ArgumentOutOfRangeException(nameof(pageSize));
    }
}
