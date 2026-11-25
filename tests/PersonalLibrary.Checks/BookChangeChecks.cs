using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class BookChangeChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        var root = Path.Combine(workspace, "book-changes"); var book = new Book { Title = "A Fictional Record" };
        using (var store = new LibraryStore(root))
        {
            store.Save(book); var timestamp = book.UpdatedAt; store.Save(book);
            check(store.Changes(book.Id).Count == 1 && book.UpdatedAt == timestamp, "Saving an unchanged book creates no audit event or artificial recent-change timestamp");
            book.Notes = "First note"; store.Save(book);
            var edit = store.Changes(book.Id)[0];
            check(LibraryStore.ChangeFields(edit).Any(field => field.Field == "Notes" && field.After.Contains("First note")),
                "Saved metadata history retains before and after field values");
            ReadingRules.Start(book, new DateOnly(2026, 1, 1)); store.Save(book);
            ReadingRules.Progress(book, 10, ProgressUnit.Percent); store.Save(book);
            ReadingRules.Finish(book, new DateOnly(2026, 1, 2), ReadingOutcome.Completed); store.Save(book);
            ReadingRules.Start(book, new DateOnly(2026, 2, 1)); store.Save(book);
            check(store.Changes(book.Id).Count == 6 && LibraryStore.ChangeFields(store.Changes(book.Id)[0]).Any(field => field.Field.StartsWith("Readings")),
                "Status transitions, progress, finishes, and rereads each retain their saved state in history");
            var oldTimestamp = store.FindBook(book.Id)!.UpdatedAt;
            store.SaveBibliography(book.Id, "A fictional reference", null);
            check(store.Changes(book.Id)[0].Kind == "Bibliography changed" && store.Changes(book.Id)[0].Details!.Contains("A fictional reference") &&
                store.FindBook(book.Id)!.UpdatedAt != oldTimestamp, "Bibliography edits record text revisions and update the source book's recent-change timestamp");
            var backup = store.Backup("history-test"); var count = store.Changes(book.Id).Count;
            book.Notes = "Later note"; store.Save(book); store.Restore(backup);
            check(store.Changes(book.Id).Count == count && store.FindBook(book.Id)!.Notes == "First note", "Backup and restore preserve book history alongside the matching book state");
            var first = store.Changes(book.Id, 2); var next = store.Changes(book.Id, 2, first[^1].Sequence);
            check(first.Count == 2 && next.Count == 2 && first.All(item => next.All(other => item.Sequence != other.Sequence)),
                "Change history pages load older events without repeating entries");
            var beforeFailure = store.Changes(book.Id).Count;
            var invalid = new Book { Title = "Invalid", Rating = 0 }; book.Notes = "Must not persist";
            reject(() => store.SaveBatch([book, invalid]), "Invalid batch rejected before saving books or audit events");
            check(store.Changes(book.Id).Count == beforeFailure && store.FindBook(book.Id)!.Notes == "First note", "Failed batch leaves book state and audit history unchanged");
            var other = new Book { Title = "Duplicate fictional record" }; store.Save(other);
            var fresh = store.FindBook(book.Id)!; store.MergeBooks(fresh, other);
            check(store.Changes(fresh.Id).Any(item => item.Kind == "Book removed") && store.Changes(fresh.Id).Any(item => item.After?.Contains("Duplicate fictional record") == true),
                "Merging books preserves both audit histories under the surviving record");
        }
        var legacyRoot = Path.Combine(workspace, "legacy-audit"); Directory.CreateDirectory(Path.Combine(legacyRoot, "profile"));
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(legacyRoot, "profile", "personal.sqlite")};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE books(id TEXT PRIMARY KEY,document TEXT NOT NULL); CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL); PRAGMA user_version=1; INSERT INTO books VALUES($id,$doc)";
            command.Parameters.AddWithValue("$id", book.Id.ToString()); command.Parameters.AddWithValue("$doc", JsonSerializer.Serialize(book, LibraryStore.Json)); command.ExecuteNonQuery();
        }
        using (var store = new LibraryStore(legacyRoot))
        {
            check(store.Changes(book.Id).Single().Kind == "Tracking started" && Directory.GetFiles(store.BackupDirectory, "pre-history-migration-*.zip").Length == 1,
                "Schema migration backs up old profiles and labels existing states as baselines rather than invented past changes");
            var legacyBackup = Directory.GetFiles(store.BackupDirectory, "pre-history-migration-*.zip").Single();
            store.Restore(legacyBackup);
            var saved = store.FindBook(book.Id)!; saved.Notes = "After legacy restore"; store.Save(saved);
            check(store.Changes(book.Id).Count == 2, "Restoring a legacy backup re-enables audit tracking before subsequent saves");
        }
    }
}
