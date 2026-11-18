using System.IO.Compression;
using System.Text.Json;
using PersonalLibrary.Infrastructure;

internal static class GoodreadsRatingsChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        using var store = new LibraryStore(Path.Combine(workspace, "goodreads-ratings-target"));
        var csv = Path.Combine(workspace, "ratings.csv");
        File.WriteAllText(csv, """
            Book Id,Title,Author,Exclusive Shelf,My Rating,My Review
            101,Coastal History,Example Writer,read,0,An existing review
            102,Local Rating,Example Writer,read,0,
            103,Shared Title,Example Writer,read,0,
            104,Shared Title,Different Writer,read,0,
            105,Conflicting Archive,Example Writer,read,0,
            106,Blank Rating,Example Writer,read,0,
            107,CSV Rating,Example Writer,read,4,
            """);
        store.ApplyGoodreads(GoodreadsImporter.Preview(store, csv));
        var local = store.Books().Single(book => book.Title == "Local Rating");
        local.Rating = 2;
        store.Save(local);
        var target = store.Books().Single(book => book.Title == "Coastal History");
        var path = Path.Combine(workspace, "ratings.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var buffer = new MemoryStream())
        {
            using (var nested = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(nested.CreateEntry("reviews.json").Open()))
            {
                writer.Write("""
                    [{"explanation":["Sample export"],"reviews":[
                      {"book":"Coastal History","rating":5.0},
                      {"book":"Local Rating","rating":1},
                      {"book":"Shared Title","rating":5},
                      {"book":"Conflicting Archive","rating":4},
                      {"book":"Conflicting Archive","rating":2},
                      {"book":"Blank Rating","rating":4.5},
                      {"book":"CSV Rating","rating":5},
                      {"book":"Unlinked Book","rating":3}
                    ],"review_drafts":"None"}]
                    """);
            }
            buffer.Position = 0;
            using var output = archive.CreateEntry("reviews.zip").Open();
            buffer.CopyTo(output);
        }

        var preview = GoodreadsRatings.Preview(store, csv, path);
        check(preview.Changes.Single() is { Stars: 5 } change && change.BookId == target.Id &&
              preview.AlreadyRated == 2 && preview.UnmatchedArchiveRatings == 4,
            "Goodreads rating recovery holds ambiguous and unlinked titles and preserves prior ratings");
        target.Notes = "Keep this personal note";
        store.Save(target);
        reject(() => store.ApplyGoodreadsRatings(preview), "Stale Goodreads rating previews are rejected");
        preview = GoodreadsRatings.Preview(store, csv, path);
        target = store.Books().Single(book => book.Id == target.Id);
        store.ApplyGoodreadsRatings(preview);
        var saved = store.Books().Single(book => book.Id == target.Id);
        target.Rating = 5;
        target.UpdatedAt = saved.UpdatedAt;
        check(JsonSerializer.Serialize(target, LibraryStore.Json) == JsonSerializer.Serialize(saved, LibraryStore.Json) &&
              Directory.EnumerateFiles(store.BackupDirectory, "pre-goodreads-ratings-*.zip").Count() == 1,
            "Goodreads rating recovery backs up the profile and changes only the selected rating");
        check(GoodreadsRatings.Preview(store, csv, path).Changes.Count == 0 && GoodreadsImporter.Preview(store, csv).Updated == 0,
            "Repeating rating recovery or importing a zero-star CSV preserves existing stars");
    }
}
