using PersonalLibrary.Core;
using System.Text.RegularExpressions;
using static PersonalLibrary.Infrastructure.GoodreadsImporter;

namespace PersonalLibrary.Infrastructure;

public sealed record DuplicateCandidate(Book First, Book Second, string Evidence)
{
    public string Label => $"{First.Title} / {Second.Title} · {Evidence}";
}

public static class BookIdentity
{
    public static bool MatchesSearch(Book book, string title, string author, string isbn)
    {
        if (isbn.Length > 0 && OpenLibraryLookup.IsbnKey(isbn) is { Length: > 0 } key && Isbns(book).Contains(key)) return true;
        return (title.Length > 0 || author.Length > 0)
            && book.Title.Contains(title, StringComparison.CurrentCultureIgnoreCase)
            && book.Author.Contains(author, StringComparison.CurrentCultureIgnoreCase);
    }

    public static bool MatchesMetadata(Book book, MetadataMatch match, EditionMetadata? edition) =>
        (match.WorkId != null && book.OpenLibraryWorkId == match.WorkId)
        || (match.EditionId != null && book.Editions.Any(e => e.OpenLibraryEditionId == match.EditionId))
        || (edition != null && Isbns(book).Contains(OpenLibraryLookup.IsbnKey(edition.Isbn)))
        || MatchesTitleAuthor(book, match);

    internal static bool MatchesTitleAuthor(Book book, MetadataMatch match) =>
        TitleAuthorKey(book.Title.Split(':', 2)[0], book.Author) is { } key
        && key == TitleAuthorKey(match.Title.Split(':', 2)[0], match.Author);

    public static IReadOnlyList<DuplicateCandidate> FindDuplicates(IEnumerable<Book> books)
    {
        var pairs = new Dictionary<(Guid, Guid), DuplicateCandidate>();
        var entries = books.Where(book => !book.Excluded).ToArray();
        AddGroups(book => Isbns(book), "Same ISBN");
        AddGroups(book => book.Editions.Select(edition => edition.OpenLibraryEditionId).OfType<string>(), "Same Open Library edition");
        AddGroups(book => book.OpenLibraryWorkId is { Length: > 0 } work ? [work] : [], "Same Open Library work");
        AddGroups(book => TitleAuthorKey(book.Title, book.Author) is { } key ? [key] : [], "Same title and author");
        AddGroups(book => TitleAuthorKey(MainTitle(book.Title), book.Author) is { } key ? [key] : [], "Same main title and author");
        return pairs.Values.OrderBy(pair => pair.First.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();

        void AddGroups(Func<Book, IEnumerable<string>> keys, string evidence)
        {
            foreach (var group in entries.SelectMany(book => keys(book).Distinct().Select(key => (key, book))).GroupBy(entry => entry.key))
            {
                var matching = group.Select(entry => entry.book).OrderBy(book => book.Id).ToArray();
                for (var i = 0; i < matching.Length; i++)
                for (var j = i + 1; j < matching.Length; j++)
                    if (!DifferentVolumes(matching[i].Title, matching[j].Title))
                        pairs.TryAdd((matching[i].Id, matching[j].Id), new(matching[i], matching[j], evidence));
            }
        }
    }

    internal static string MainTitle(string title) => SeriesSuffix.Replace(title, "").Split(':', 2)[0].Trim();

    internal static bool DifferentVolumes(string first, string second)
    {
        var left = Volume(first);
        var right = Volume(second);
        return left.Length > 0 && right.Length > 0 && left != right;
    }

    private static readonly Regex SeriesSuffix = new(@"\s*\([^()]*?(?:#|\bvol(?:ume)?\.?\s*)[0-9]+(?:\s*[-–]\s*[0-9]+)?\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VolumeNumber = new(
        @"(?:#|\b(?:vol(?:ume)?|book|part)\.?\s+|第)\s*(?<number>[0-9]+(?:\s*[-–]\s*[0-9]+)?|[IVXLCDM]+\b)(?=\W|$)|\b(?<number>II|III|IV|VI|VII|VIII|IX|I|V|X)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Volume(string title)
    {
        var value = VolumeNumber.Match(title).Groups["number"].Value;
        return value.ToUpperInvariant().Replace(" ", "").Replace('–', '-') switch
        {
            "I" => "1", "II" => "2", "III" => "3", "IV" => "4", "V" => "5",
            "VI" => "6", "VII" => "7", "VIII" => "8", "IX" => "9", "X" => "10",
            var number => number
        };
    }

    private static IEnumerable<string> Isbns(Book book) => book.Editions.Select(edition => OpenLibraryLookup.IsbnKey(edition.Isbn))
        .Where(key => key.Length > 0);

    private static string? TitleAuthorKey(string title, string author)
    {
        var titleKey = Normalize(title);
        var authorKey = Normalize(author);
        return titleKey.Length > 0 && authorKey.Length > 0 && authorKey != "unknown" ? titleKey + "|" + authorKey : null;
    }
}
