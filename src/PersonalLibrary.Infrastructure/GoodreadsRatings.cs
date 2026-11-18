using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record GoodreadsRatingChange(Guid BookId, string Title, int Stars)
{
    public string Display => $"{Title}  ·  {Stars} of 5 stars";
}

public sealed class GoodreadsRatingsPreview
{
    public IReadOnlyList<GoodreadsRatingChange> Changes { get; }
    public IReadOnlyList<string> UnmatchedArchiveTitles { get; }
    public int UnmatchedArchiveRatings => UnmatchedArchiveTitles.Count;
    public int AlreadyRated { get; }
    internal IReadOnlyDictionary<Guid, DateTimeOffset> Baseline { get; }

    internal GoodreadsRatingsPreview(Book[] books, GoodreadsRatingChange[] changes, string[] unmatchedArchiveTitles, int alreadyRated)
    {
        Baseline = books.ToDictionary(book => book.Id, book => book.UpdatedAt);
        Changes = Array.AsReadOnly(changes);
        UnmatchedArchiveTitles = Array.AsReadOnly(unmatchedArchiveTitles);
        AlreadyRated = alreadyRated;
    }
}

public static class GoodreadsRatings
{
    public static GoodreadsRatingsPreview Preview(LibraryStore store, string csvPath, string archivePath)
    {
        var books = store.Books().ToArray();
        var sources = GoodreadsImporter.ReadSources(csvPath);
        var archive = GoodreadsArchive.ReadRatings(archivePath);
        var uniqueArchive = archive.GroupBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1 && group.First().Stars != null)
            .ToDictionary(group => group.Key, group => group.First().Stars!.Value, StringComparer.OrdinalIgnoreCase);
        var uniqueTitles = sources.GroupBy(source => source.Title, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linked = books.Where(book => book.Goodreads != null).GroupBy(book => book.Goodreads!.BookId)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First());

        var changes = new List<GoodreadsRatingChange>();
        var archiveMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alreadyRated = 0;
        foreach (var source in sources)
        {
            if (source.Error != null || !linked.TryGetValue(source.Id, out var book)) continue;
            var stars = GoodreadsImporter.CsvRating(source.Fields);
            if (uniqueTitles.Contains(source.Title) && uniqueArchive.TryGetValue(source.Title, out var archiveStars))
            {
                archiveMatches.Add(source.Title);
                stars ??= archiveStars;
            }
            if (stars == null) continue;
            if (book.Rating != null) alreadyRated++;
            else changes.Add(new(book.Id, book.Title, stars.Value));
        }
        var unmatched = archive.Where(row => row.Stars != null && !archiveMatches.Contains(row.Title))
            .Select(row => row.Title).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new(books, changes.OrderBy(change => change.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(), unmatched, alreadyRated);
    }
}
