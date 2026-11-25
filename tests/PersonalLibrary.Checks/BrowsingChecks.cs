using System.Diagnostics;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class BrowsingChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        using var store = new LibraryStore(Path.Combine(workspace, "bounded-library"));
        var books = Enumerable.Range(0, 2000).Select(i => new Book
        {
            Title = $"Fictional title {i:D4}", Author = i == 1800 ? "Émile Example" : "Imaginary Author",
            Status = i % 4 == 0 ? ReadingStatus.Unclassified : ReadingStatus.ToRead,
            Shelves = i % 2 == 0 ? ["history"] : ["philosophy"],
            ToReadGroups = i % 3 == 0 ? ["Research for a book", " Biblical research ", "research for a book"] : [],
            Notes = "Personal notes", Excluded = i == 1999
        }).ToArray();
        store.SaveBatch(books);
        var watch = Stopwatch.StartNew();
        var first = store.Browse(new()); watch.Stop();
        check(first.Books.Count == 50 && first.Total == 1499 && first.Books.All(book => book.Status == ReadingStatus.ToRead),
            "Default database browse loads only 50 To read books and reports the full filtered count");
        Console.WriteLine($"MEASURE 2,000-book synthetic first-50 database query: {watch.Elapsed.TotalMilliseconds:0.0} ms");
        var filtered = store.Browse(new(Shelf: "HISTORY", Group: "research for a book", Limit: null));
        check(filtered.Books.Count > 50 && filtered.Books.All(book => book.Shelves.Contains("history") && book.ToReadGroups.Count == 2 && book.Status == ReadingStatus.ToRead),
            "Shelf and overlapping user groups intersect the chosen status and Show all expands only matching books");
        var beyond = store.Browse(new(Status: null, Search: "ÉMILE"));
        check(beyond.Books.Single().Id == books[1800].Id && store.Browse(new(Status: null, Search: "' OR 1=1 --")).Total == 0,
            "Search reaches books outside the first page, supports case-insensitive Unicode, and treats SQL-like input as text");
        var all = store.Browse(new(Status: null, Limit: null));
        check(all.Books.Count == 1999 && store.Browse(new(Status: null, ShowExcluded: true, Limit: null)).Total == 2000,
            "All statuses and excluded-record controls remain independent of the result limit");
        var descending = store.Browse(new(Descending: true, Sort: LibrarySort.Title));
        check(string.CompareOrdinal(descending.Books[0].Title, descending.Books[^1].Title) > 0,
            "Sorting is applied before the first-50 database limit");
        var saved = store.FindBook(books[6].Id)!;
        check(saved.ToReadGroups.SequenceEqual(["Research for a book", "Biblical research"]) && saved.Notes == "Personal notes" && store.LibraryNames(groups: true).Count == 2,
            "User groups trim and deduplicate without changing shelves or personal notes");
        ReadingRules.Classify(saved, ReadingStatus.Read); store.Save(saved);
        check(store.Browse(new(Status: ReadingStatus.Read, Shelf: "history", Group: "Biblical research")).Books.Single().Id == saved.Id,
            "Switching status preserves the same shelf/group intersection and saved memberships");
    }
}
