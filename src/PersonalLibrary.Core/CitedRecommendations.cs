using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalLibrary.Core;

public sealed record CitationEvidence(Book Source, EpubCitation Citation);

public sealed record CitedRecommendation(string Key, string Title, string Author, Book? LocalBook,
    IReadOnlyList<CitationEvidence> Sources);

public static partial class CitedRecommendations
{
    public static bool IsSource(Book book) => !book.Excluded && book.Rating is 4 or 5 && Recommendations.HasCompleted(book);

    public static IEnumerable<CitedRecommendation> Rank(IReadOnlyList<Book> library,
        Func<Guid, IEnumerable<EpubCitation>> citations, DateTimeOffset? asOf = null)
    {
        var now = asOf ?? DateTimeOffset.Now;
        var localTitles = library.ToLookup(book => TextKey(book.Title));
        var references = library.Where(IsSource).SelectMany(book => citations(book.Id)
            .Where(IsBookCitation).Select(citation => new CitationEvidence(book, citation)));

        return references.GroupBy(evidence => TextKey(evidence.Citation.Title) + "|" + Surname(evidence.Citation.Author))
            .Select(group =>
            {
                var sources = group.GroupBy(evidence => evidence.Source.Id).Select(evidence => evidence.First())
                    .OrderByDescending(evidence => evidence.Source.Rating)
                    .ThenBy(evidence => evidence.Source.Title, StringComparer.OrdinalIgnoreCase).ToArray();
                var citation = sources[0].Citation;
                return (Key: group.Key, Citation: citation, Sources: sources);
            })
            .Select(work =>
            {
                var citation = work.Citation;
                var matches = localTitles[TextKey(citation.Title)].Where(book =>
                    TextKey(book.Author) == TextKey(citation.Author) || Surname(book.Author) == Surname(citation.Author)).ToArray();
                if (matches.Any(book => book.Excluded || Recommendations.HasCompleted(book) || book.RecommendationDismissed ||
                    book.RecommendationSnoozedUntil > now ||
                    book.Status is not (ReadingStatus.ToRead or ReadingStatus.Unclassified))) return null;
                var local = matches.FirstOrDefault(book => book.Status == ReadingStatus.ToRead) ?? matches.FirstOrDefault();
                return new CitedRecommendation(work.Key, local?.Title ?? citation.Title, local?.Author ?? citation.Author, local, work.Sources);
            })
            .OfType<CitedRecommendation>()
            .OrderByDescending(work => work.Sources.Sum(evidence => evidence.Source.Rating == 5 ? 2 : 1))
            .ThenByDescending(work => work.Sources.Count)
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(work => work.Author, StringComparer.OrdinalIgnoreCase)
            .ThenBy(work => work.Key, StringComparer.Ordinal);
    }

    public static IEnumerable<EpubCitation> ReadText(string markdown)
    {
        var lines = markdown.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = ListPrefix().Replace(lines[index].Trim(), "");
            var match = MarkedCitation().Match(line);
            if (!match.Success) match = PrintedCitation().Match(line);
            if (!match.Success) continue;
            var author = match.Groups["author"].Value.Trim(' ', ',', '.', ':');
            var title = match.Groups["title"].Value.Trim(' ', '.', '*', '_');
            yield return new(index + 1, "Pasted or scanned text", line, title, author, "bibliography.md", null);
        }
    }

    private static bool IsBookCitation(EpubCitation citation)
    {
        var title = citation.Title.Trim();
        var author = citation.Author.Trim();
        if (title.Length is < 2 or > 220 || author.Length is < 2 or > 90 ||
            title.Contains("://", StringComparison.Ordinal) || author.Contains("://", StringComparison.Ordinal) ||
            !author.Any(char.IsLetter) || author.Count(char.IsDigit) > 0 ||
            !title.Any(char.IsLetter) || NonBookCitation().IsMatch(citation.Text))
            return false;

        // A later italicized journal or collection title can follow the actual article title in quotes.
        var titleIndex = citation.Text.IndexOf(title, StringComparison.OrdinalIgnoreCase);
        if (titleIndex >= 0)
        {
            var beforeTitle = citation.Text[..titleIndex];
            if (beforeTitle.Contains('“') || beforeTitle.Contains('"')) return false;
        }
        return !title.StartsWith('“') && !title.StartsWith('"');
    }

    internal static string Surname(string author)
    {
        var name = author.Split([';', '&'], 2)[0].Trim();
        if (name.Contains(',')) return TextKey(name.Split(',', 2)[0]);
        return TextKey(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
    }

    internal static string TextKey(string value)
    {
        var normalized = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            normalized.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return string.Join(' ', normalized.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"^(?:[-+] |\d+\. )")]
    private static partial Regex ListPrefix();

    [GeneratedRegex(@"^(?<author>[^*_]{2,90}?)\s*[.:,]\s+(?<marker>\*\*?|_)\s*(?<title>[^*_]+?)\s*\k<marker>(?:[\s.,:(]|$)")]
    private static partial Regex MarkedCitation();

    [GeneratedRegex(@"^(?<author>[\p{L}’'\- ]+,\s+[\p{L}’'\- ]+)\.\s+(?<title>[^.?!“""\r\n]+)[.!?]\s+[^:.]{2,60}:\s+.+?\b(?:1[4-9]\d{2}|20\d{2})\b")]
    private static partial Regex PrintedCitation();

    [GeneratedRegex(@"\bdoi\s*:|doi\.org/|\b\d+\s*\(\d+\)\s*[:,]|\b(?:vol|no)\.\s*\d+\s*[:,]\s*\d+[-–]\d+", RegexOptions.IgnoreCase)]
    private static partial Regex NonBookCitation();
}
