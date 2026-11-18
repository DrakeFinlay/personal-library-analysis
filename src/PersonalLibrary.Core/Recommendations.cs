namespace PersonalLibrary.Core;

public sealed record RecommendationReason(string Label, int MatchedBooks, double AverageRating, double Weight);
public sealed record Recommendation(Book Book, double Score, IReadOnlyList<RecommendationReason> Reasons);
public sealed record RecommendationList(int RatedBooks, int Candidates, IReadOnlyList<Recommendation> Matches);
public sealed record RecommendationOverview(bool HasRatedBooks, IReadOnlyList<Recommendation> ToRead, IReadOnlyList<Recommendation> OwnedUnread);

public static class Recommendations
{
    public const int PicksPerSection = 10;
    private const int NeutralRating = 3;
    private const int PriorObservations = 2;
    private const int MaxSignals = 3;
    private static readonly HashSet<string> StatusTopics = new(StringComparer.Ordinal)
    {
        "READ", "TO READ", "CURRENTLY READING", "TO READ IN JAPANESE", "READ B4 GOODREADS",
        "DID NOT FINISH", "DNF", "PARTIALLY READ", "REFERENCE ONLY", "UNCLASSIFIED", "MUSIC BOOK", "PHOTOGRAPHY ART BOOK"
    };

    private sealed record Feature(string Key, string Label, int Prior);

    public static RecommendationOverview Overview(IReadOnlyList<Book> library, DateTimeOffset? asOf = null)
    {
        var now = asOf ?? DateTimeOffset.Now;
        var toRead = Rank(library, asOf: now);
        var topToRead = SelectPicks(toRead.Matches);
        var displayed = topToRead.Select(match => match.Book.Id).ToHashSet();
        var topOwned = SelectPicks(Rank(library, ownedOnly: true, asOf: now).Matches
            .OrderBy(match => displayed.Contains(match.Book.Id)));
        return new(toRead.RatedBooks > 0, topToRead, topOwned);
    }

    private static Recommendation[] SelectPicks(IEnumerable<Recommendation> ranked)
    {
        var authors = new HashSet<string>(StringComparer.Ordinal);
        return ranked.Where(match => AuthorKey(match.Book) is not { } author || authors.Add(author))
            .Take(PicksPerSection).ToArray();
    }

    public static void Snooze(Book book, DateTimeOffset now) => book.RecommendationSnoozedUntil = now.AddYears(1);

    public static void Dismiss(Book book)
    {
        if (book.Status == ReadingStatus.ToRead) ReadingRules.Classify(book, ReadingStatus.Unclassified);
        book.RecommendationSnoozedUntil = null;
        book.RecommendationDismissed = true;
    }

    public static RecommendationList Rank(IReadOnlyList<Book> library, bool ownedOnly = false, DateTimeOffset? asOf = null)
    {
        var now = asOf ?? DateTimeOffset.Now;
        var rated = library.Where(book => book.Rating is >= 1 and <= 5 && HasCompleted(book)).ToArray();
        var evidence = rated.SelectMany(book => Features(book).Select(feature => (feature.Key, Stars: book.Rating!.Value)))
            .GroupBy(entry => entry.Key).ToDictionary(group => group.Key,
                group => (Count: group.Count(), Sum: group.Sum(entry => entry.Stars)), StringComparer.Ordinal);
        var candidates = library.Where(book => !book.Excluded && !HasCompleted(book) && !book.RecommendationDismissed &&
            !(book.RecommendationSnoozedUntil > now) && (ownedOnly
            ? book.Status == ReadingStatus.ToRead && HasPhysicalCopy(book)
            : book.Status == ReadingStatus.ToRead)).ToArray();
        var ranked = new List<Recommendation>();
        foreach (var book in candidates)
        {
            var reasons = Features(book).Where(feature => evidence.ContainsKey(feature.Key)).Select(feature =>
            {
                var (count, sum) = evidence[feature.Key];
                var weight = (double)(sum - NeutralRating * count) / (count + feature.Prior);
                return new RecommendationReason(feature.Label, count, (double)sum / count, weight);
            }).Where(reason => reason.Weight != 0)
                .OrderByDescending(reason => Math.Abs(reason.Weight)).ThenBy(reason => reason.Label, StringComparer.Ordinal)
                .Take(MaxSignals).ToArray();
            var score = reasons.Sum(reason => reason.Weight);
            if (score > 0) ranked.Add(new(book, score, reasons));
        }
        return new(rated.Length, candidates.Length, ranked.OrderByDescending(match => match.Score)
            .ThenBy(match => match.Book.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Book.Author, StringComparer.OrdinalIgnoreCase).ThenBy(match => match.Book.Id).ToArray());
    }

    public static bool IsOwned(Book book) => book.Editions.Any(edition =>
        edition.PhysicalCopies.Any(copy => !copy.Sold && !copy.IsLibraryLoan) ||
        (!edition.IsLibraryLoan && edition.Holdings.Any(holding => holding != "Physical" || edition.PhysicalCopies.Count == 0)));

    public static bool HasPhysicalCopy(Book book) => book.Editions.Any(edition =>
        edition.Holdings.Contains("Physical") && (edition.PhysicalCopies.Count > 0
            ? edition.PhysicalCopies.Any(copy => !copy.Sold && !copy.IsLibraryLoan)
            : !edition.IsLibraryLoan));

    internal static bool HasCompleted(Book book) =>
        ReadingRules.CountsAsReading(book) &&
        (book.Status == ReadingStatus.Read || book.Readings.Any(reading => reading.Outcome == ReadingOutcome.Completed));

    private static IEnumerable<Feature> Features(Book book)
    {
        if (AuthorKey(book) is { } author)
            yield return new("author:" + author, "Author: " + book.Author.Trim(), 2 * PriorObservations);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var topic in book.Shelves.Concat(book.Calibre?.Metadata.Tags ?? []))
        {
            var key = Key(topic.Replace('-', ' ').Replace('_', ' '));
            if (key.Length > 0 && !StatusTopics.Contains(key) && seen.Add(key))
                yield return new("topic:" + key, "Topic: " + topic.Trim(), PriorObservations);
        }
    }

    private static string? AuthorKey(Book book)
    {
        var key = Key(book.Author);
        return key is "" or "UNKNOWN" or "VARIOUS" or "VARIOUS AUTHORS" ? null : key;
    }

    private static string Key(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
