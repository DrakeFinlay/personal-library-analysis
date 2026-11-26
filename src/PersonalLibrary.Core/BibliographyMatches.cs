namespace PersonalLibrary.Core;

public sealed record LocalCitedBook(Book Book, IReadOnlyList<CitationEvidence> Evidence);

public static class BibliographyMatches
{
    public static Book[] Find(EpubCitation citation, IEnumerable<Book> library)
    {
        if (citation.Interpretation?.Excluded == true) return [];
        var title = CitedRecommendations.TextKey(citation.Title);
        var shortTitle = CitedRecommendations.TextKey(citation.Title.Split(':', 2)[0]);
        var surname = CitedRecommendations.Surname(citation.Author);
        if (shortTitle.Length < 2 || shortTitle.Length < 6 && surname.Length < 2) return [];
        return library.Where(book => !book.Excluded &&
            (CitedRecommendations.TextKey(book.Title) == title || CitedRecommendations.TextKey(book.Title.Split(':', 2)[0]) == shortTitle ||
                shortTitle.Replace(" ", "").Length >= 8 && surname.Length > 1 &&
                CitedRecommendations.TextKey(book.Title.Split(':', 2)[0]).Replace(" ", "") == shortTitle.Replace(" ", "")) &&
            (surname.Length == 0 || $" {CitedRecommendations.TextKey(book.Author)} ".Contains($" {surname} ", StringComparison.Ordinal))).ToArray();
    }

    public static EpubCitation[] ReadText(string text, IReadOnlyList<Book> library, IReadOnlyDictionary<string, CitationWorkIdentity>? identities = null, string evidenceSource = "")
    {
        var citations = CitationProcessing.ReadText(text, identities, evidenceSource).ToList();
        // OCR loses italics and may wrap a reference over several lines. Match known
        // titles only with author evidence inside the same bounded text block.
        var separated = System.Text.RegularExpressions.Regex.Replace(text.Replace("\r", ""),
            @"\n(?=[\p{L}’'\- ]{2,60},\s+[\p{L}])", "\n\n");
        var blocks = separated.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var key = $" {CitedRecommendations.TextKey(block)} ";
            foreach (var book in library.Where(book => !book.Excluded))
            {
                var title = CitedRecommendations.TextKey(book.Title.Split(':', 2)[0]);
                var surname = CitedRecommendations.Surname(book.Author);
                var position = key.IndexOf($" {title} ", StringComparison.Ordinal);
                if (position < 0 && title.Replace(" ", "").Length >= 8 && surname.Length >= 2)
                {
                    var compact = key.Replace(" ", ""); var compactTitle = title.Replace(" ", "");
                    var compactPosition = compact.IndexOf(compactTitle, StringComparison.Ordinal);
                    if (compactPosition >= 0 && compact.Substring(Math.Max(0, compactPosition - 120), Math.Min(compact.Length, compactPosition + compactTitle.Length + 60) - Math.Max(0, compactPosition - 120))
                        .Contains(surname.Replace(" ", ""), StringComparison.Ordinal) && !citations.Any(citation => citation.Text.Trim() == block.Trim() && Find(citation, [book]).Length > 0))
                        citations.Add(Suggestion(book, block));
                }
                if (title.Length < 4 || surname.Length < 2 || position < 0 ||
                    !key.Substring(Math.Max(0, position - 160), Math.Min(key.Length, position + title.Length + 80) - Math.Max(0, position - 160))
                        .Contains($" {surname} ", StringComparison.Ordinal) ||
                    citations.Any(citation => citation.Text.Trim() == block.Trim() && Find(citation, [book]).Length > 0)) continue;
                citations.Add(Suggestion(book, block));
            }
        }
        return citations.Select((citation, index) => CitationProcessing.ApplyIdentity(citation with { Number = index + 1, EvidenceSource = evidenceSource }, identities)).ToArray();

        EpubCitation Suggestion(Book book, string block) => new(citations.Count + 1, "Possible library / OCR match", block.Trim(), book.Title, book.Author, "bibliography.md", null,
            Interpretation: new(CitationProcessing.WorkKey(book.Title, book.Author), CitationResolution.LibrarySuggestion, block.Trim(),
                Reason: "Library title and author occur in this note. Interpretation needs review before creating a graph connection.", ExtractedTitle: book.Title, ExtractedAuthor: book.Author));
    }

    public static IReadOnlyList<LocalCitedBook> All(IReadOnlyList<Book> library, Func<Guid, IEnumerable<EpubCitation>> read)
    {
        var evidence = new Dictionary<Guid, List<CitationEvidence>>();
        foreach (var source in library.Where(book => !book.Excluded))
            foreach (var citation in read(source.Id).Where(citation => citation.Interpretation?.CanConnect != false))
                foreach (var match in Find(citation, library.Where(book => book.Id != source.Id)))
                {
                    if (!evidence.TryGetValue(match.Id, out var entries)) evidence[match.Id] = entries = [];
                    if (!entries.Any(entry => entry.Source.Id == source.Id && entry.Citation.Text == citation.Text))
                        entries.Add(new(source, citation));
                }
        return library.Where(book => evidence.ContainsKey(book.Id))
            .OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(book => new LocalCitedBook(book, evidence[book.Id])).ToArray();
    }
}
