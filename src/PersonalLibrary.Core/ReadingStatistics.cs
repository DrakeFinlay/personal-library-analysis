namespace PersonalLibrary.Core;

public sealed record Completion(Book Book, Reading Reading, Edition Edition)
{
    public DateOnly? Finished => Reading.Finished;
    public int? Year => Reading.CompletionYear;
    public int? Pages => Edition.PageCount;
}

public sealed record PageCoverage(long KnownPages, int WithPageCount, int WithoutPageCount)
{
    public static PageCoverage Calculate(IReadOnlyList<Completion> completions)
    {
        var known = completions.Where(completion => completion.Pages.HasValue).ToArray();
        return new(known.Sum(completion => (long)completion.Pages!.Value), known.Length, completions.Count - known.Length);
    }
}

public sealed record AnnualCompletions(int Year, IReadOnlyList<Completion> Readings, PageCoverage Pages);
public sealed record PublicationPoint(DateOnly Finished, int OriginalYear, IReadOnlyList<Completion> Readings);
public sealed record AuthorReadingStatistics(string Author, IReadOnlyList<Completion> Readings, int RatedBooks, decimal? AverageRating);

public sealed class ReadingStatistics
{
    public IReadOnlyList<AnnualCompletions> Years { get; }
    public PageCoverage LifetimePages { get; }
    public int UndatedCompletions { get; }
    public int YearOnlyCompletions { get; }
    public int DatedWithoutPublicationYear { get; }
    public IReadOnlyList<PublicationPoint> PublicationPoints { get; }
    public IReadOnlyList<AuthorReadingStatistics> MostReadAuthors { get; }
    public IReadOnlyList<AuthorReadingStatistics> FavoriteAuthors { get; }
    public Completion? OldestBook { get; }
    public Completion? LongestBook { get; }
    public Completion? ShortestBook { get; }

    public ReadingStatistics(IEnumerable<Book> books)
    {
        var completed = books.SelectMany(book => ReadingRules.CompletedReadings(book)
            .Select(reading => new Completion(book, reading, book.Editions.Single(edition => edition.Id == reading.EditionId))))
            .OrderByDescending(completion => completion.Year)
            .ThenByDescending(completion => completion.Finished)
            .ThenBy(completion => completion.Book.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        LifetimePages = PageCoverage.Calculate(completed);
        var dated = completed.Where(completion => completion.Finished.HasValue).ToArray();
        UndatedCompletions = completed.Count(completion => !completion.Year.HasValue);
        YearOnlyCompletions = completed.Length - dated.Length - UndatedCompletions;
        Years = completed.Where(completion => completion.Year.HasValue).GroupBy(completion => completion.Year!.Value).Select(group =>
        {
            var readings = group.ToArray();
            return new AnnualCompletions(group.Key, readings, PageCoverage.Calculate(readings));
        }).ToArray();
        DatedWithoutPublicationYear = dated.Count(completion => !completion.Book.OriginalYear.HasValue);
        PublicationPoints = dated.Where(completion => completion.Book.OriginalYear.HasValue)
            .GroupBy(completion => (Finished: completion.Finished!.Value, Year: completion.Book.OriginalYear!.Value))
            .Select(group => new PublicationPoint(group.Key.Finished, group.Key.Year, group.ToArray())).ToArray();

        var authors = completed.GroupBy(completion => AuthorName(completion.Book.Author), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.ToUpperInvariant() is not ("" or "UNKNOWN" or "VARIOUS" or "VARIOUS AUTHORS"))
            .Select(group =>
            {
                var ratings = group.DistinctBy(completion => completion.Book.Id)
                    .Select(completion => completion.Book.Rating)
                    .Where(rating => rating is >= 1 and <= 5).ToArray();
                return new AuthorReadingStatistics(group.Key, group.ToArray(), ratings.Length,
                    ratings.Length == 0 ? null : ratings.Average(rating => (decimal)rating!.Value));
            })
            .OrderByDescending(author => author.Readings.Count)
            .ThenBy(author => author.Author, StringComparer.CurrentCultureIgnoreCase).ToArray();
        MostReadAuthors = authors.Take(5).ToArray();
        FavoriteAuthors = authors.Where(author => author.RatedBooks >= 2 && author.AverageRating >= 4.5m)
            .OrderByDescending(author => author.AverageRating)
            .ThenByDescending(author => author.RatedBooks)
            .ThenBy(author => author.Author, StringComparer.CurrentCultureIgnoreCase).ToArray();

        OldestBook = completed.Where(completion => completion.Book.OriginalYear is not (null or 0))
            .MinBy(completion => completion.Book.OriginalYear);
        var knownLength = completed.Where(completion => completion.Pages > 0).ToArray();
        LongestBook = knownLength.MaxBy(completion => completion.Pages);
        ShortestBook = knownLength.MinBy(completion => completion.Pages);
    }

    private static string AuthorName(string author) =>
        string.Join(' ', author.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
