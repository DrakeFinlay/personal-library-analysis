using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class BibliographyGraphChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var epic = new Book { Title = "The Imaginary Comedy", Author = "Dante Fictional" };
        var philosophers = new Book { Title = "Fictional Philosophers", Author = "A. Historian" };
        var caseBook = new Book { Title = "A Fictional Case", Author = "B. Scholar" };
        var other = new Book { Title = "Another Fictional Source", Author = "C. Writer" };
        var excluded = new Book { Title = "Excluded source", Excluded = true };
        EpubCitation Cite(Book target, string text) => new(1, "References", text, target.Title, target.Author, "synthetic.xhtml", null);
        var epicCitation = Cite(epic, "Fictional, Dante. The Imaginary Comedy. Synthetic Press, 2020.");
        var fromPhilosophers = new[] { epicCitation, epicCitation with { ArchiveEntry = "online.txt" },
            epicCitation with { Text = "See Fictional, The Imaginary Comedy, page 42." } };
        var fromCase = new[] { Cite(philosophers, "Historian. Fictional Philosophers. Synthetic Press, 2021.") };
        var remote = new EpubCitation(2, "References", "An external reference.", "An External Work", "Theo Invented", "source.md", null);
        var data = new Dictionary<Guid, EpubCitation[]> {
            [philosophers.Id] = fromPhilosophers.Concat([remote, remote with { Title = "A Single-Source Leaf", Text = "A one-off reference." }]).ToArray(), [caseBook.Id] = fromCase,
            [other.Id] = [epicCitation, remote with { Author = "Invented, Theo" }], [excluded.Id] = [epicCitation]
        };
        Book[] library = [epic, philosophers, caseBook, other, excluded];
        var graph = BibliographyGraph.Build(library, id => data.GetValueOrDefault(id) ?? []);
        string Key(Book book) => "book:" + book.Id;
        var edge = graph.Connections.Single(e => e.Source == Key(philosophers) && e.Target == Key(epic));
        check(edge.Evidence.Length == 2 && graph.CitedBy(Key(epic)).Length == 2,
            "Citation graph deduplicates duplicate provider text and counts distinct citing books separately from evidence passages");
        check(graph.Path(Key(epic), Key(caseBook)).SequenceEqual([Key(epic), Key(philosophers), Key(caseBook)]) &&
            !graph.Connections.Any(e => e.Source == Key(caseBook) && e.Target == Key(epic)),
            "Graph paths reveal deep chains in either direction without inventing transitive citation edges");
        var neighborhood = graph.Neighborhood(Key(epic), 2);
        check(neighborhood.Keys.Contains(Key(other)) && neighborhood.Keys.Contains(Key(caseBook)) &&
            graph.Connections.Any(e => e.Source == Key(caseBook) && e.Target == Key(philosophers)),
            "A neighborhood retains parallel incoming branches and the original directed cross connections");
        var stats = graph.Statistics(); var row = stats.Single(row => row.Work.BookId == epic.Id);
        check(row.CitingBooks == 2 && row.EvidencePassages == 3 && stats.Any(row => row.Work.Title == "An External Work" && row.CitingBooks == 2 && row.Work.BookId == null),
            "Repeated-work statistics include works outside My Books and normalize author-name order conservatively");
        check(graph.Neighborhood(Key(epic), 2, 2).Keys.Length == 2 && graph.Neighborhood(Key(epic), 2, 2).Omitted > 0 && graph.Statistics().Single(r => r.Work.BookId == epic.Id).CitingBooks == 2,
            "Graph display limits report omissions without limiting corpus-wide citation statistics");
        check(graph.SourceBooks == 3 && !graph.Nodes.ContainsKey(Key(excluded)), "Excluded sources do not contribute graph nodes or citation counts");
        var shared = graph.SharedNetwork();
        check(!shared.Nodes.Values.Any(node => node.Title == "A Single-Source Leaf") && shared.Path(Key(epic), Key(caseBook)).Length == 3 &&
            shared.Nodes.ContainsKey(Key(other)) && shared.CitedBy(Key(epic)).Length == 2,
            "Shared visualization excludes one-source leaves while preserving parallel sources and singly cited bridges in deep chains");
        var single = BibliographyGraph.Build(library, id => id == philosophers.Id ? fromPhilosophers : []);
        check(single.SharedNetwork().Nodes.Count == 0 && single.SharedNetwork().Connections.Length == 0 && single.Statistics().Single().EvidencePassages == 2,
            "Repeated mentions in one bibliography do not create a visualization and remain available in full statistics");
        data[epic.Id] = [Cite(caseBook, "A cyclic citation."), Cite(epic, "Self-reference.")];
        var cyclic = BibliographyGraph.Build(library, id => data.GetValueOrDefault(id) ?? []);
        check(cyclic.Neighborhood(Key(epic), 8).Keys.Distinct().Count() == cyclic.Nodes.Count && !cyclic.Connections.Any(e => e.Source == e.Target),
            "Cycles terminate and self references do not create artificial graph loops");
        var sharedCycle = cyclic.SharedNetwork();
        check(sharedCycle.Connections.Length == sharedCycle.Connections.Select(edge => (edge.Source, edge.Target)).Distinct().Count() && sharedCycle.Path(Key(epic), Key(caseBook)).Length > 0,
            "Shared-network projection terminates through cycles and retains each directed connection once");
        var sameTitle = new Book { Title = epic.Title, Author = "Another Fictional" };
        var ambiguous = BibliographyGraph.Build(library.Concat([sameTitle]).ToArray(), id => id == philosophers.Id ? [epicCitation with { Author = "Fictional" }] : []);
        check(ambiguous.Connections.Single().Target.StartsWith("cited:") && ambiguous.Nodes[ambiguous.Connections.Single().Target].Identity.Contains("Ambiguous"),
            "Ambiguous library matches stay separate instead of creating false bridges to multiple books");
        var unknown = BibliographyGraph.Build(library, id => id == philosophers.Id || id == other.Id ?
            [remote with { Author = "" }, remote with { Title = "", Text = "A complete unresolved note." }] : []);
        check(unknown.Statistics().All(row => row.CitingBooks == 1) && unknown.TextOnlyEntries == 2,
            "Missing-author external works are not merged across sources and text-only notes do not invent work identities");
        check(graph.Path(Key(epic), "absent").Length == 0 && BibliographyGraph.Build([], _ => []).Statistics().Length == 0,
            "Empty graphs and unknown path endpoints are handled without fabricated connections");
        using var store = new LibraryStore(System.IO.Path.Combine(workspace, "graph-snapshot")); store.SaveBatch(library);
        store.SaveEpubBibliography(philosophers.Id, new("synthetic.epub", "hash", [], [epicCitation, remote,
            remote with { Title = "A Single-Source Leaf", Text = "First single-source evidence." },
            remote with { Title = "A Single-Source Leaf", Text = "Second single-source evidence." }], 0));
        store.SaveEpubBibliography(caseBook.Id, new("synthetic-case.epub", "hash", [], fromCase, 0));
        store.SaveBibliography(other.Id, "Fictional, Dante. *The Imaginary Comedy*. Synthetic Press, 2020.\n\nInvented, Theo. *An External Work*. Synthetic Press, 2021.", null);
        var snapshot = store.ReadBibliographyGraph();
        check(snapshot.Statistics().Single(row => row.Work.BookId == epic.Id).CitingBooks == 2 && snapshot.Path(Key(epic), Key(caseBook)).Length == 3 && store.Changes(epic.Id).Count == 1,
            "Graph snapshots combine structured and editable bibliographies without changing books or history");
    }
}
