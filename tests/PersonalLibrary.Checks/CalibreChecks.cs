using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class CalibreChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var folder = Path.Combine(workspace, "synthetic-calibre");
        var bookFolder = Path.Combine(folder, "Reader", "First book (1)");
        Directory.CreateDirectory(bookFolder);
        var catalog = Path.Combine(folder, "metadata.db");
        var sourceCover = Path.Combine(bookFolder, "cover.jpg");
        File.WriteAllBytes(sourceCover, [1, 3, 5, 7]);
        File.WriteAllText(Path.Combine(bookFolder, "First book.epub"), "Synthetic file link");
        Write(catalog, """
            CREATE TABLE library_id(uuid TEXT);
            CREATE TABLE books(id INTEGER PRIMARY KEY, uuid TEXT, title TEXT, path TEXT, pubdate TEXT, series_index REAL);
            CREATE TABLE authors(id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE books_authors_link(id INTEGER PRIMARY KEY, book INTEGER, author INTEGER);
            CREATE TABLE series(id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE books_series_link(book INTEGER, series INTEGER);
            CREATE TABLE publishers(id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE books_publishers_link(book INTEGER, publisher INTEGER);
            CREATE TABLE languages(id INTEGER PRIMARY KEY, lang_code TEXT);
            CREATE TABLE books_languages_link(book INTEGER, lang_code INTEGER, item_order INTEGER);
            CREATE TABLE identifiers(book INTEGER, type TEXT, val TEXT);
            CREATE TABLE comments(book INTEGER, text TEXT);
            CREATE TABLE tags(id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE books_tags_link(book INTEGER, tag INTEGER);
            CREATE TABLE data(book INTEGER, format TEXT, name TEXT);
            INSERT INTO library_id VALUES('b29e9f60-85d6-4f15-98c7-779de3cd9bd0');
            INSERT INTO books VALUES
              (1,'d36490cf-4132-42ec-933a-48d6e6925380','First book','Reader/First book (1)','2020-01-01 00:00:00+00:00',2.5),
              (2,'35c75e2b-48ee-4f6d-9d20-2fd969855dc9','Synthetic comic','Comic (2)','0101-01-01 00:00:00+00:00',1);
            INSERT INTO authors VALUES(1,'Sample Author'),(2,'Another Author');
            INSERT INTO books_authors_link VALUES(1,1,2),(2,1,1);
            INSERT INTO series VALUES(1,'Sample series');
            INSERT INTO books_series_link VALUES(1,1);
            INSERT INTO publishers VALUES(1,'Sample Press');
            INSERT INTO books_publishers_link VALUES(1,1);
            INSERT INTO languages VALUES(1,'eng');
            INSERT INTO books_languages_link VALUES(1,1,0);
            INSERT INTO identifiers VALUES(1,'isbn','9781234567890'),(1,'example','a');
            INSERT INTO comments VALUES(1,'<p>Plain &amp; simple</p>');
            INSERT INTO tags VALUES(1,'Unmapped genre'),(2,'Manga');
            INSERT INTO books_tags_link VALUES(1,1),(2,2);
            INSERT INTO data VALUES(1,'EPUB','First book'),(2,'PDF','../../outside');
            """);
        var hash = SHA256.HashData(File.ReadAllBytes(catalog));
        using var store = new LibraryStore(Path.Combine(workspace, "calibre-target"));
        var preview = CalibreImporter.Preview(store, folder);
        check(preview.Added == 2 && preview.Covers == 1 && preview.WithoutLocalFiles == 1
            && store.Books().Count == 0 && store.Setting(CalibreImporter.FolderSetting, "") == "", "Calibre preview reads without applying");
        store.ApplyCalibre(preview);
        var books = store.Books();
        var first = books.Single(book => book.Title == "First book");
        var comic = books.Single(book => book.Title == "Synthetic comic");
        check(books.All(book => book.Status == ReadingStatus.Unclassified && book.Readings.Count == 0 && book.Shelves.Count == 0)
            && comic.Excluded && comic.SelectedEdition.PublicationYear == null, "Calibre import leaves statuses and shelves unclassified and excludes explicit manga");
        check(first.Author == "Another Author, Sample Author" && first.SelectedEdition.PublicationYear == 2020
            && first.OriginalYear == null && first.SelectedEdition.Isbn == "9781234567890"
            && first.SelectedEdition.Publisher == "Sample Press" && comic.SelectedEdition.Publisher == null
            && first.SelectedEdition.Holdings.Contains("Ebook") && first.SelectedEdition.PageCount == null,
            "Calibre edition fields and ebook ownership preserve missing values");
        var source = first.Calibre!;
        check(source.Metadata.SeriesIndex == 2.5 && source.Metadata.Publisher == "Sample Press"
            && source.Metadata.Description == "Plain & simple" && source.Metadata.Tags.SequenceEqual(["Unmapped genre"])
            && File.Exists(CalibreImporter.ResolvePath(source.LibraryFolder, source.Metadata.Files.Single().RelativePath))
            && comic.Calibre!.Metadata.Files.Length == 0, "Calibre source details and scoped file links");
        check(File.Exists(store.CoverPath(first.SelectedEdition)) && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(catalog)))
            && store.Setting(CalibreImporter.FolderSetting, "") == folder, "Import copies cover and saves connection without modifying Calibre");
        check(CalibreImporter.Preview(store, folder).Unchanged == 2, "Re-refresh is idempotent");
        check(Directory.GetFiles(store.BackupDirectory, "pre-calibre-import-*.zip").Length == 1, "Import creates a pre-change backup");
        first.SelectedEdition.Publisher = null;
        store.Save(first);
        check(store.Books().Single(book => book.Id == first.Id).SelectedEdition.Publisher == "Sample Press",
            "Existing Calibre records recover publisher from their saved source metadata");
        File.Delete(store.CoverPath(first.SelectedEdition)!);
        var coverRepair = CalibreImporter.Preview(store, folder);
        store.ApplyCalibre(coverRepair);
        check(coverRepair.Updated == 1 && File.Exists(store.CoverPath(first.SelectedEdition))
            && CalibreImporter.Preview(store, folder).Unchanged == 2, "Calibre refresh restores a missing cover file even when metadata is unchanged");

        first.Title = "Personal title";
        first.Shelves.Add("Personal shelf");
        first.SelectedEdition.PageCount = 250;
        first.SelectedEdition.Publisher = "Local Press";
        ReadingRules.Classify(first, ReadingStatus.Read);
        store.Save(first);
        Write(catalog, """
            UPDATE books SET title='Revised in Calibre' WHERE id=1;
            UPDATE authors SET name='Corrected Author' WHERE id=2;
            UPDATE publishers SET name='Revised Calibre Press' WHERE id=1;
            DELETE FROM books WHERE id=2;
            INSERT INTO books VALUES (3,'3574d58d-a6cd-4023-bd8a-650551ec5b2b','New on refresh','New (3)',NULL,1);
            """);
        File.WriteAllBytes(sourceCover, [9, 8, 7]);
        var refresh = CalibreImporter.Preview(store, folder);
        check(refresh.Added == 1 && refresh.Updated == 1 && refresh.MissingFromSource == 1, "Refresh reports additions, updates and absent records");
        first.Notes = "Edited after preview";
        store.Save(first);
        try
        {
            store.ApplyCalibre(refresh);
            check(false, "Stale Calibre preview must be rejected");
        }
        catch (InvalidOperationException)
        {
            check(store.Books().Count == 2, "Stale Calibre preview is rejected before writes");
        }
        store.ApplyCalibre(CalibreImporter.Preview(store, folder));
        var updated = store.Books().Single(book => book.Id == first.Id);
        check(store.Books().Count == 3 && updated.Title == "Personal title" && updated.Author == "Corrected Author, Sample Author"
            && updated.Notes == "Edited after preview" && updated.Shelves.SequenceEqual(["Personal shelf"])
            && updated.SelectedEdition.Id == first.SelectedEdition.Id && updated.SelectedEdition.PageCount == 250
            && updated.SelectedEdition.Publisher == "Local Press" && updated.Calibre?.Metadata.Publisher == "Revised Calibre Press"
            && updated.Status == ReadingStatus.Read && updated.Readings.Single().Finished == null
            && File.ReadAllBytes(store.CoverPath(updated.SelectedEdition)!).SequenceEqual(new byte[] { 9, 8, 7 }),
            "Refresh updates untouched source fields and preserves local metadata and undated reading history");
        ReadingRules.Classify(updated, ReadingStatus.ToRead);
        ReadingRules.Classify(updated, ReadingStatus.Read);
        check(updated.Readings.Count == 1 && ReadingRules.CompletedInYear([updated], DateTime.Today.Year) == 0,
            "Bulk Read classification reuses history and does not add a dated completion");
        var goodreadsPath = Path.Combine(workspace, "calibre-refresh-goodreads.csv");
        File.WriteAllText(goodreadsPath, """
            Book Id,Title,Author,Exclusive Shelf
            42,Revised in Calibre,"Corrected Author, Sample Author",read
            """);
        updated.Title = updated.Calibre!.Metadata.Title;
        store.Save(updated);
        store.ApplyGoodreads(GoodreadsImporter.Preview(store, goodreadsPath));
        Write(catalog, "UPDATE books SET title='Another Calibre title' WHERE id=1; UPDATE authors SET name='Another Calibre author' WHERE id=2;");
        store.ApplyCalibre(CalibreImporter.Preview(store, folder));
        updated = store.Books().Single(book => book.Id == first.Id);
        check(updated.Title == "Revised in Calibre" && updated.Author == "Corrected Author, Sample Author" &&
            updated.Calibre!.Metadata.Title == "Another Calibre title", "Calibre refresh keeps authoritative Goodreads fields and updates its own source snapshot");
        store.SaveBibliography(updated.Id, "Synthetic notes for removal", null);
        var removed = store.RemoveBooks([updated]);
        var afterRemoval = CalibreImporter.Preview(store, folder);
        check(removed.Removed == 1 && store.Books().All(book => book.Id != updated.Id)
            && !File.Exists(store.BibliographyPath(updated.Id)) && afterRemoval.PreviouslyRemoved == 1
            && afterRemoval.Added == 0 && File.Exists(catalog),
            "Personal-only removal archives notes and prevents Calibre refresh from re-adding the book");
    }

    private static void Write(string catalog, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = catalog, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
