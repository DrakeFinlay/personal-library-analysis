using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record GoodreadsCandidate(Guid Id, string Label);

public sealed record GoodreadsImportRow(int Number, string SourceId, string Label, string Action,
    string Details, IReadOnlyList<GoodreadsCandidate> Candidates)
{
    public string Display => $"{Action} · {Label}";
}

public sealed record GoodreadsImportReport(string FileName, DateTimeOffset ImportedAt, int Added, int Updated,
    int Unchanged, int Skipped, GoodreadsImportRow[] Rows, Dictionary<string, string>[] SkippedSourceFields);

public sealed class GoodreadsImportPreview
{
    private readonly GoodreadsImporter.Source[] sources;
    private readonly Book[] books;
    private readonly HashSet<string> previouslyRemoved;
    private readonly Dictionary<int, Guid> decisions = [];
    internal readonly Dictionary<Guid, DateTimeOffset> Baseline;
    internal readonly List<Book> Pending = [];

    internal GoodreadsImportPreview(string fileName, GoodreadsImporter.Source[] sources, Book[] books, HashSet<string> previouslyRemoved)
    {
        FileName = fileName;
        this.sources = sources;
        this.books = books;
        this.previouslyRemoved = previouslyRemoved;
        Baseline = books.ToDictionary(book => book.Id, book => book.UpdatedAt);
        Rebuild();
    }

    public string FileName { get; }
    public IReadOnlyList<GoodreadsImportRow> Rows { get; private set; } = [];
    public int Added => Rows.Count(row => row.Action == "Add");
    public int Updated => Rows.Count(row => row.Action == "Update");
    public int Unchanged => Rows.Count(row => row.Action == "Unchanged");
    public int Skipped => Rows.Count - Added - Updated - Unchanged;
    public int Excluded => Rows.Count(row => row.Action == "Out of scope");
    public int LegacyUndated => Pending.Count(book => GoodreadsImporter.IsLegacy(book.Goodreads!.Fields));

    public bool HasDecision(int number) => decisions.ContainsKey(number);

    public void ClearDecision(int number)
    {
        decisions.Remove(number);
        Rebuild();
    }

    public void Resolve(int number, Guid bookId)
    {
        var row = Rows.Single(item => item.Number == number);
        if (row.Action != "Review") throw new InvalidOperationException("This row does not need a match decision.");
        if (bookId != Guid.Empty && row.Candidates.All(candidate => candidate.Id != bookId))
            throw new InvalidOperationException("Choose one of the suggested books or add a separate book.");
        decisions[number] = bookId;
        Rebuild();
    }

    private void Rebuild()
    {
        Pending.Clear();
        var reserved = new HashSet<Guid>();
        var byId = books.Where(book => book.Goodreads != null).ToDictionary(book => book.Goodreads!.BookId, StringComparer.Ordinal);
        var candidates = sources.ToDictionary(source => source.Number, source => GoodreadsImporter.Candidates(source, books));
        var claims = sources.Where(source => !previouslyRemoved.Contains(source.Id) && !GoodreadsImporter.IsOutsideScope(source.Fields) &&
                !byId.ContainsKey(source.Id) && !decisions.ContainsKey(source.Number))
            .Select(source => candidates[source.Number].Automatic).OfType<Book>()
            .GroupBy(book => book.Id).ToDictionary(group => group.Key, group => group.Count());
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var possibleDuplicates = new HashSet<int>();
        foreach (var source in sources.Where(source => source.Error == null && !previouslyRemoved.Contains(source.Id) &&
            !GoodreadsImporter.IsOutsideScope(source.Fields)).OrderBy(source => !byId.ContainsKey(source.Id)))
        {
            var repeated = false;
            foreach (var key in GoodreadsImporter.DuplicateKeys(source)) repeated |= !sourceKeys.Add(key);
            if (repeated) possibleDuplicates.Add(source.Number);
        }
        var rows = new List<GoodreadsImportRow>();
        // Explicit matches take precedence over automatic matches from other rows.
        foreach (var source in sources.OrderBy(source => !byId.ContainsKey(source.Id)).ThenBy(source => !decisions.ContainsKey(source.Number)))
        {
            var label = $"{source.Title} — {source.Author}";
            if (source.Error != null)
            {
                rows.Add(new(source.Number, source.Id, label, "Skip", source.Error, []));
                continue;
            }
            if (GoodreadsImporter.IsOutsideScope(source.Fields))
            {
                rows.Add(new(source.Number, source.Id, label, "Out of scope", "Manga or comics. This row is skipped and retained in the import report.", []));
                continue;
            }
            if (previouslyRemoved.Contains(source.Id))
            {
                rows.Add(new(source.Number, source.Id, label, "Skip", "Previously removed from Personal Library.", []));
                continue;
            }
            var (automatic, possible, reason) = candidates[source.Number];
            byId.TryGetValue(source.Id, out var current);
            if (current != null) reason = "Goodreads ID";
            else if (decisions.TryGetValue(source.Number, out var target))
            {
                current = books.FirstOrDefault(book => book.Id == target);
                reason = current == null ? "Separate book chosen in preview" : "Match chosen in preview";
            }
            else if (automatic != null && claims[automatic.Id] == 1 && !reserved.Contains(automatic.Id)) current = automatic;
            else if (possible.Length > 0)
            {
                var available = possible.Where(book => book.Goodreads == null && !reserved.Contains(book.Id))
                    .Select(book => new GoodreadsCandidate(book.Id, $"{book.Title} — {book.Author} · {ReadingRules.Label(book.Status)}{(book.Calibre == null ? "" : " · Calibre")}")).ToArray();
                rows.Add(new(source.Number, source.Id, label, "Review", "Match needs confirmation. This row will be skipped unless you choose a match or add it separately.", available));
                continue;
            }
            else if (possibleDuplicates.Contains(source.Number))
            {
                rows.Add(new(source.Number, source.Id, label, "Review", "Another CSV row has the same ISBN or title and author. Add this row separately or leave it skipped.", []));
                continue;
            }
            if (current != null && !reserved.Add(current.Id))
            {
                rows.Add(new(source.Number, source.Id, label, "Review", "Another export row has been matched to this book. Add this row separately or leave it skipped.", []));
                continue;
            }
            var book = GoodreadsImporter.Merge(source, current);
            var action = current == null ? "Add" : JsonSerializer.Serialize(book, LibraryStore.Json) ==
                JsonSerializer.Serialize(current, LibraryStore.Json) ? "Unchanged" : "Update";
            if (action != "Unchanged") Pending.Add(book);
            var details = $"{reason ?? "New book"} · {ReadingRules.Label(book.Status)}";
            if (current != null && current.Status != book.Status) details += $" (was {ReadingRules.Label(current.Status)})";
            if (book.Excluded) details += " · Excluded";
            if (GoodreadsImporter.IsLegacy(source.Fields)) details += " · Pre-Goodreads completion mapped to undated";
            else if (source.Finished is { } finished) details += $" · Goodreads date read {finished:yyyy-MM-dd}";
            if (current?.Rating != book.Rating) details += $"\nRating: {current?.Rating?.ToString() ?? "Unrated"} → {book.Rating?.ToString() ?? "Unrated"}";
            if (current != null && current.Review != book.Review)
                details += book.Review.Length == 0 ? "\nGoodreads clears the existing review." : "\nThe Goodreads review replaces the existing review.";
            if (book.Goodreads!.Shelves.Length > 0) details += "\nGoodreads shelves: " + string.Join(", ", book.Goodreads.Shelves);
            if (current != null && book.Editions.Count > current.Editions.Count) details += "\nAdds a Goodreads edition alongside the existing edition.";
            if (source.Warning.Length > 0) details += Environment.NewLine + source.Warning;
            rows.Add(new(source.Number, source.Id, label, action, details, []));
        }
        Rows = rows.OrderBy(row => row.Number).ToArray();
    }

    internal GoodreadsImportReport Report() => new(FileName, DateTimeOffset.Now, Added, Updated, Unchanged, Skipped, Rows.ToArray(),
        sources.Where(source => Rows.Any(row => row.Number == source.Number && row.Action is "Skip" or "Review" or "Out of scope"))
            .Select(source => source.Fields).ToArray());
}

public static class GoodreadsImporter
{
    public const string ReportSetting = "goodreads-last-import";
    private const string LegacyShelf = "read-b4-goodreads";
    private static readonly HashSet<string> StatusShelves = new(["read", "to-read", "currently-reading"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Comics = new(["manga", "comic", "comics", "graphic novel", "graphic novels",
        "manhwa", "manhua", "webtoon", "webtoons", "manga comics", "comics manga", "漫画", "マンガ"], StringComparer.Ordinal);

    internal static bool IsOutsideScope(Dictionary<string, string> fields)
    {
        var title = Field(fields, "Title");
        if (IsComicTitle(title)) return true;
        var categories = new[] { "Bookshelves", "Exclusive Shelf", "Genre", "Genres", "Binding" }
            .SelectMany(field => Field(fields, field).Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries));
        return categories.Any(category =>
        {
            var key = Normalize(category);
            return Comics.Contains(key) || new[] { "read", "to read", "currently reading", "dnf" }
                .Any(status => (key.StartsWith(status + " ", StringComparison.Ordinal) && Comics.Contains(key[(status.Length + 1)..]))
                    || (key.EndsWith(" " + status, StringComparison.Ordinal) && Comics.Contains(key[..^(status.Length + 1)])));
        });
    }

    internal static bool IsComicTitle(string title) =>
        System.Text.RegularExpressions.Regex.IsMatch(title,
            @"\((?:manga|manhwa|manhua|comics?|graphic novel)\)|\[(?:manga|manhwa|manhua|comics?|graphic novel)\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal sealed record Source(int Number, string Id, string Title, string Author, string Isbn, ReadingStatus? Status,
        DateOnly? Finished, DateOnly? Added, int? ReadCount, string[] Shelves, Dictionary<string, string> Fields,
        string? Error, string Warning, GoodreadsArchive.Session[]? Sessions = null);

    public static GoodreadsImportPreview Preview(LibraryStore store, string path, string? archivePath = null)
    {
        var sources = ReadSources(path);
        if (!string.IsNullOrWhiteSpace(archivePath)) AttachReadingSessions(sources, archivePath);
        return new GoodreadsImportPreview(Path.GetFileName(path), sources.ToArray(), store.Books().ToArray(), store.RemovedGoodreadsBooks());
    }

    internal static List<Source> ReadSources(string path)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()?.Select(header => header.Trim()).ToArray();
        if (headers == null || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length ||
            !new[] { "Book Id", "Title", "Author", "Exclusive Shelf" }.All(required => headers.Contains(required, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Choose a Goodreads CSV with Book Id, Title, Author, and Exclusive Shelf columns.");
        var sources = new List<Source>();
        try
        {
            while (!parser.EndOfData)
            {
                var line = checked((int)parser.LineNumber);
                var cells = parser.ReadFields()!;
                if (cells.Length != headers.Length) throw new InvalidDataException($"CSV row {line} has the wrong number of columns.");
                var fields = headers.Zip(cells).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase);
                sources.Add(Parse(line, fields));
            }
        }
        catch (MalformedLineException ex) { throw new InvalidDataException($"The Goodreads CSV has malformed quoting near line {ex.LineNumber}.", ex); }
        if (sources.Count == 0) throw new InvalidDataException("The Goodreads CSV contains no books.");
        var duplicates = sources.Where(source => source.Error == null).GroupBy(source => source.Id)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        return sources.Select(source => duplicates.Contains(source.Id)
            ? source with { Error = "Duplicate Goodreads ID in this CSV. Correct the duplicate rows and preview again." }
            : source).ToList();
    }

    private static void AttachReadingSessions(List<Source> sources, string archivePath)
    {
        var archive = GoodreadsArchive.ReadSessions(archivePath);
        var byTitle = sources.ToLookup(source => source.Title, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (!archive.TryGetValue(source.Title, out var sessions) || IsLegacy(source.Fields)) continue;
            var sameTitle = byTitle[source.Title];
            if (sameTitle.Count() > 1)
            {
                // The archive stores titles without edition IDs. An exact, unique CSV finish can disambiguate a session.
                sessions = source.Finished is { } date && sameTitle.Count(row => row.Finished == date) == 1
                    ? sessions.Where(session => session.Completed && session.Finished == date).ToArray() : [];
                if (sessions.Length == 0)
                {
                    sources[index] = source with { Warning = (source.Warning + " Archive sessions were skipped because this title appears more than once in the CSV.").Trim() };
                    continue;
                }
            }
            var recentFinish = sessions.Where(session => session.Completed).MaxBy(session => session.Finished)?.Finished;
            sources[index] = source with
            {
                Sessions = sessions,
                Finished = source.Finished ?? recentFinish,
                Warning = (source.Warning + $" Found {sessions.Length} archive reading session(s) for this title.").Trim()
            };
        }
    }

    private static Source Parse(int line, Dictionary<string, string> fields)
    {
        var id = Field(fields, "Book Id");
        var title = Field(fields, "Title");
        var author = Field(fields, "Author");
        var shelves = Field(fields, "Bookshelves").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exclusive = Field(fields, "Exclusive Shelf").ToLowerInvariant();
        var tags = shelves.Append(exclusive).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var status = IsLegacy(fields) ? ReadingStatus.Read
            : tags.Contains("did-not-finish") ? ReadingStatus.DidNotFinish
            : tags.Contains("partially-read") ? ReadingStatus.PartiallyRead
            : tags.Contains("reference-only") ? ReadingStatus.ReferenceOnly
            : exclusive switch
            {
                "read" => ReadingStatus.Read,
                "to-read" or "to-read-in-japanese" => ReadingStatus.ToRead,
                "currently-reading" => ReadingStatus.CurrentlyReading,
                _ => (ReadingStatus?)null
            };
        var warnings = new List<string>();
        if (status == null) warnings.Add($"Unmapped exclusive shelf '{exclusive}'. Existing status is kept.");
        var finished = Date(fields, "Date Read", warnings);
        var added = Date(fields, "Date Added", warnings);
        var count = Number(fields, "Read Count");
        if (count > 1) warnings.Add($"Read Count is {count}. The export's count alone cannot identify separate rereads.");
        var rawIsbn13 = UnwrapIsbn(Field(fields, "ISBN13"));
        var rawIsbn10 = UnwrapIsbn(Field(fields, "ISBN"));
        var isbn13 = OpenLibraryLookup.NormalizeIsbn(rawIsbn13);
        var isbn10 = OpenLibraryLookup.NormalizeIsbn(rawIsbn10);
        if (isbn13 == null && isbn10 == null && (rawIsbn13.Length > 0 || rawIsbn10.Length > 0))
            warnings.Add("The export ISBN could not be validated and will not be used to match an edition.");
        var error = title.Length == 0 || !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0
            ? $"Row {line} has a missing title or invalid Goodreads ID." : null;
        return new(line, id, title, author, isbn13 ?? isbn10 ?? "", status, finished, added, count, shelves, fields, error, string.Join(" ", warnings));
    }

    internal static (Book? Automatic, Book[] Possible, string? Reason) Candidates(Source source, Book[] books)
    {
        if (source.Error != null || IsOutsideScope(source.Fields)) return (null, [], null);
        var isbn = IsbnKey(source.Isbn);
        var title = Normalize(source.Title);
        var author = Normalize(source.Author);
        var eligible = books.Where(book => book.Goodreads == null && !book.Excluded).ToArray();
        var byIsbn = isbn.Length == 0 ? [] : eligible.Where(book => !BookIdentity.DifferentVolumes(book.Title, source.Title) &&
            book.Editions.Any(edition => IsbnKey(edition.Isbn) == isbn)).ToArray();
        if (byIsbn.Length > 0) return (byIsbn.Length == 1 ? byIsbn[0] : null, byIsbn, "ISBN");
        bool SameAuthor(Book book) => author.Length > 0 && author != "unknown" && author == Normalize(book.Author);
        var exact = eligible.Where(book => title.Length > 0 && title == Normalize(book.Title) && SameAuthor(book)).ToArray();
        if (exact.Length > 0) return (exact.Length == 1 ? exact[0] : null, exact, "Exact title and author");
        var possible = books.Where(book => !book.Excluded && !BookIdentity.DifferentVolumes(book.Title, source.Title)
            && (title == Normalize(book.Title) ||
            (SameAuthor(book) && Normalize(BookIdentity.MainTitle(book.Title)) == Normalize(BookIdentity.MainTitle(source.Title)))))
            .ToArray();
        // Already linked records are evidence of a possible duplicate, but cannot take a second source ID.
        return (null, possible, null);
    }

    internal static IEnumerable<string> DuplicateKeys(Source source)
    {
        if (IsbnKey(source.Isbn) is { Length: > 0 } isbn) yield return "ISBN:" + isbn;
        var title = Normalize(source.Title);
        var author = Normalize(source.Author);
        if (title.Length > 0 && author.Length > 0 && author != "unknown") yield return $"{title}|{author}";
    }

    internal static Book Merge(Source source, Book? current)
    {
        var book = current == null ? new Book() : LibraryStore.Clone(current);
        var prior = book.Goodreads;
        book.Title = source.Title;
        if (source.Author.Length > 0 && !source.Author.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) book.Author = source.Author;
        book.OriginalYear = Year(source.Fields, "Original Publication Year") ?? book.OriginalYear;
        if (CsvRating(source.Fields) is { } stars) book.Rating = stars;
        if (source.Fields.TryGetValue("My Review", out var review)) book.Review = review;
        var importedShelves = source.Shelves.Append(Field(source.Fields, "Exclusive Shelf"))
            .Where(shelf => shelf.Length > 0 && !StatusShelves.Contains(shelf) && !shelf.Equals(LegacyShelf, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        book.Shelves = book.Shelves.Where(shelf => !shelf.Equals(LegacyShelf, StringComparison.OrdinalIgnoreCase) &&
                !(prior?.Shelves.Contains(shelf, StringComparer.OrdinalIgnoreCase) ?? false))
            .Concat(importedShelves).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var edition = MatchEdition(book, current == null, source);
        var status = source.Status ?? (current == null ? ReadingStatus.Unclassified : book.Status);
        if (current != null && ReadingRules.IsSpecialCollection(current.Status)) status = current.Status;
        if (status == ReadingStatus.ToRead && book.RecommendationDismissed)
            status = book.Status == ReadingStatus.ToRead ? ReadingStatus.Unclassified : book.Status;
        var completion = ApplyReading(book, edition, source, status);
        if (status is ReadingStatus.Read or ReadingStatus.ToRead or ReadingStatus.CurrentlyReading)
            ApplyArchiveSessions(book, edition, source.Sessions ?? [], status == ReadingStatus.Read);
        book.Status = status;
        if (status != ReadingStatus.ToRead) book.ReadNext = false;
        else if (source.Added is { } added) book.ToReadAddedAt = new DateTimeOffset(added.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        else if (current == null) book.ToReadAddedAt = DateTimeOffset.MinValue;
        book.Goodreads = new GoodreadsReference(source.Id, source.Fields, importedShelves, edition.Id, completion);
        return book;
    }

    internal static void ApplyArchiveSessions(Book book, Edition edition, GoodreadsArchive.Session[] sessions, bool includeImprecise)
    {
        var completed = sessions.Where(session => session.Completed).ToArray();
        var matched = new HashSet<Guid>();
        foreach (var session in completed.Where(session => session.Finished.HasValue).OrderBy(session => session.Finished))
        {
            var reading = book.Readings.FirstOrDefault(item =>
                item.Outcome == ReadingOutcome.Completed && !matched.Contains(item.Id) && item.Finished == session.Finished &&
                (item.Started == null || session.Started == null || item.Started == session.Started));
            if (reading == null)
            {
                reading = new Reading { EditionId = edition.Id, Outcome = ReadingOutcome.Completed, Finished = session.Finished };
                book.Readings.Add(reading);
            }
            reading.Started ??= session.Started;
            matched.Add(reading.Id);
        }
        if (includeImprecise)
        {
            foreach (var session in completed.Where(session => session.Finished == null)
                .OrderByDescending(session => session.FinishYear))
            {
                // A sole undated session does not establish a second reading beyond the CSV completion.
                if (session.FinishYear == null && completed.Length < 2) continue;
                var available = book.Readings.Where(reading => reading.Outcome == ReadingOutcome.Completed && !matched.Contains(reading.Id));
                var reading = available.FirstOrDefault(item => item.CompletionYear == session.FinishYear &&
                    (item.Started == null || session.Started == null || item.Started == session.Started));
                if (reading == null && session.FinishYear.HasValue)
                    reading = available.FirstOrDefault(item => item.CompletionYear == null && item.Started == null && item.Progress.Count == 0);
                if (reading == null)
                {
                    reading = new Reading { EditionId = edition.Id, Outcome = ReadingOutcome.Completed };
                    book.Readings.Add(reading);
                }
                if (reading.Finished == null) reading.ApproximateFinishYear = session.FinishYear;
                if (reading.Finished == null || session.Started <= reading.Finished) reading.Started ??= session.Started;
                matched.Add(reading.Id);
            }
        }
        if (book.ActiveReading is { Started: null } active)
            active.Started = sessions.Where(session => !session.Completed).MaxBy(session => session.Started)?.Started;
    }

    private static Edition MatchEdition(Book book, bool isNew, Source source)
    {
        var prior = book.Editions.FirstOrDefault(edition => edition.Id == book.Goodreads?.EditionId);
        var isbn = IsbnKey(source.Isbn);
        var same = isbn.Length == 0 ? null : book.Editions.FirstOrDefault(edition => IsbnKey(edition.Isbn) == isbn);
        var selected = book.SelectedEdition;
        var edition = same ?? (prior != null && (isbn.Length == 0 || IsbnKey(prior.Isbn) == isbn) ? prior : null);
        if (edition == null && (isNew || isbn.Length == 0)) edition = selected;
        // A title and author match identifies a work, not the Calibre edition.
        if (edition == null)
        {
            edition = new Edition { Label = "Goodreads edition", Language = "" };
            book.Editions.Add(edition);
        }
        if (isNew) { edition.Label = "Goodreads edition"; edition.Language = ""; book.SelectedEditionId = edition.Id; }
        if (source.Isbn.Length > 0 && IsbnKey(edition.Isbn).Length == 0) edition.Isbn = source.Isbn;
        if (edition == same || edition != selected || isNew || edition == prior && edition.Label == "Goodreads edition")
        {
            if (Field(source.Fields, "Publisher") is { Length: > 0 } publisher) edition.Publisher ??= publisher;
            edition.PublicationYear = Year(source.Fields, "Year Published") ?? edition.PublicationYear;
            edition.PageCount = Number(source.Fields, "Number of Pages") is > 0 and var pages ? pages : edition.PageCount;
        }
        return edition;
    }

    private static Guid? ApplyReading(Book book, Edition edition, Source source, ReadingStatus status)
    {
        var active = book.ActiveReading;
        var legacy = IsLegacy(source.Fields);
        var history = book.Readings.Where(reading => reading.Outcome == ReadingOutcome.Completed).ToArray();
        var linked = history.FirstOrDefault(reading => reading.Id == book.Goodreads?.CompletionId);
        var date = legacy ? null : source.Finished;
        var completion = linked;
        if (status == ReadingStatus.Read || (status is ReadingStatus.ToRead or ReadingStatus.CurrentlyReading && date != null))
        {
            var exact = date != null || legacy && source.Finished != null
                ? history.FirstOrDefault(reading => reading.Finished == (date ?? source.Finished)) : null;
            var placeholder = history.FirstOrDefault(reading => reading.Started == null && reading.CompletionYear == null && reading.Progress.Count == 0);
            var reread = source.ReadCount is { } count && book.Goodreads is { } previous &&
                Number(previous.Fields, "Read Count") is { } priorCount && count > priorCount && date != null && date != linked?.Finished;
            completion = exact ?? (reread ? null : linked ?? placeholder)
                ?? (legacy && history.Length == 1 ? history[0] : null)
                ?? (book.Goodreads == null && history.Length == 1 && source.ReadCount is null or <= 1 ? history[0] : null)
                ?? (date == null && !legacy ? history.LastOrDefault() : null);
            if (completion == null)
            {
                if (status == ReadingStatus.Read && active != null && !legacy &&
                    (date == null || active.Started == null || active.Started <= date))
                    completion = active;
                else
                {
                    completion = new Reading { EditionId = edition.Id };
                    book.Readings.Add(completion);
                }
                completion.Outcome = ReadingOutcome.Completed;
            }
            if (legacy) { completion.Started = null; completion.Finished = null; completion.ApproximateFinishYear = null; }
            else if (date != null || completion.Id == linked?.Id && book.Goodreads is { } previousSource &&
                Field(previousSource.Fields, "Date Read").Length > 0)
            {
                completion.Finished = date;
                if (date != null) completion.ApproximateFinishYear = null;
                if (completion.Started > date) completion.Started = null;
            }
        }
        if (status == ReadingStatus.CurrentlyReading && book.ActiveReading == null)
            book.Readings.Add(new Reading { EditionId = edition.Id, Outcome = ReadingOutcome.InProgress });
        else if (status != ReadingStatus.CurrentlyReading && book.ActiveReading is { } superseded)
        {
            superseded.Outcome = status switch
            {
                ReadingStatus.DidNotFinish => ReadingOutcome.DidNotFinish,
                ReadingStatus.PartiallyRead => ReadingOutcome.PartiallyRead,
                _ => ReadingOutcome.Superseded
            };
        }
        var partial = status == ReadingStatus.PartiallyRead ? ReadingOutcome.PartiallyRead : ReadingOutcome.DidNotFinish;
        if (status is ReadingStatus.PartiallyRead or ReadingStatus.DidNotFinish && book.Readings.All(reading => reading.Outcome != partial))
            book.Readings.Add(new Reading { EditionId = edition.Id, Outcome = partial });
        return completion?.Id;
    }

    internal static bool IsLegacy(Dictionary<string, string> fields) =>
        Field(fields, "Exclusive Shelf").Equals(LegacyShelf, StringComparison.OrdinalIgnoreCase) ||
        Field(fields, "Bookshelves").Split(',', StringSplitOptions.TrimEntries).Contains(LegacyShelf, StringComparer.OrdinalIgnoreCase);

    internal static bool HasEditionIsbn(GoodreadsReference source) =>
        EditionIsbn(source) != null;

    internal static string? EditionIsbn(GoodreadsReference source) =>
        OpenLibraryLookup.NormalizeIsbn(UnwrapIsbn(Field(source.Fields, "ISBN13"))) ??
        OpenLibraryLookup.NormalizeIsbn(UnwrapIsbn(Field(source.Fields, "ISBN")));

    internal static string Field(Dictionary<string, string> fields, string key)
    {
        // The JSON round trip uses the default dictionary comparer.
        if (fields.TryGetValue(key, out var value)) return value.Trim();
        return fields.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.Trim() ?? "";
    }

    private static int? Number(Dictionary<string, string> fields, string key) =>
        int.TryParse(Field(fields, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    internal static int? CsvRating(Dictionary<string, string> fields) =>
        Number(fields, "My Rating") is >= 1 and <= 5 and var stars ? stars : null;
    internal static int? Year(Dictionary<string, string> fields, string key) => Number(fields, key) is not 0 and var year ? year : null;

    private static DateOnly? Date(Dictionary<string, string> fields, string key, List<string> warnings)
    {
        var value = Field(fields, key);
        if (value.Length == 0) return null;
        if (DateOnly.TryParseExact(value, ["yyyy/M/d", "yyyy-M-d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        warnings.Add($"{key} is invalid and will be left undated.");
        return null;
    }

    private static string UnwrapIsbn(string value) => value.StartsWith("=\"") && value.EndsWith('"') ? value[2..^1] : value;

    internal static string IsbnKey(string value) => OpenLibraryLookup.IsbnKey(value);

    internal static string Normalize(string value) => string.Join(' ', string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
        .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' '))
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
