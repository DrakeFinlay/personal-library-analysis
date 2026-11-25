using System.Text.Json;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record LibraryQuery(ReadingStatus? Status = ReadingStatus.ToRead, string? Shelf = null,
    string? Group = null, string Search = "", bool ByAuthor = false, bool Descending = false,
    bool ShowExcluded = false, bool MissingCover = false, int? Limit = 50);
public sealed record LibraryResults(List<Book> Books, int Total);

public sealed partial class LibraryStore
{
    public Book? FindBook(Guid id)
    {
        lock (gate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT document FROM books WHERE id=$id";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteScalar() is string text ? DecodeBook(text) : null;
        }
    }

    private static Book DecodeBook(string text)
    {
        var book = JsonSerializer.Deserialize<Book>(text, Json) ?? throw new InvalidDataException("Invalid book data.");
        if (book.Goodreads is { } source && GoodreadsImporter.IsOutsideScope(source.Fields)) book.Excluded = true;
        ReadingRules.Validate(book); RestoreSourcePublishers(book); return book;
    }

    public IReadOnlyList<string> LibraryNames(bool groups = false)
    {
        lock (gate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DISTINCT value FROM books, json_each(document, '$.{(groups ? "ToReadGroups" : "Shelves")}') ORDER BY value COLLATE NOCASE";
            using var reader = command.ExecuteReader(); var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names.Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }

    public LibraryResults Browse(LibraryQuery query)
    {
        lock (gate)
        {
            using var connection = Open();
            connection.CreateFunction<string?, string?, bool>("contains_text", (text, term) =>
                (text ?? "").Contains(term ?? "", StringComparison.CurrentCultureIgnoreCase));
            connection.CreateFunction<string?, string?, bool>("same_text", (a, b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase));
            connection.CreateFunction<string, bool>("missing_cover", text => DecodeBook(text).Editions.All(edition => CoverPath(edition) == null));
            var where = """
                WHERE ($excluded OR coalesce(json_extract(document,'$.Excluded'),0)=0)
                AND ($status IS NULL OR json_extract(document,'$.Status')=$status)
                AND ($shelf IS NULL OR EXISTS (SELECT 1 FROM json_each(document,'$.Shelves') WHERE same_text(value,$shelf)))
                AND ($group IS NULL OR EXISTS (SELECT 1 FROM json_each(document,'$.ToReadGroups') WHERE same_text(value,$group)))
                AND ($search='' OR contains_text(json_extract(document,'$.Title'),$search)
                    OR contains_text(json_extract(document,'$.Author'),$search)
                    OR EXISTS (SELECT 1 FROM json_each(document,'$.Editions') WHERE contains_text(json_extract(value,'$.Isbn'),$search)))
                AND (NOT $missing OR missing_cover(document))
                """;
            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$excluded", query.ShowExcluded);
            command.Parameters.AddWithValue("$status", query.Status?.ToString() is { } status ? status : DBNull.Value);
            command.Parameters.AddWithValue("$shelf", (object?)query.Shelf ?? DBNull.Value);
            command.Parameters.AddWithValue("$group", (object?)query.Group ?? DBNull.Value);
            command.Parameters.AddWithValue("$search", query.Search.Trim()); command.Parameters.AddWithValue("$missing", query.MissingCover);
            command.CommandText = "SELECT count(*) FROM books " + where;
            var total = Convert.ToInt32(command.ExecuteScalar());
            var field = query.ByAuthor ? "Author" : "Title";
            var direction = query.Descending ? "DESC" : "ASC";
            command.CommandText = $"SELECT document FROM books {where} ORDER BY json_extract(document,'$.{field}') COLLATE NOCASE {direction}, json_extract(document,'$.Title') COLLATE NOCASE, id LIMIT $limit";
            command.Parameters.AddWithValue("$limit", query.Limit is { } limit ? Math.Max(1, limit) : -1);
            using var reader = command.ExecuteReader(); var books = new List<Book>();
            while (reader.Read()) books.Add(DecodeBook(reader.GetString(0)));
            return new(books, total);
        }
    }
}
