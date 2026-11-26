using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class CitationDiscoveryChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var first = new Book { Title = "Fictional history source", Author = "A. Historian", Status = ReadingStatus.Read, Rating = 5 };
        var second = new Book { Title = "Fictional biblical source", Author = "B. Researcher", Status = ReadingStatus.Read };
        var third = new Book { Title = "Another fictional source", Author = "C. Researcher", Status = ReadingStatus.Read, Rating = 2 };
        var fourth = new Book { Title = "Another enjoyed source", Author = "D. Scholar", Status = ReadingStatus.Read, Rating = 4 };
        var local = new Book { Title = "A Shared Local Work", Author = "L. Writer", Status = ReadingStatus.ToRead };
        var read = new Book { Title = "A Previously Read Work", Author = "R. Writer", Status = ReadingStatus.Read };
        var dismissed = new Book { Title = "A Dismissed Work", Author = "X. Writer", RecommendationDismissed = true };
        var excluded = new Book { Title = "An Excluded Work", Author = "Y. Writer", Excluded = true };
        EpubCitation Cite(string title, string author = "E. Writer") => new(1, "Bibliography", $"{author}, {title} (Town: Press, 2020).", title, author, "works.xhtml", null);
        var unexpected = Cite("An Unexpected Shared Work"); var enjoyed = Cite("A Bridge Between Enjoyed Books", "F. Writer"); var single = Cite("A Single Source Work");
        var others = new[] { Cite(local.Title, local.Author), Cite(read.Title, read.Author), Cite(dismissed.Title, dismissed.Author), Cite(excluded.Title, excluded.Author) };
        var data = new Dictionary<Guid, EpubCitation[]>
        {
            [first.Id] = [unexpected, enjoyed, single, single with { Text = "Another note about the same single-source work." }, .. others],
            [second.Id] = [unexpected, .. others], [third.Id] = [unexpected], [fourth.Id] = [enjoyed]
        };
        Book[] library = [first, second, third, fourth, local, read, dismissed, excluded];
        var graph = BibliographyGraph.Build(library, id => CitationProcessing.Process(data.GetValueOrDefault(id) ?? []));
        var leads = CitationDiscovery.Rank(library, graph);
        check(leads.First().Title == enjoyed.Title && leads.First().EnjoyedSources == 2 && leads.Single(lead => lead.Title == unexpected.Title).CitingBooks == 3,
            "Discovery prioritizes shared references between enjoyed books while retaining bridges through unrated sources");
        check(!leads.Any(lead => lead.Title == single.Title || lead.Title == read.Title || lead.Title == dismissed.Title || lead.Title == excluded.Title) &&
            leads.Single(lead => lead.Title == local.Title).Pick.LocalBook?.Id == local.Id,
            "Reading leads exclude single-source repeats, read/dismissed/excluded targets and map eligible local works to My Books");
        check(CitationDiscovery.Rank(library, graph, includeOtherStatuses: true).Any(lead => lead.Title == read.Title) &&
            !CitationDiscovery.Rank(library, graph, includeOtherStatuses: true).Any(lead => lead.Title == dismissed.Title || lead.Title == excluded.Title),
            "Discovery can include already-read works for exploration while retaining deliberate exclusions and dismissals");
        var focused = CitationDiscovery.Rank(library, graph, new HashSet<Guid> { first.Id, second.Id });
        check(focused.Length == 2 && focused.All(lead => lead.Pick.Sources.Any(item => item.Source.Id == first.Id) && lead.Pick.Sources.Any(item => item.Source.Id == second.Id)) &&
            CitationDiscovery.Rank(library, graph, new HashSet<Guid> { second.Id, fourth.Id }).Length == 0,
            "Source focus requires every selected bibliography rather than accepting any selected source");
        var uncertain = Cite("An Unresolved Work") with { Interpretation = new("", CitationResolution.NeedsReview, "Unknown short reference") };
        check(CitationDiscovery.Rank(library, BibliographyGraph.Build(library, id => id == first.Id || id == second.Id ? [uncertain] : [])).Length == 0,
            "Unresolved interpretations do not manufacture shared discovery leads");
        local.RecommendationSnoozedUntil = DateTimeOffset.Now.AddDays(1);
        check(!CitationDiscovery.Rank(library, graph).Any(lead => lead.Pick.LocalBook?.Id == local.Id), "Discovery respects local Not now preferences");
        local.RecommendationSnoozedUntil = null;
        foreach (var book in library.Where(book => book.Status == ReadingStatus.Read))
        {
            book.Status = ReadingStatus.Unclassified; ReadingRules.Start(book, null); ReadingRules.Finish(book, null, ReadingOutcome.Completed);
        }
        using var store = new LibraryStore(Path.Combine(workspace, "discovery")); store.SaveBatch(library);
        foreach (var source in data) store.SaveEpubBibliography(source.Key, new("synthetic.epub", "synthetic-source-" + source.Key, [], source.Value, 0));
        store.SetSetting("citation-recommendation:synthetic", "dismissed"); store.SetSetting("unrelated", "leave alone");
        check(store.CitationPreferences().Count == 1 && store.CitationPreferences()["citation-recommendation:synthetic"] == "dismissed",
            "Discovery loads citation preferences in one bounded namespace without unrelated settings");
        check(CitationDiscovery.Rank(store.Books(), store.ReadBibliographyGraph()).Any(lead => lead.Title == unexpected.Title && lead.CitingBooks == 3),
            "Discovery consumes saved processed bibliography evidence without a personal-data import");
    }
}
