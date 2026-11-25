using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record GoodreadsOrderRecovery(Book[] Changes, Dictionary<Guid, DateTimeOffset> Baseline,
    int Positions, int Dates, int MissingToReadPositions);

public static partial class GoodreadsOrder
{
    public static int? Position(Dictionary<string, string> fields)
    {
        var matches = ToReadPosition().Matches(GoodreadsImporter.Field(fields, "Bookshelves with positions"));
        if (matches.Count != 1) return null;
        return int.TryParse(matches[0].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    public static DateOnly? Added(Dictionary<string, string> fields) =>
        DateOnly.TryParseExact(GoodreadsImporter.Field(fields, "Date Added"), ["yyyy/M/d", "yyyy-M-d"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;

    public static void ApplySource(Book book, GoodreadsReference? prior, Dictionary<string, string> fields)
    {
        var position = Position(fields); var date = Added(fields);
        if (!book.ToReadPositionIsLocal && position.HasValue && (book.ToReadPosition == null || prior != null && book.ToReadPosition == Position(prior.Fields)))
            book.ToReadPosition = position;
        if (!book.DateAddedIsLocal && date.HasValue && (book.DateAdded == null || prior != null && book.DateAdded == Added(prior.Fields)))
            book.DateAdded = date;
    }

    public static GoodreadsOrderRecovery Preview(IReadOnlyList<Book> books)
    {
        var changes = new List<Book>(); var positions = 0; var dates = 0;
        foreach (var saved in books.Where(book => !book.Excluded && book.Goodreads != null))
        {
            var book = LibraryStore.Clone(saved);
            if (!book.ToReadPositionIsLocal && book.ToReadPosition == null && book.Status == ReadingStatus.ToRead && Position(book.Goodreads!.Fields) is { } position)
            { book.ToReadPosition = position; positions++; }
            if (!book.DateAddedIsLocal && book.DateAdded == null && Added(book.Goodreads!.Fields) is { } date)
            { book.DateAdded = date; dates++; }
            if (book.ToReadPosition != saved.ToReadPosition || book.DateAdded != saved.DateAdded) changes.Add(book);
        }
        return new(changes.ToArray(), books.ToDictionary(book => book.Id, book => book.UpdatedAt), positions, dates,
            books.Count(book => !book.Excluded && book.Status == ReadingStatus.ToRead && book.ToReadPosition == null &&
                (book.Goodreads == null || Position(book.Goodreads.Fields) == null)));
    }

    [GeneratedRegex(@"(?:^|,)\s*to-read\s*\(\s*#(\d+)\s*\)(?=\s*(?:,|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToReadPosition();
}

public sealed partial class LibraryStore
{
    public string ApplyGoodreadsOrder(GoodreadsOrderRecovery preview)
    {
        lock (gate)
        {
            var books = Books();
            if (books.Count != preview.Baseline.Count || books.Any(book => !preview.Baseline.TryGetValue(book.Id, out var at) || at != book.UpdatedAt))
                throw new InvalidOperationException("The library changed. Preview the saved Goodreads order again.");
            if (preview.Changes.Length == 0) return "";
            // Rebuild from current saved source fields; callers cannot inject unrelated edits.
            var fresh = GoodreadsOrder.Preview(books);
            if (fresh.Changes.Length == 0) return "";
            var backup = Backup("pre-goodreads-order-recovery"); PreviewBackup(backup);
            using var connection = Open(); using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE books SET document=json_set(document,'$.ToReadPosition',$rank,'$.DateAdded',$date,'$.UpdatedAt',$at) WHERE id=$id";
            var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
            var rank = command.Parameters.Add("$rank", Microsoft.Data.Sqlite.SqliteType.Integer);
            var date = command.Parameters.Add("$date", Microsoft.Data.Sqlite.SqliteType.Text);
            command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
            foreach (var book in fresh.Changes)
            {
                id.Value = book.Id.ToString(); rank.Value = (object?)book.ToReadPosition ?? DBNull.Value;
                date.Value = (object?)book.DateAdded?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value;
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("A recovery book is no longer available.");
            }
            transaction.Commit(); AutomaticBackup();
            return backup;
        }
    }
}
