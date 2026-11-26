using System.IO.Compression;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class LibraryCleanupChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var plain = new Book { Title = "1Q84", Author = "Haruki Murakami" };
        var decorated = new Book { Title = "1Q84 (1Q84, #1-3)", Author = plain.Author };
        var volume1 = new Book { Title = "Collected Letters: Volume 1", Author = "Example Reader", OpenLibraryWorkId = "OL8W" };
        var volume2 = new Book { Title = "Collected Letters: Volume 2", Author = volume1.Author, OpenLibraryWorkId = "OL8W" };
        var excluded = new Book { Title = plain.Title, Author = plain.Author, Excluded = true };
        var probable = BookIdentity.FindDuplicates([plain, decorated, volume1, volume2, excluded]).Single();
        check(new[] { probable.First.Id, probable.Second.Id }.ToHashSet().SetEquals([plain.Id, decorated.Id]),
            "Duplicate review recognizes series annotations and filters out known excluded books and separate volumes");
        using var store = new LibraryStore(Path.Combine(workspace, "catalog-and-cleanup"));
        var authors = Path.Combine(workspace, "authors.tsv");
        var editions = Path.Combine(workspace, "editions.tsv.gz");
        File.WriteAllText(authors, "/type/author\t/authors/OL1A\t1\tdate\t{\"name\":\"Fictional Reader\"}\n");
        using (var file = File.Create(editions))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new StreamWriter(gzip))
            writer.Write("/type/edition\t/books/OL2M\t1\tdate\t" + """
                {"title":"The Catalog Lantern","authors":[{"key":"/authors/OL1A"}],"works":[{"key":"/works/OL3W"}],"isbn_10":["0441172717"],"publish_date":"May 1980","number_of_pages":192,"publishers":["Lantern Press"],"covers":[null,-1,123]}
                """);
        var catalog = new OfflineCatalog(store.Root);
        check(!catalog.Installed && catalog.Search("Lantern", "", "").Count == 0, "Offline lookup works without an installed catalog");
        check(catalog.Install(authors, editions, null, CancellationToken.None) == 1, "Offline catalog builds from the Open Library dump format");
        var exact = catalog.Search("", "", "978-0-441-17271-9").Single();
        check(exact.Edition?.Pages == 192 && exact.Edition.Year == 1980 && exact.OriginalYear == null && exact.Cover != null
            && catalog.Search("Catalog Lant", "Fictional", "").Single().EditionId == "OL2M",
            "Offline title, author and equivalent ISBN search return an identified edition");
        File.WriteAllText(authors, "");
        try { catalog.Install(authors, editions, null, CancellationToken.None); }
        catch (InvalidDataException) { }
        check(catalog.Search("Lantern", "", "").Count == 1, "A failed catalog rebuild preserves the installed index");

        var first = new Book { Title = exact.Title, Author = exact.Author, Notes = "Calibre note" };
        first.SelectedEdition.Isbn = "0441172717";
        first.SelectedEdition.Holdings.Add("Ebook");
        first.Calibre = new(Guid.NewGuid(), Guid.NewGuid(), "Synthetic", first.SelectedEdition.Id,
            new(first.Title, first.Author, first.SelectedEdition.Isbn, "", null, "", "", null, "", [], [], [], null));
        var second = new Book { Title = exact.Title + ": A Novel", Author = exact.Author, Rating = 5, Review = "Goodreads review" };
        second.SelectedEdition.Isbn = "9780441172719";
        second.SelectedEdition.PageCount = 200;
        second.SelectedEdition.OpenLibraryEditionId = exact.EditionId;
        ReadingRules.Classify(second, ReadingStatus.Read);
        second.Goodreads = new("123", [], [], second.SelectedEdition.Id, second.Readings.Single().Id);
        store.SaveBatch([first, second]);
        var preview = CatalogReconciliation.Preview(store, catalog);
        store.ApplyCatalogMatches(preview);
        var books = store.Books();
        check(preview.Editions.Count == 2 && books.Single(book => book.Id == first.Id).SelectedEdition.PageCount == 192
            && books.Single(book => book.Id == second.Id).SelectedEdition.PageCount == 200
            && preview.Editions.All(edition => edition.Publisher == "Lantern Press")
            && books.All(book => book.SelectedEdition.Publisher == "Lantern Press")
            && CatalogReconciliation.Preview(store, catalog).Editions.Count == 0,
            "Catalog reconciliation fills missing edition metadata and preserves a corrected page count");
        var pair = BookIdentity.FindDuplicates(books).Single();
        check(pair.Evidence == "Same ISBN" && BookIdentity.MatchesSearch(first, "", "", second.SelectedEdition.Isbn),
            "Duplicate review and add-book search recognize equivalent ISBNs");
        var keep = books.Single(book => book.Id == first.Id);
        var duplicate = books.Single(book => book.Id == second.Id);
        store.SaveBibliography(keep.Id, "Saved bibliography", null);
        store.MergeBooks(store.FindBook(keep.Id)!, store.FindBook(duplicate.Id)!);
        var combined = store.Books().Single();
        check(combined.Id == keep.Id && combined.Status == ReadingStatus.Read && combined.Readings.Single().EditionId == second.SelectedEdition.Id
            && combined.Editions.Count == 2 && combined.Calibre?.BookId == first.Calibre.BookId && combined.Goodreads?.BookId == second.Goodreads.BookId
            && combined.Rating == 5 && combined.Review == "Goodreads review" && combined.Notes == "Calibre note"
            && store.Bibliography(combined.Id).Text == "Saved bibliography"
            && Directory.GetFiles(store.BackupDirectory, "pre-book-merge-*.zip").Length == 1,
            "Reviewed merge preserves editions, reading identity, both source links, personal writing and bibliography");
    }
}
