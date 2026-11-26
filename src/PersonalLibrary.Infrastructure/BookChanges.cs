using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace PersonalLibrary.Infrastructure;

public sealed record BookChange(long Sequence, Guid BookId, DateTimeOffset At, string Kind,
    string? Before, string? After, string? Details);
public sealed record BookFieldChange(string Field, string Before, string After);
public sealed record AttachmentChange(string File, string? Before, string After, string[]? Images = null);

public sealed partial class LibraryStore
{
    private void EnsureSchema()
    {
        using var connection = Open(); using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var current = Convert.ToInt32(version.ExecuteScalar());
        if (current > 2) throw new InvalidOperationException("This profile was created by a newer app. Upgrade before opening it.");
        if (current == 2) return;
        if (current == 1) PreviewBackup(Backup("pre-history-migration"));
        using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS books(id TEXT PRIMARY KEY, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE book_changes(sequence INTEGER PRIMARY KEY AUTOINCREMENT, book_id TEXT NOT NULL,
                occurred_at TEXT NOT NULL, kind TEXT NOT NULL, before_document TEXT, after_document TEXT, details TEXT);
            CREATE INDEX book_changes_book ON book_changes(book_id, sequence DESC);
            INSERT INTO book_changes(book_id,occurred_at,kind,after_document)
                SELECT id,strftime('%Y-%m-%dT%H:%M:%fZ','now'),'Tracking started',document FROM books;
            CREATE TRIGGER book_created AFTER INSERT ON books BEGIN
                INSERT INTO book_changes(book_id,occurred_at,kind,after_document)
                VALUES(new.id,strftime('%Y-%m-%dT%H:%M:%fZ','now'),'Book added',new.document);
            END;
            CREATE TRIGGER book_changed AFTER UPDATE ON books WHEN old.document<>new.document BEGIN
                INSERT INTO book_changes(book_id,occurred_at,kind,before_document,after_document)
                VALUES(new.id,strftime('%Y-%m-%dT%H:%M:%fZ','now'),'Book changed',old.document,new.document);
            END;
            CREATE TRIGGER book_removed AFTER DELETE ON books BEGIN
                INSERT INTO book_changes(book_id,occurred_at,kind,before_document)
                VALUES(old.id,strftime('%Y-%m-%dT%H:%M:%fZ','now'),'Book removed',old.document);
            END;
            PRAGMA user_version=2;
            """;
        command.ExecuteNonQuery(); transaction.Commit();
    }

    public IReadOnlyList<BookChange> Changes(Guid bookId, int limit = 100, long? before = null)
    {
        lock (gate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT sequence,occurred_at,kind,before_document,after_document,details FROM book_changes WHERE book_id=$id AND ($before IS NULL OR sequence<$before) ORDER BY sequence DESC LIMIT $limit";
            command.Parameters.AddWithValue("$id", bookId.ToString()); command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
            command.Parameters.AddWithValue("$before", (object?)before ?? DBNull.Value);
            using var reader = command.ExecuteReader(); var entries = new List<BookChange>();
            while (reader.Read()) entries.Add(new(reader.GetInt64(0), bookId, DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            return entries;
        }
    }

    private void RecordAttachmentChange(Guid bookId, string file, string? before, string after, string[]? images = null)
    {
        if (before == after && images is not { Length: > 0 }) return;
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE books SET document=json_set(document,'$.UpdatedAt',$at) WHERE id=$id";
        command.Parameters.AddWithValue("$id", bookId.ToString()); command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The bibliography's source book was removed.");
        command.CommandText = "UPDATE book_changes SET kind='Bibliography changed',details=$details WHERE sequence=(SELECT max(sequence) FROM book_changes WHERE book_id=$id)";
        command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(new AttachmentChange(file, before, after, images), Json));
        command.ExecuteNonQuery(); transaction.Commit();
    }

    public static IReadOnlyList<BookFieldChange> ChangeFields(BookChange change)
    {
        var differences = new List<BookFieldChange>();
        Compare("", change.Before == null ? null : JsonNode.Parse(change.Before), change.After == null ? null : JsonNode.Parse(change.After));
        return differences;
        void Compare(string path, JsonNode? before, JsonNode? after)
        {
            if (JsonNode.DeepEquals(before, after)) return;
            if (before is JsonObject left && after is JsonObject right)
            {
                foreach (var key in left.Select(item => item.Key).Union(right.Select(item => item.Key)))
                    if (key != "UpdatedAt") Compare(path.Length == 0 ? key : path + "." + key, left[key], right[key]);
            }
            else if (before is JsonArray leftArray && after is JsonArray rightArray)
            {
                for (var i = 0; i < Math.Max(leftArray.Count, rightArray.Count); i++)
                    Compare($"{path}[{i + 1}]", i < leftArray.Count ? leftArray[i] : null, i < rightArray.Count ? rightArray[i] : null);
            }
            else differences.Add(new(path.Length == 0 ? "Book snapshot" : path,
                before?.ToJsonString(Json) ?? "(not recorded)", after?.ToJsonString(Json) ?? "(not recorded)"));
        }
    }
}
