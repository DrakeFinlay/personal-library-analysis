using System.IO.Compression;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class GoodreadsChecks
{
    private static readonly string[] Headers = ["Book Id", "Title", "Author", "ISBN", "ISBN13", "My Rating", "My Review",
        "Number of Pages", "Year Published", "Original Publication Year", "Date Read", "Date Added", "Bookshelves",
        "Exclusive Shelf", "Read Count", "Unmapped Field", "Publisher"];

    public static void Run(string workspace, Action<bool, string> check)
    {
        using var store = new LibraryStore(Path.Combine(workspace, "goodreads-target"));
        var matching = new Book { Title = "Old local title", Author = "Synthetic Writer", Notes = "Private notes", Review = "Old review",
            Rating = 2, Shelves = ["local shelf"], Status = ReadingStatus.ToRead, ReadNext = true };
        matching.SelectedEdition.Isbn = "0-9802004-4-X";
        matching.SelectedEdition.Holdings = ["Ebook"];
        matching.SelectedEdition.PageCount = 75;
        var origin = new CalibreMetadata(matching.Title, matching.Author, matching.SelectedEdition.Isbn, "", null, "",
            "", null, "", [], [], [new("EPUB", "fixture.epub")], null);
        matching.Calibre = new CalibreReference(Guid.NewGuid(), Guid.NewGuid(), workspace, matching.SelectedEdition.Id, origin);
        ReadingRules.Start(matching, new DateOnly(2025, 1, 1));
        ReadingRules.Progress(matching, 30, ProgressUnit.Page);
        var differentEdition = new Book { Title = "Other Book", Author = "Synthetic Writer" };
        differentEdition.SelectedEdition.Isbn = "9781626251700";
        differentEdition.SelectedEdition.PageCount = 90;
        differentEdition.SelectedEdition.Publisher = "First Edition Press";
        var legacy = new Book { Title = "Old Reading", Author = "Another Author" };
        ReadingRules.Start(legacy, new DateOnly(2018, 1, 1));
        ReadingRules.Finish(legacy, new DateOnly(2018, 1, 2), ReadingOutcome.Completed);
        var legacyReading = legacy.Readings.Single().Id;
        var ambiguous = new Book { Title = "Shared Title", Author = "Unknown" };
        var localOnly = new Book { Title = "Local Only", Status = ReadingStatus.ReferenceOnly };
        store.SaveBatch([matching, differentEdition, legacy, ambiguous, localOnly]);
        store.SaveBibliography(matching.Id, "# Local bibliography", null);
        var review = "A comma, a \"quote\" and café.\nSecond line.";
        var path = Path.Combine(workspace, "synthetic-goodreads.csv");
        var rows = new[]
        {
            new[] { "1", "Corrected title", matching.Author, "", "=\"9780980200447\"", "5", review, "80", "2009", "2001", "2025/01/05", "2024/08/11", "history, favorites", "read", "1", "preserved", "Sample Press" },
            new[] { "2", differentEdition.Title, differentEdition.Author, "", "9780385533225", "0", "", "121", "2016", "", "", "2023/12/31", "language", "to-read", "0", "", "Second Edition Press" },
            new[] { "3", legacy.Title, legacy.Author, "", "", "0", "", "", "", "", "2018/01/02", "", "read-b4-goodreads, classics", "read", "1", "", "" },
            new[] { "4", "Graphic Story", "Fictional Author", "", "", "4", "", "", "", "", "2024/01/02", "", "manga, favorites", "read", "3", "", "" },
            new[] { "5", ambiguous.Title, "New Author", "", "", "0", "", "", "", "", "", "", "", "currently-reading", "0", "", "" },
            new[] { "6", "Broken duplicate", "New Author", "", "", "", "", "", "", "", "", "", "", "read", "", "", "" },
            new[] { "6", "Broken duplicate", "New Author", "", "", "", "", "", "", "", "", "", "", "read", "", "", "" }
        };
        Write(path, rows);
        var preview = GoodreadsImporter.Preview(store, path);
        check(preview.Added == 0 && preview.Updated == 3 && preview.Skipped == 4 && preview.Excluded == 1 &&
            preview.LegacyUndated == 1 && store.Books().Count == 5, "Goodreads preview separates updates, out-of-scope manga, ambiguous, and duplicate rows without writing");
        var unresolved = preview.Rows.Single(row => row.Action == "Review");
        check(unresolved.Candidates.Single().Id == ambiguous.Id, "A shared title with uncertain author needs explicit confirmation");
        preview.Resolve(unresolved.Number, ambiguous.Id);
        store.ApplyGoodreads(preview);
        var imported = store.Books().ToDictionary(book => book.Id);
        var synced = imported[matching.Id];
        check(synced.Title == "Corrected title" && synced.Rating == 5 && synced.Review == review && synced.OriginalYear == 2001 &&
            synced.Status == ReadingStatus.Read && synced.Shelves.SequenceEqual(new[] { "local shelf", "history", "favorites" }),
            "Goodreads wins overlapping reading and work fields and preserves quoted CSV text");
        check(synced.Calibre?.BookId == matching.Calibre.BookId && synced.SelectedEdition.Holdings.Single() == "Ebook" &&
            synced.SelectedEdition.Publisher == "Sample Press" &&
            synced.SelectedEdition.PageCount == 80 && synced.Notes == "Private notes" && store.Bibliography(matching.Id).Text == "# Local bibliography" &&
            synced.Readings.Single().Finished == new DateOnly(2025, 1, 5) && synced.Readings.Single().Progress.Single().Value == 30,
            "ISBN reconciliation keeps the existing Calibre link, ownership, bibliography, notes, and reading progress");
        var toRead = imported[differentEdition.Id];
        check(toRead.Editions.Count == 2 && toRead.SelectedEdition.Id == differentEdition.SelectedEdition.Id &&
            toRead.SelectedEdition.PageCount == 90 && toRead.Editions.Single(edition => edition.Id == toRead.Goodreads!.EditionId).PageCount == 121 &&
            toRead.SelectedEdition.Publisher == "First Edition Press" &&
            toRead.Editions.Single(edition => edition.Id == toRead.Goodreads!.EditionId).Publisher == "Second Edition Press" &&
            toRead.ToReadAddedAt.Year == 2023, "An exact work match with a different ISBN keeps both editions and the Goodreads shelf date");
        var undated = imported[legacy.Id];
        check(undated.Readings.Single().Id == legacyReading && undated.Readings.Single().Started == null &&
            undated.Readings.Single().Finished == null && !undated.Shelves.Contains("read-b4-goodreads") &&
            undated.Goodreads!.Fields["Date Read"] == "2018/01/02", "Pre-Goodreads completion is undated while original date and shelf are retained in provenance");
        check(imported.Values.All(book => book.Title != "Graphic Story") && ReadingRules.CompletedInYear(imported.Values, 2024) == 0 &&
            imported[ambiguous.Id].ActiveReading?.Progress.Count == 0 && imported[localOnly.Id].Goodreads == null,
            "Manga creates no library record or reading history, and other local records are untouched");
        var report = store.LastGoodreadsImport();
        check(report?.SkippedSourceFields.Length == 3 && report.SkippedSourceFields.Any(fields => fields["Book Id"] == "4")
            && synced.Goodreads!.Fields["Unmapped Field"] == "preserved" &&
            Directory.GetFiles(store.BackupDirectory, "pre-goodreads-import-*.zip").Length == 1,
            "Import persists a backup, source fields, and skipped rows in its report");
        var repeat = GoodreadsImporter.Preview(store, path);
        check(repeat.Added + repeat.Updated == 0 && repeat.Unchanged == 4 && repeat.Skipped == 3,
            "Repeating the same export leaves books, editions, and history unchanged");

        synced.SelectedEdition.Publisher = null;
        store.Save(synced);
        check(store.Books().Single(book => book.Id == matching.Id).SelectedEdition.Publisher == "Sample Press",
            "Existing Goodreads records recover publisher from the saved ISBN-matched export row");

        rows[0][10] = "2025/03/07";
        rows[0][12] = "history";
        rows[0][14] = "2";
        rows[0][16] = "Revised Goodreads Press";
        synced.SelectedEdition.Publisher = "";
        store.Save(synced);
        Write(path, rows);
        var refresh = GoodreadsImporter.Preview(store, path);
        store.ApplyGoodreads(refresh);
        var reread = store.Books().Single(book => book.Id == matching.Id);
        check(reread.Readings.Count == 2 && reread.Readings.Last().Finished == new DateOnly(2025, 3, 7) &&
            reread.SelectedEdition.Publisher == "" &&
            reread.Shelves.SequenceEqual(new[] { "local shelf", "history" }), "A later Goodreads reread adds its supplied date and reconciles removed source shelves");
        check(GoodreadsImporter.Preview(store, path).Updated == 0, "The updated reread import is idempotent");

        var stale = GoodreadsImporter.Preview(store, path);
        store.Save(new Book { Title = "Added after preview" });
        var rejected = false;
        try { store.ApplyGoodreads(stale); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "A stale Goodreads preview is rejected before backup or writes");
        CheckReadingArchive(workspace, check);
        var existingManga = new Book { Title = "Test illustrated series" };
        existingManga.Goodreads = new("900", new() { ["Bookshelves"] = "to-read-manga" }, [], existingManga.SelectedEdition.Id, null);
        var bookAboutManga = new Book { Title = "A History of Manga" };
        bookAboutManga.Goodreads = new("901", new() { ["Title"] = bookAboutManga.Title, ["Bookshelves"] = "manga-history" }, [], bookAboutManga.SelectedEdition.Id, null);
        store.SaveBatch([existingManga, bookAboutManga]);
        var classified = store.Books().Where(book => book.Id == existingManga.Id || book.Id == bookAboutManga.Id).ToDictionary(book => book.Id);
        check(classified[existingManga.Id].Excluded && !classified[bookAboutManga.Id].Excluded,
            "Previously imported records with explicit manga shelves are hidden while books about the subject stay in scope");
    }

    private static void CheckReadingArchive(string workspace, Action<bool, string> check)
    {
        using var store = new LibraryStore(Path.Combine(workspace, "goodreads-archive-target"));
        var csv = Path.Combine(workspace, "reading-archive.csv");
        File.WriteAllText(csv, """
            Book Id,Title,Author,Exclusive Shelf,Date Read,Bookshelves
            100,Coastal History,Example Writer,read,2025/04/09,
            101,Already Read,Example Writer,read,2024/01/02,read-b4-goodreads
            102,New Reading,Example Writer,currently-reading,,
            103,Shared Title,Example Writer,read,,
            104,Shared Title,Different Writer,read,,
            105,Partial Reading,Example Writer,read,2024/03/04,partially-read
            106,Japanese Reading,Example Writer,to-read-in-japanese,,
            """);
        var path = Path.Combine(workspace, "reading-archive.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var buffer = new MemoryStream())
        {
            using (var nested = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(nested.CreateEntry("reading_sessions.json").Open()))
            {
                writer.Write("""
                    [
                      {"explanation":["Sample export"]},
                      {"book":"Coastal History","state":"COMPLETED","started_at":"2020-01-01 00:00:00 UTC","ended_at":"2020-01-05 00:00:00 UTC"},
                      {"book":"Coastal History","state":"COMPLETED","started_at":"2025-04-07 00:00:00 UTC","ended_at":"2025-04-09 00:00:00 UTC"},
                      {"book":"Already Read","state":"COMPLETED","ended_at":"2024-01-02 00:00:00 UTC"},
                      {"book":"New Reading","state":"READING","started_at":"2025-01-11 00:00:00 UTC","ended_at":"(not provided)"},
                      {"book":"Shared Title","state":"COMPLETED","ended_at":"2023-01-02 00:00:00 UTC"},
                      {"book":"Partial Reading","state":"COMPLETED","ended_at":"2024-03-04 00:00:00 UTC"}
                    ]
                    """);
            }
            buffer.Position = 0;
            using var output = archive.CreateEntry("reading_sessions.zip").Open();
            buffer.CopyTo(output);
        }
        var preview = GoodreadsImporter.Preview(store, csv, path);
        check(preview.Rows.Count(row => row.Details.Contains("Archive sessions were skipped")) == 2,
            "Goodreads archive sessions are skipped for ambiguous CSV titles and flagged in the preview");
        store.ApplyGoodreads(preview);
        var books = store.Books();
        check(books.Single(book => book.Title == "Coastal History").Readings.OrderBy(reading => reading.Finished)
                  .Select(reading => reading.Started).SequenceEqual(new DateOnly?[] { new(2020, 1, 1), new(2025, 4, 7) })
              && books.Single(book => book.Title == "Already Read").Readings.Single().Finished == null
              && books.Single(book => book.Title == "New Reading").ActiveReading?.Started == new DateOnly(2025, 1, 11)
              && books.Single(book => book.Title == "Partial Reading").Readings.Single().Outcome == ReadingOutcome.PartiallyRead,
            "Goodreads archive restores dated rereads and current starts while respecting legacy and partial shelves");
        var again = GoodreadsImporter.Preview(store, csv, path);
        check(again.Unchanged == 7, "Repeating a Goodreads import with reading sessions leaves history unchanged");
        var japanese = books.Single(book => book.Title == "Japanese Reading");
        check(japanese.Status == ReadingStatus.ToRead && japanese.Shelves.Contains("to-read-in-japanese"),
            "The Japanese to-read shelf keeps its name and contributes to To read");
    }

    private static void Write(string path, string[][] rows) =>
        File.WriteAllLines(path, rows.Prepend(Headers).Select(row =>
            string.Join(',', row.Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\""))));
}
