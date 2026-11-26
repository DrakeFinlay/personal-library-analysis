namespace PersonalLibrary.Core;

public sealed record CitationDiscoveryLead(string GraphKey, CitedRecommendation Pick, int EnjoyedSources, string Library)
{
    public string Title => Pick.Title;
    public string Author => Pick.Author;
    public int CitingBooks => Pick.Sources.Count;
}

public static class CitationDiscovery
{
    public static CitationDiscoveryLead[] Rank(IReadOnlyList<Book> library, BibliographyGraph graph,
        IReadOnlySet<Guid>? requiredSources = null, bool includeOtherStatuses = false, DateTimeOffset? asOf = null)
    {
        var now = asOf ?? DateTimeOffset.Now;
        var leads = new List<CitationDiscoveryLead>();
        foreach (var node in graph.Nodes.Values)
        {
            // One source contributes once regardless of how often it repeats a work.
            var evidence = graph.CitedBy(node.Key).Select(edge => edge.Evidence.FirstOrDefault(item => CitedRecommendations.IsBookCitation(item.Citation)))
                .OfType<CitationEvidence>().DistinctBy(item => item.Source.Id).ToArray();
            if (evidence.Length < 2 || requiredSources?.Any(requiredId => !evidence.Any(item => item.Source.Id == requiredId)) == true) continue;
            var citation = evidence[0].Citation;
            if (library.Any(book => book.Excluded && CitedRecommendations.TextKey(book.Title.Split(':', 2)[0]) == CitedRecommendations.TextKey(citation.Title.Split(':', 2)[0]) &&
                CitedRecommendations.Surname(book.Author) == CitedRecommendations.Surname(citation.Author))) continue;
            var matches = BibliographyMatches.Find(citation, library);
            if (matches.Any(book => book.RecommendationDismissed || book.RecommendationSnoozedUntil > now)) continue;
            if (!includeOtherStatuses && matches.Any(book => Recommendations.HasCompleted(book) || book.Status is not (ReadingStatus.ToRead or ReadingStatus.Unclassified))) continue;
            var local = node.BookId is { } id ? library.FirstOrDefault(book => book.Id == id) : null;
            var key = citation.Interpretation?.CanonicalWorkKey is { Length: > 0 } canonical ? canonical
                : citation.Interpretation?.WorkKey is { Length: > 0 } work ? work : CitedRecommendations.TextKey(citation.Title) + "|" + CitedRecommendations.Surname(citation.Author);
            var ordered = evidence.OrderByDescending(item => CitedRecommendations.IsSource(item.Source)).ThenByDescending(item => item.Source.Rating)
                .ThenBy(item => item.Source.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var pick = new CitedRecommendation(key, local?.Title ?? node.Title, local?.Author ?? node.Author, local, ordered);
            leads.Add(new(node.Key, pick, evidence.Count(item => CitedRecommendations.IsSource(item.Source)),
                local != null ? "In My Books" : matches.Length > 1 ? "Library match unresolved" : "Not in My Books"));
        }
        return leads.OrderByDescending(lead => lead.EnjoyedSources).ThenByDescending(lead => lead.CitingBooks)
            .ThenBy(lead => lead.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(lead => lead.GraphKey, StringComparer.Ordinal).ToArray();
    }
}
