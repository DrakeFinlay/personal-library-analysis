using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using static PersonalLibrary.Infrastructure.OpenLibraryLookup;

namespace PersonalLibrary.Infrastructure;

public sealed class OfflineCatalog(string root)
{
    private string CatalogPath => Path.Combine(root, "resources", "open-library.sqlite");
    public bool Installed => File.Exists(CatalogPath);

    public long Install(string authorsPath, string editionsPath, IProgress<string>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
        var staging = CatalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var count = Build(staging, authorsPath, editionsPath, progress, token);
            token.ThrowIfCancellationRequested();
            File.Move(staging, CatalogPath, overwrite: true);
            return count;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private static long Build(string path, string authorsPath, string editionsPath, IProgress<string>? progress, CancellationToken token)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            PRAGMA journal_mode=OFF;
            PRAGMA synchronous=OFF;
            CREATE TEMP TABLE authors(id TEXT PRIMARY KEY, name TEXT NOT NULL) WITHOUT ROWID;
            CREATE TABLE editions(id TEXT NOT NULL UNIQUE, work TEXT, title TEXT NOT NULL, author TEXT NOT NULL,
                isbn TEXT NOT NULL, year INTEGER, pages INTEGER, publisher TEXT NOT NULL, cover INTEGER);
            CREATE TABLE identifiers(isbn TEXT NOT NULL, edition INTEGER NOT NULL, PRIMARY KEY(isbn,edition)) WITHOUT ROWID;
            CREATE VIRTUAL TABLE search_index USING fts5(title,author,content='editions',content_rowid='rowid');
            """;
        schema.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        using var addAuthor = Command(connection, transaction, "INSERT OR REPLACE INTO authors VALUES($id,$name)", "$id", "$name");
        long authors = 0;
        foreach (var (key, data) in Rows(authorsPath, "/type/author", token))
        {
            if (Id(key, 'A') is not { } id || Str(data, "name") is not { Length: > 0 } name) continue;
            Execute(addAuthor, id, name);
            if (++authors % 100000 == 0) progress?.Report($"Indexed {authors:N0} author names…");
        }
        if (authors == 0) throw new InvalidDataException("The authors dump has no Open Library author records.");
        using var authorName = Command(connection, transaction, "SELECT name FROM authors WHERE id=$id", "$id");
        using var edition = Command(connection, transaction, """
            INSERT OR IGNORE INTO editions VALUES($id,$work,$title,$author,$isbn,$year,$pages,$publisher,$cover)
            RETURNING rowid
            """, "$id", "$work", "$title", "$author", "$isbn", "$year", "$pages", "$publisher", "$cover");
        using var identifier = Command(connection, transaction, "INSERT OR IGNORE INTO identifiers VALUES($isbn,$edition)", "$isbn", "$edition");
        long count = 0;
        foreach (var (key, data) in Rows(editionsPath, "/type/edition", token))
        {
            if (Id(key, 'M') is not { } id || Str(data, "title") is not { Length: > 0 } title) continue;
            var subtitle = Str(data, "subtitle");
            if (subtitle.Length > 0 && !title.Contains(subtitle, StringComparison.OrdinalIgnoreCase)) title += ": " + subtitle;
            var names = new List<string>();
            foreach (var author in Items(data, "authors"))
            {
                if (Id(Str(author, "key"), 'A') is not { } authorId) continue;
                Bind(authorName, authorId);
                if (authorName.ExecuteScalar() is string name) names.Add(name);
            }
            var isbns = Items(data, "isbn_13").Concat(Items(data, "isbn_10"))
                .Select(value => IsbnKey(Str(value))).Where(value => value.Length > 0).Distinct().ToArray();
            var work = Id(Str(Items(data, "works").FirstOrDefault(), "key"), 'W');
            var year = Regex.Match(Str(data, "publish_date"), @"\b[0-9]{4}\b", RegexOptions.RightToLeft).Value;
            var cover = Items(data, "covers").FirstOrDefault(value =>
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0);
            Bind(edition, id, work, title, string.Join(", ", names), isbns.FirstOrDefault() ?? "",
                int.TryParse(year, out var published) && published > 0 ? published : (int?)null,
                Number(data, "number_of_pages") is > 0 and var pages ? pages : null,
                string.Join(", ", Items(data, "publishers").Select(Str).Where(value => value.Length > 0)),
                cover.ValueKind == JsonValueKind.Number ? cover.GetInt32() : (int?)null);
            if (edition.ExecuteScalar() is not long rowId) continue;
            foreach (var isbn in isbns) Execute(identifier, isbn, rowId);
            if (++count % 100000 == 0) progress?.Report($"Imported {count:N0} editions…");
        }
        if (count == 0) throw new InvalidDataException("The editions dump has no Open Library edition records.");
        progress?.Report("Building the title and author search index…");
        using var registration = token.Register(() => InterruptIndex(connection));
        using var index = Command(connection, transaction, "INSERT INTO search_index(search_index) VALUES('rebuild')");
        index.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return count;
    }

    private static void InterruptIndex(SqliteConnection connection) =>
        SQLitePCL.raw.sqlite3_interrupt(connection.Handle);

    public IReadOnlyList<MetadataMatch> Search(string title, string author, string isbn, CancellationToken token = default)
    {
        if (!Installed) return [];
        using var connection = Open(CatalogPath, SqliteOpenMode.ReadOnly);
        using var cancellation = token.Register(() => InterruptIndex(connection));
        var key = IsbnKey(isbn);
        if (key.Length > 0)
        {
            var exact = Read(connection, """
                SELECT e.*, i.isbn FROM identifiers i JOIN editions e ON e.rowid=i.edition
                WHERE i.isbn=$query LIMIT 25
                """, key, token);
            if (exact.Count > 0) return exact;
        }
        var tokens = Terms("title", title).Concat(Terms("author", author)).ToArray();
        if (tokens.Length == 0) return [];
        return Read(connection, """
            SELECT e.*, e.isbn FROM search_index JOIN editions e ON e.rowid=search_index.rowid
            WHERE search_index MATCH $query ORDER BY rank LIMIT 25
            """, string.Join(" AND ", tokens), token);
    }

    private static IEnumerable<string> Terms(string field, string value) => Regex.Matches(value, @"[\p{L}\p{M}\p{N}]+")
        .Select(token => $"{field}:\"{token.Value}\"*");

    private static List<MetadataMatch> Read(SqliteConnection connection, string sql, string query, CancellationToken token)
    {
        using var command = Command(connection, null, sql, "$query");
        Bind(command, query);
        using var reader = command.ExecuteReader();
        var results = new List<MetadataMatch>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var cover = CoverById(reader.IsDBNull(8) ? null : reader.GetInt32(8));
            var title = reader.GetString(2);
            var author = reader.GetString(3);
            var edition = new EditionMetadata(title, author, reader.GetString(9),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetString(7), cover);
            results.Add(new(title, author, null, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(0), cover, edition));
        }
        return results;
    }

    private static IEnumerable<(string Key, JsonElement Data)> Rows(string path, string type, CancellationToken token)
    {
        using var file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new StreamReader(input);
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            if (!line.StartsWith(type + "\t", StringComparison.Ordinal)) continue;
            var fields = line.Split('\t', 5);
            if (fields.Length != 5) throw new InvalidDataException("An Open Library dump row is incomplete.");
            using var json = JsonDocument.Parse(fields[4]);
            yield return (fields[1], json.RootElement);
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in parameters) command.Parameters.AddWithValue(name, DBNull.Value);
        return command;
    }

    private static void Bind(SqliteCommand command, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
    }

    private static void Execute(SqliteCommand command, params object?[] values)
    {
        Bind(command, values);
        command.ExecuteNonQuery();
    }
}
