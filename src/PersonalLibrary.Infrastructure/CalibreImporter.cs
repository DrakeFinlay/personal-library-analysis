using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record CalibreChange(string Action, string Title, string Author, bool Excluded);

public sealed class CalibrePreview
{
    internal record Pending(Book Book, DateTimeOffset? ExpectedUpdate, byte[]? Cover);
    internal List<Pending> PendingBooks { get; } = [];
    public required string Folder { get; init; }
    public Guid LibraryId { get; internal set; }
    public List<CalibreChange> Changes { get; } = [];
    public int Added { get; internal set; }
    public int Updated { get; internal set; }
    public int Unchanged { get; internal set; }
    public int MissingFromSource { get; internal set; }
    public int WithoutLocalFiles { get; internal set; }
    public int Covers { get; internal set; }
    public int PreviouslyRemoved { get; internal set; }
}

public static class CalibreImporter
{
    public const string FolderSetting = "calibre-folder";

    public static CalibrePreview Preview(LibraryStore store, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose your Calibre library folder.");
        var preview = new CalibrePreview { Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim())) };
        var database = Path.Combine(preview.Folder, "metadata.db");
        if (!File.Exists(database)) throw new InvalidOperationException("Choose the Calibre library folder that contains metadata.db.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT uuid FROM library_id LIMIT 1";
            if (!Guid.TryParse(command.ExecuteScalar() as string, out var libraryId))
                throw new InvalidDataException("This Calibre catalog has no valid library identity.");
            preview.LibraryId = libraryId;
            var existing = store.Books().Where(book => book.Calibre?.LibraryId == libraryId)
                .ToDictionary(book => book.Calibre!.BookId);
            var suppressed = store.RemovedCalibreBooks(libraryId);
            var sourceIds = new HashSet<Guid>();

            command.CommandText = """
                SELECT b.uuid, b.title, b.path, b.pubdate, b.series_index,
                  (SELECT json_group_array(name) FROM
                    (SELECT a.name FROM books_authors_link l JOIN authors a ON a.id = l.author WHERE l.book = b.id ORDER BY l.id)),
                  (SELECT s.name FROM books_series_link l JOIN series s ON s.id = l.series WHERE l.book = b.id LIMIT 1),
                  (SELECT p.name FROM books_publishers_link l JOIN publishers p ON p.id = l.publisher WHERE l.book = b.id LIMIT 1),
                  (SELECT n.lang_code FROM books_languages_link l JOIN languages n ON n.id = l.lang_code WHERE l.book = b.id ORDER BY l.item_order LIMIT 1),
                  (SELECT val FROM identifiers WHERE book = b.id AND lower(type) = 'isbn' LIMIT 1),
                  (SELECT text FROM comments WHERE book = b.id LIMIT 1),
                  (SELECT json_group_array(name) FROM
                    (SELECT t.name FROM books_tags_link l JOIN tags t ON t.id = l.tag WHERE l.book = b.id ORDER BY t.name)),
                  (SELECT json_group_array(identifier) FROM
                    (SELECT type || ': ' || val AS identifier FROM identifiers WHERE book = b.id ORDER BY type, val)),
                  (SELECT json_group_array(json_object('Format', format, 'RelativePath', name || '.' || lower(format))) FROM
                    (SELECT format, name FROM data WHERE book = b.id ORDER BY format, name))
                FROM books b ORDER BY b.id
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!Guid.TryParse(Text(reader, 0), out var bookId) || !sourceIds.Add(bookId))
                    throw new InvalidDataException("The Calibre catalog contains an invalid or duplicate book identity.");
                if (suppressed.Contains(bookId))
                {
                    preview.PreviouslyRemoved++;
                    continue;
                }
                var title = Text(reader, 1).Trim();
                if (title.Length == 0) title = "Untitled Calibre book";
                var path = Text(reader, 2);
                var series = Text(reader, 6);
                var tags = JsonSerializer.Deserialize<string[]>(Text(reader, 11)) ?? [];
                var identifiers = JsonSerializer.Deserialize<string[]>(Text(reader, 12)) ?? [];
                var files = (JsonSerializer.Deserialize<CalibreFile[]>(Text(reader, 13)) ?? [])
                    .Where(file => !Path.IsPathRooted(file.RelativePath))
                    .Select(file => file with { RelativePath = Path.Combine(path, file.RelativePath) })
                    .Where(file => ResolvePath(preview.Folder, file.RelativePath) != null).ToArray();
                var available = files.Any(file => File.Exists(ResolvePath(preview.Folder, file.RelativePath)));
                if (!available) preview.WithoutLocalFiles++;
                var coverPath = ResolvePath(preview.Folder, Path.Combine(path, "cover.jpg"));
                var cover = File.Exists(coverPath) ? File.ReadAllBytes(coverPath!) : null;
                if (cover != null) preview.Covers++;
                existing.TryGetValue(bookId, out var current);
                var source = new CalibreMetadata(title, string.Join(", ", JsonSerializer.Deserialize<string[]>(Text(reader, 5)) ?? []),
                    Text(reader, 9), Text(reader, 8), PublicationYear(Text(reader, 3)), Text(reader, 7),
                    series, series.Length > 0 && !reader.IsDBNull(4) ? reader.GetDouble(4) : null,
                    PlainText(Text(reader, 10)), tags, identifiers, files,
                    cover == null ? current?.Calibre?.Metadata.CoverFile : LibraryStore.CoverName(cover, ".jpg"));
                var restoreCover = cover != null && current?.Calibre is { } reference
                    && store.CoverPath(current.Editions.Single(edition => edition.Id == reference.EditionId)) == null;
                AddChange(preview, bookId, source, current, cover, available, restoreCover);
            }
            preview.MissingFromSource = existing.Keys.Count(id => !sourceIds.Contains(id));
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("Calibre's catalog could not be read. Check that this is a Calibre library folder and try again if Calibre is busy.", ex);
        }
        return preview;
    }

    private static void AddChange(CalibrePreview preview, Guid bookId, CalibreMetadata source, Book? current, byte[]? cover, bool available,
        bool restoreCover)
    {
        var previous = current?.Calibre;
        if (previous != null && previous.LibraryFolder == preview.Folder && SameMetadata(previous.Metadata, source) && !restoreCover)
        {
            preview.Unchanged++;
            return;
        }
        var book = current == null ? new Book { Status = ReadingStatus.Unclassified } : LibraryStore.Clone(current);
        var edition = previous == null ? book.SelectedEdition : book.Editions.Single(e => e.Id == previous.EditionId);
        var old = previous?.Metadata;
        if (old == null)
        {
            edition.Label = "Calibre edition";
            book.SelectedEditionId = edition.Id;
            if (available) edition.Holdings.Add("Ebook");
            book.Excluded = source.Tags.Any(tag => ComicTags.Contains(tag.Trim()))
                || source.Files.Any(file => ComicFormats.Contains(file.Format));
        }
        var goodreads = book.Goodreads;
        var goodreadsAuthor = goodreads == null ? "" : GoodreadsImporter.Field(goodreads.Fields, "Author");
        var hasGoodreadsEdition = goodreads != null && goodreads.EditionId == edition.Id && GoodreadsImporter.HasEditionIsbn(goodreads);
        if (goodreads == null && (old == null || book.Title == old.Title)) book.Title = source.Title;
        if ((goodreadsAuthor.Length == 0 || goodreadsAuthor.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) &&
            (old == null || book.Author == old.Author)) book.Author = source.Author;
        if (!hasGoodreadsEdition && (old == null || edition.Isbn == old.Isbn)) edition.Isbn = source.Isbn;
        if (!string.IsNullOrWhiteSpace(source.Publisher)) edition.Publisher ??= source.Publisher.Trim();
        if (old == null || edition.Language == old.Language) edition.Language = source.Language;
        if ((!hasGoodreadsEdition || GoodreadsImporter.Year(goodreads!.Fields, "Year Published") == null) &&
            (old == null || edition.PublicationYear == old.PublicationYear)) edition.PublicationYear = source.PublicationYear;
        if (old == null || edition.CoverFile == old.CoverFile || restoreCover)
            edition.CoverFile = source.CoverFile;
        book.Calibre = new CalibreReference(preview.LibraryId, bookId, preview.Folder, edition.Id, source);
        preview.PendingBooks.Add(new CalibrePreview.Pending(book, current?.UpdatedAt,
            edition.CoverFile == source.CoverFile ? cover : null));
        preview.Changes.Add(new CalibreChange(current == null ? "Add" : "Update", book.Title, book.Author, book.Excluded));
        if (current == null) preview.Added++;
        else preview.Updated++;
    }

    private static bool SameMetadata(CalibreMetadata left, CalibreMetadata right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    public static string? ResolvePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) return null;
        try
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (ArgumentException) { return null; }
    }

    private static readonly HashSet<string> ComicTags = new(["manga", "comic", "comics", "graphic novel", "graphic novels"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ComicFormats = new(["cbz", "cbr", "cb7", "cbc"], StringComparer.OrdinalIgnoreCase);
    private static string Text(SqliteDataReader reader, int field) => reader.IsDBNull(field) ? "" : reader.GetString(field);
    private static int? PublicationYear(string value) =>
        value.Length >= 10 && DateOnly.TryParseExact(value.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) && date != new DateOnly(101, 1, 1) ? date.Year : null;

    private static string PlainText(string html)
    {
        var breaks = Regex.Replace(html, @"<\s*(?:br\b[^>]*|/\s*(?:p|div|li|h[1-6])\s*)>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return WebUtility.HtmlDecode(Regex.Replace(breaks, "<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(1))).Trim();
    }
}
