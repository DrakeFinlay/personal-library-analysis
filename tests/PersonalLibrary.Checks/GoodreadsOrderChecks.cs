using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class GoodreadsOrderChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        using var store = new LibraryStore(Path.Combine(workspace, "order-recovery"));
        Book Linked(string title, string id, string positions, string added)
        {
            var book = new Book { Title = title, Author = "A Fictional Author" };
            book.Goodreads = new(id, new() { ["Bookshelves with positions"] = positions, ["Date Added"] = added }, [], book.SelectedEdition.Id, null);
            return book;
        }
        var ten = Linked("Alphabetically first", "1001", "history (#1), to-read (#10)", "2020/1/3");
        ten.SelectedEdition.Label = "Goodreads edition";
        ten.Goodreads!.Fields["Publisher"] = "Saved source publisher";
        var two = Linked("Alphabetically last", "1002", "to-read (#2), philosophy (#7)", "2018/12/31");
        var unknown = Linked("Missing rank", "1003", "history (#2)", "invalid");
        store.SaveBatch([ten, two, unknown]); var before = store.Books();
        var preview = GoodreadsOrder.Preview(before);
        check(preview.Positions == 2 && preview.Dates == 2 && preview.MissingToReadPositions == 1,
            "Recovery distinguishes canonical To-read positions from unrelated shelf positions and leaves invalid dates unknown");
        var backup = store.ApplyGoodreadsOrder(preview);
        check(File.Exists(backup) && store.Books().All(book => book.Readings.Count == 0 && book.Author == "A Fictional Author") &&
            store.FindBook(ten.Id)!.DateAdded == new DateOnly(2020, 1, 3) &&
            LibraryStore.ChangeFields(store.Changes(ten.Id)[0]).All(change => change.Field is "DateAdded" or "ToReadPosition"),
            "Targeted Goodreads recovery creates a verified backup and changes only position/date fields");
        check(store.Browse(new()).Books.Select(book => book.Id).SequenceEqual([two.Id, ten.Id, unknown.Id]) &&
            store.Browse(new(Descending: true)).Books.Select(book => book.Id).SequenceEqual([ten.Id, two.Id, unknown.Id]),
            "Default rank sorting is numeric, reversible, and keeps unknown positions last in both directions");
        check(store.Browse(new(Sort: LibrarySort.DateAdded)).Books.Select(book => book.Id).SequenceEqual([two.Id, ten.Id, unknown.Id]),
            "Date-added sorting uses recorded dates and leaves unknown dates last");
        check(GoodreadsOrder.Preview(store.Books()).Changes.Length == 0 && store.Changes(ten.Id).Count == 2,
            "Recovery is idempotent and the applied fields are retained in per-book change history");
        reject(() => store.ApplyGoodreadsOrder(preview), "A stale position-recovery preview is rejected");
        var fields = new Dictionary<string, string> { ["Bookshelves with positions"] = "to-read (#0)", ["Date Added"] = "2026/02/30" };
        check(GoodreadsOrder.Position(fields) == null && GoodreadsOrder.Added(fields) == null,
            "Zero positions and impossible dates remain unknown");
        var csv = Path.Combine(workspace, "synthetic-ranked-goodreads.csv");
        File.WriteAllText(csv, "Book Id,Title,Author,Exclusive Shelf,Bookshelves with positions,Date Added\n1001,Alphabetically first,A Fictional Author,to-read,to-read (#12),2020/01/03\n");
        store.ApplyGoodreads(GoodreadsImporter.Preview(store, csv));
        check(store.FindBook(ten.Id)!.ToReadPosition == 12, "A regular Goodreads refresh updates an unchanged source-managed position");
        var local = store.FindBook(ten.Id)!; local.ToReadPosition = null; local.ToReadPositionIsLocal = true;
        local.DateAdded = new DateOnly(2010, 1, 1); local.DateAddedIsLocal = true; store.Save(local);
        store.ApplyGoodreads(GoodreadsImporter.Preview(store, csv));
        check(store.FindBook(ten.Id) is { ToReadPosition: null, DateAdded.Year: 2010 } && GoodreadsOrder.Preview(store.Books()).Changes.Length == 0,
            "Deliberately cleared ranks and locally corrected dates survive future imports and recovery");
        var invalid = new Book { Title = "Bad rank", ToReadPosition = -1 };
        reject(() => store.Save(invalid), "Negative manual positions are rejected");
        var duplicate = new Book { Title = "Duplicate", DateAdded = new DateOnly(2000, 1, 1), ToReadPosition = 4 };
        store.Save(duplicate); store.MergeBooks(store.FindBook(ten.Id)!, duplicate);
        check(store.FindBook(ten.Id) is { ToReadPosition: null, ToReadPositionIsLocal: true, DateAdded.Year: 2010, DateAddedIsLocal: true },
            "Merging preserves deliberate local rank/date corrections, including a cleared rank");
    }
}
