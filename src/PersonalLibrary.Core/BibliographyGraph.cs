namespace PersonalLibrary.Core;

public sealed record BibliographyNode(string Key, string Title, string Author, Guid? BookId, bool HasBibliography, string Identity)
{
    public string Label => Title + (Author.Length == 0 ? "" : " · " + Author);
}
public sealed record BibliographyConnection(string Source, string Target, CitationEvidence[] Evidence);
public sealed record CitedWorkStatistics(BibliographyNode Work, int CitingBooks, int EvidencePassages)
{
    public string Title => Work.Title;
    public string Author => Work.Author;
    public string Identity => Work.Identity;
}
public sealed record BibliographyNeighborhood(string[] Keys, int Omitted);

public sealed class BibliographyGraph
{
    public IReadOnlyDictionary<string, BibliographyNode> Nodes { get; }
    public BibliographyConnection[] Connections { get; }
    public int SourceBooks { get; }
    public int TextOnlyEntries { get; }
    private readonly Dictionary<string, BibliographyConnection[]> incoming;
    private readonly Dictionary<string, BibliographyConnection[]> outgoing;

    private BibliographyGraph(Dictionary<string, BibliographyNode> nodes, BibliographyConnection[] connections, int sources, int textOnly)
    {
        Nodes = nodes; Connections = connections; SourceBooks = sources; TextOnlyEntries = textOnly;
        incoming = connections.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.ToArray());
        outgoing = connections.GroupBy(e => e.Source).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public BibliographyConnection[] CitedBy(string key) => incoming.GetValueOrDefault(key) ?? [];
    public BibliographyConnection[] References(string key) => outgoing.GetValueOrDefault(key) ?? [];
    public IEnumerable<string> Neighbors(string key) => CitedBy(key).Select(e => e.Source).Concat(References(key).Select(e => e.Target)).Distinct();
    public CitedWorkStatistics[] Statistics() => Nodes.Values.Select(node => new CitedWorkStatistics(node,
        CitedBy(node.Key).Length, CitedBy(node.Key).Sum(e => e.Evidence.Length)))
        .Where(row => row.CitingBooks > 0).OrderByDescending(row => row.CitingBooks).ThenByDescending(row => row.EvidencePassages)
        .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public BibliographyNeighborhood Neighborhood(string root, int depth, int limit = 120)
    {
        if (!Nodes.ContainsKey(root) || depth < 0 || limit < 1) return new([], 0);
        var seen = new HashSet<string> { root }; var ordered = new List<string> { root };
        var queue = new Queue<(string Key, int Depth)>(); queue.Enqueue((root, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Depth >= depth) continue;
            foreach (var neighbor in Neighbors(current.Key).OrderByDescending(key => CitedBy(key).Length).ThenBy(key => Nodes[key].Label, StringComparer.CurrentCultureIgnoreCase))
                if (seen.Add(neighbor)) { ordered.Add(neighbor); queue.Enqueue((neighbor, current.Depth + 1)); }
        }
        return new(ordered.Take(limit).ToArray(), Math.Max(0, ordered.Count - limit));
    }

    // Traverse both directions to reveal influence chains and cross connections.
    // The returned graph edges retain their actual source -> target direction.
    public string[] Path(string from, string to)
    {
        if (!Nodes.ContainsKey(from) || !Nodes.ContainsKey(to)) return [];
        var parents = new Dictionary<string, string?> { [from] = null }; var queue = new Queue<string>(); queue.Enqueue(from);
        while (queue.TryDequeue(out var key))
        {
            if (key == to)
            {
                var path = new List<string>(); string? step = to;
                while (step != null) { path.Add(step); step = parents[step]; }
                path.Reverse(); return path.ToArray();
            }
            foreach (var neighbor in Neighbors(key)) if (parents.TryAdd(neighbor, key)) queue.Enqueue(neighbor);
        }
        return [];
    }

    public static BibliographyGraph Build(IReadOnlyList<Book> library, Func<Guid, IEnumerable<EpubCitation>> read)
    {
        var books = library.Where(book => !book.Excluded).ToArray();
        var citations = books.ToDictionary(book => book.Id, book => read(book.Id).ToArray());
        var nodes = new Dictionary<string, BibliographyNode>();
        var edges = new Dictionary<(string Source, string Target), Dictionary<string, CitationEvidence>>();
        var index = books.SelectMany(book => new[] { Key(book.Title), Key(book.Title.Split(':', 2)[0]) }.Distinct().Select(key => (key, book)))
            .GroupBy(pair => pair.key).ToDictionary(group => group.Key, group => group.Select(pair => pair.book).DistinctBy(book => book.Id).ToArray());
        string Local(Book book)
        {
            var key = "book:" + book.Id;
            nodes.TryAdd(key, new(key, book.Title, book.Author, book.Id, citations[book.Id].Length > 0, "My Books · inferred match")); return key;
        }
        var textOnly = 0;
        foreach (var source in books.Where(book => citations[book.Id].Length > 0))
        {
            var from = Local(source);
            foreach (var citation in citations[source.Id])
            {
                if (string.IsNullOrWhiteSpace(citation.Title)) { textOnly++; continue; }
                var title = Key(citation.Title); var shortTitle = Key(citation.Title.Split(':', 2)[0]);
                if (title.Length == 0) { textOnly++; continue; }
                var candidates = (index.GetValueOrDefault(title) ?? []).Concat(index.GetValueOrDefault(shortTitle) ?? []).DistinctBy(book => book.Id).ToArray();
                var matches = BibliographyMatches.Find(citation, candidates);
                string target;
                if (matches.Length == 1) target = Local(matches[0]);
                else
                {
                    var author = string.Join(' ', CitedRecommendations.TextKey(citation.Author).Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
                    target = "cited:" + title + "|" + author + (author.Length == 0 ? "|source:" + source.Id : "");
                    var identity = matches.Length > 1 ? "Ambiguous My Books match" : author.Length == 0 ? "Author missing · grouped within this source only" : "Outside My Books · inferred grouping";
                    nodes.TryAdd(target, new(target, citation.Title, citation.Author, null, false, identity));
                }
                if (from == target) continue;
                var pair = (from, target);
                if (!edges.TryGetValue(pair, out var evidence)) edges[pair] = evidence = new();
                evidence.TryAdd(CitedRecommendations.TextKey(citation.Text), new(source, citation));
            }
        }
        return new(nodes, edges.Select(pair => new BibliographyConnection(pair.Key.Source, pair.Key.Target, pair.Value.Values.ToArray())).ToArray(),
            citations.Count(pair => pair.Value.Length > 0), textOnly);
    }

    private static string Key(string title) => CitedRecommendations.TextKey(title).Replace(" ", "");
}
