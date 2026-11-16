using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record BibliographyText(string Text, string? Hash);
public sealed record BackupManifest(int Version, DateTimeOffset Created, Dictionary<string, string> Files);
public sealed record BackupPreview(string Path, DateTimeOffset Created, int Books, int Files);

public sealed class LibraryStore : IDisposable
{
    private const string ReadingSectionsKey = "reading-sections";
    private static readonly ReadingSection DailyReads = new(new Guid("452199dd-fd56-4fc0-a3d7-7008c0902c9d"), "Daily Reads");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() },
        IgnoreReadOnlyProperties = true
    };
    private readonly object gate = new();
    private readonly FileStream profileLock;
    public string Root { get; }
    public string Profile => Path.Combine(Root, "profile");
    public string BackupDirectory => Path.Combine(Root, "backups");
    private string Database => Path.Combine(Profile, "personal.sqlite");
    public string? LastBackupError { get; private set; }

    public LibraryStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        profileLock = new FileStream(Path.Combine(Root, ".profile.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Directory.CreateDirectory(Profile);
            using var connection = Open();
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            var current = Convert.ToInt32(version.ExecuteScalar());
            if (current > 1) throw new InvalidOperationException("This profile was created by a newer app. Upgrade before opening it.");
            using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS books(id TEXT PRIMARY KEY, document TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                PRAGMA user_version=1;
                """;
            create.ExecuteNonQuery();
            _ = Books();
        }
        catch { profileLock.Dispose(); throw; }
    }

    private SqliteConnection Open(string? path = null, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? Database, Pooling = false,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
    }

    public static Book Clone(Book book) => JsonSerializer.Deserialize<Book>(JsonSerializer.Serialize(book, Json), Json)!;

    public List<Book> Books()
    {
        lock (gate)
        {
            using var connection = Open();
            return ReadBooks(connection);
        }
    }

    private static List<Book> ReadBooks(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, document FROM books";
        using var reader = command.ExecuteReader();
        List<Book> books = [];
        while (reader.Read())
        {
            var book = JsonSerializer.Deserialize<Book>(reader.GetString(1), Json) ?? throw new InvalidDataException("Invalid book data.");
            if (book.Id.ToString() != reader.GetString(0)) throw new InvalidDataException("Book identity does not match stored record.");
            if (book.Goodreads is { } goodreads && GoodreadsImporter.IsOutsideScope(goodreads.Fields)) book.Excluded = true;
            ReadingRules.Validate(book);
            RestoreSourcePublishers(book);
            books.Add(book);
        }
        return books.OrderBy(b => b.SortOrder).ThenBy(b => b.Title).ToList();
    }

    private static void RestoreSourcePublishers(Book book)
    {
        if (book.Goodreads is { } goodreads && GoodreadsImporter.Field(goodreads.Fields, "Publisher") is { Length: > 0 } publisher
            && book.Editions.Find(edition => edition.Id == goodreads.EditionId) is { Publisher: null } goodreadsEdition)
        {
            var isbn = GoodreadsImporter.EditionIsbn(goodreads);
            if (isbn == null ? goodreadsEdition.Label == "Goodreads edition" :
                OpenLibraryLookup.IsbnKey(goodreadsEdition.Isbn) == OpenLibraryLookup.IsbnKey(isbn))
                goodreadsEdition.Publisher = publisher;
        }
        if (book.Calibre is { } calibre && !string.IsNullOrWhiteSpace(calibre.Metadata.Publisher)
            && book.Editions.Find(edition => edition.Id == calibre.EditionId) is { Publisher: null } calibreEdition)
            calibreEdition.Publisher = calibre.Metadata.Publisher.Trim();
    }

    public void Save(Book book) => SaveBatch([book]);

    public IReadOnlyList<ReadingSection> ReadingSections() =>
        JsonSerializer.Deserialize<ReadingSection[]>(Setting(ReadingSectionsKey, "null"), Json) ?? [DailyReads];

    public void SaveReadingSections(IEnumerable<ReadingSection> sections)
    {
        var updated = sections.Select(section => section with { Name = section.Name.Trim() }).ToArray();
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ReadingSection.UnassignedName };
        foreach (var section in updated)
        {
            if (section.Name.Length == 0 || !names.Add(section.Name))
                throw new InvalidOperationException("Give each section a unique name. “Other books” is reserved.");
            if (section.Id == Guid.Empty || !ids.Add(section.Id))
                throw new InvalidOperationException("Each section needs a unique identity.");
        }

        lock (gate)
        {
            var moved = Books().Where(book => book.ReadingSectionId is { } id && !ids.Contains(id)).ToList();
            foreach (var book in moved) book.ReadingSectionId = null;
            SaveBatch(moved, (ReadingSectionsKey, JsonSerializer.Serialize(updated, Json)));
        }
    }

    public void MoveToReadingSection(Guid bookId, Guid? sectionId)
    {
        lock (gate)
        {
            var sections = ReadingSections();
            if (sectionId.HasValue && sections.All(section => section.Id != sectionId))
                throw new InvalidOperationException("That reading section is no longer available.");
            var current = Books().Where(book => !book.Excluded && book.Status == ReadingStatus.CurrentlyReading).ToList();
            var book = current.Find(book => book.Id == bookId)
                ?? throw new InvalidOperationException("This book is no longer in Currently Reading.");
            if (book.ReadingSectionId == sectionId) return;
            book.ReadingSectionId = sectionId;
            var destination = current.Where(item => item.Id != bookId && item.ReadingSectionId == sectionId).Append(book).ToList();
            for (var i = 0; i < destination.Count; i++) destination[i].SortOrder = i;
            SaveBatch(destination);
        }
    }

    public void SaveBatch(IReadOnlyList<Book> books, (string Key, string Value)? setting = null)
    {
        lock (gate)
        {
            foreach (var book in books) ReadingRules.Validate(book);
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            WriteBooks(connection, transaction, books);
            if (setting is { } entry) WriteSetting(connection, transaction, entry.Key, entry.Value);
            transaction.Commit();
            AutomaticBackup();
        }
    }

    private static void WriteBooks(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<Book> books)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO books(id,document) VALUES($id,$document) ON CONFLICT(id) DO UPDATE SET document=excluded.document";
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var document = command.Parameters.Add("$document", SqliteType.Text);
        foreach (var book in books)
        {
            book.UpdatedAt = DateTimeOffset.Now;
            book.Title = book.Title.Trim();
            book.Shelves = book.Shelves.Select(s => s.Trim()).Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            id.Value = book.Id.ToString();
            document.Value = JsonSerializer.Serialize(book, Json);
            command.ExecuteNonQuery();
        }
    }

    public bool HasBibliographyFiles(Book book) => Directory.Exists(Path.GetDirectoryName(BibliographyPath(book.Id)));

    public void MergeBooks(Book keep, Book duplicate)
    {
        lock (gate)
        {
            if (keep.Id == duplicate.Id) throw new InvalidOperationException("Select two different books.");
            var current = Books().ToDictionary(book => book.Id);
            if (!current.TryGetValue(keep.Id, out var survivor) || survivor.UpdatedAt != keep.UpdatedAt
                || !current.TryGetValue(duplicate.Id, out var other) || other.UpdatedAt != duplicate.UpdatedAt)
                throw new InvalidOperationException("The books changed. Review the pair again.");
            if (HasBibliographyFiles(other))
                throw new InvalidOperationException("Keep the record with the bibliography. If both have saved bibliography files, review them separately.");
            var merged = BookMerge.Combine(survivor, other);
            Backup("pre-book-merge");
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            WriteBooks(connection, transaction, [merged]);
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM books WHERE id=$id";
            delete.Parameters.AddWithValue("$id", other.Id.ToString());
            delete.ExecuteNonQuery();
            transaction.Commit();
            AutomaticBackup();
        }
    }

    public string Setting(string key, string fallback)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key=$key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string ?? fallback;
        }
    }

    public void ApplyCalibre(CalibrePreview preview)
    {
        lock (gate)
        {
            var existing = Books().Where(book => book.Calibre?.LibraryId == preview.LibraryId)
                .ToDictionary(book => book.Calibre!.BookId);
            var suppressed = RemovedCalibreBooks(preview.LibraryId);
            foreach (var entry in preview.PendingBooks)
            {
                var sourceId = entry.Book.Calibre!.BookId;
                existing.TryGetValue(sourceId, out var current);
                if (suppressed.Contains(sourceId) || entry.ExpectedUpdate != current?.UpdatedAt)
                    throw new InvalidOperationException("Your library changed after the preview. Refresh again to review the current changes.");
            }
            if (preview.PendingBooks.Count > 0)
            {
                Backup("pre-calibre-import");
                foreach (var entry in preview.PendingBooks)
                    if (entry.Cover != null) StoreCover(entry.Cover, ".jpg");
            }
            SaveBatch(preview.PendingBooks.Select(entry => entry.Book).ToList(), (CalibreImporter.FolderSetting, preview.Folder));
        }
    }

    public void ApplyGoodreads(GoodreadsImportPreview preview)
    {
        lock (gate)
        {
            var books = Books();
            if (books.Count != preview.Baseline.Count || books.Any(book =>
                !preview.Baseline.TryGetValue(book.Id, out var expected) || book.UpdatedAt != expected))
                throw new InvalidOperationException("Your library changed after the preview. Preview the Goodreads CSV again.");
            if (preview.Pending.Count == 0) return;
            Backup("pre-goodreads-import");
            SaveBatch(preview.Pending, (GoodreadsImporter.ReportSetting, JsonSerializer.Serialize(preview.Report(), Json)));
        }
    }

    public void ApplyCatalogMatches(CatalogMatchPreview preview)
    {
        lock (gate)
        {
            if (preview.Pending.Count == 0) return;
            var current = Books().ToDictionary(book => book.Id, book => book.UpdatedAt);
            if (preview.Pending.Any(book => !current.TryGetValue(book.Id, out var expected) || expected != book.UpdatedAt))
                throw new InvalidOperationException("Your library changed. Preview the offline catalog matches again.");
            Backup("pre-catalog-match");
            SaveBatch(preview.Pending);
        }
    }

    public GoodreadsImportReport? LastGoodreadsImport()
    {
        var json = Setting(GoodreadsImporter.ReportSetting, "");
        return json.Length == 0 ? null : JsonSerializer.Deserialize<GoodreadsImportReport>(json, Json);
    }

    private static string RemovedCalibrePrefix(Guid libraryId) => $"calibre-removed:{libraryId:D}:";

    internal HashSet<Guid> RemovedCalibreBooks(Guid libraryId)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            var prefix = RemovedCalibrePrefix(libraryId);
            command.CommandText = "SELECT key FROM settings WHERE key LIKE $pattern";
            command.Parameters.AddWithValue("$pattern", prefix + "%");
            using var reader = command.ExecuteReader();
            var ids = new HashSet<Guid>();
            while (reader.Read())
                if (Guid.TryParse(reader.GetString(0)[prefix.Length..], out var id)) ids.Add(id);
            return ids;
        }
    }

    public BookRemovalResult RemoveBooks(IReadOnlyList<Book> confirmed, CalibreRemovalPlan? calibre = null)
    {
        lock (gate)
        {
            if (confirmed.Count == 0 || confirmed.Select(book => book.Id).Distinct().Count() != confirmed.Count)
                throw new InvalidOperationException("Select the books to remove.");
            var byId = Books().ToDictionary(book => book.Id);
            var current = confirmed.Select(book => byId.TryGetValue(book.Id, out var found) && found.UpdatedAt == book.UpdatedAt
                ? found : throw new InvalidOperationException("The selected books changed. Review the removal again.")).ToList();
            Backup("pre-book-removal");
            var (removed, warning) = calibre == null
                ? (current.Select(book => book.Id).ToArray(), (string?)null)
                : CalibreRemoval.Apply(calibre, current);
            if (removed.Length == 0) return new BookRemovalResult(0, warning);
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM books WHERE id = $id";
            var id = delete.Parameters.Add("$id", SqliteType.Text);
            foreach (var bookId in removed)
            {
                id.Value = bookId.ToString();
                delete.ExecuteNonQuery();
                if (byId[bookId].Calibre is { } source)
                    WriteSetting(connection, transaction, RemovedCalibrePrefix(source.LibraryId) + source.BookId.ToString("D"), "true");
            }
            transaction.Commit();
            foreach (var bookId in removed)
            {
                var bibliography = SafePath(Profile, Path.Combine("bibliographies", bookId.ToString()));
                if (!Directory.Exists(bibliography)) continue;
                try
                {
                    var destination = SafePath(Root, Path.Combine("removed-bibliographies", bookId.ToString(), Guid.NewGuid().ToString("N")));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Directory.Move(bibliography, destination);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warning = string.Join("\n\n", new[] { warning, $"The bibliography files for “{byId[bookId].Title}” could not be archived." }
                        .Where(value => !string.IsNullOrEmpty(value)));
                }
            }
            AutomaticBackup();
            return new BookRemovalResult(removed.Length, warning);
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (gate)
        {
            using var connection = Open();
            WriteSetting(connection, null, key, value);
            AutomaticBackup();
        }
    }

    private static void WriteSetting(SqliteConnection connection, SqliteTransaction? transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public string BibliographyPath(Guid bookId) => Path.Combine(Profile, "bibliographies", bookId.ToString(), "bibliography.md");
    private string EpubBibliographyPath(Guid bookId) => Path.Combine(Path.GetDirectoryName(BibliographyPath(bookId))!, "epub-bibliography.json");
    public bool HasEpubBibliography(Guid bookId) => File.Exists(EpubBibliographyPath(bookId));

    public EpubBibliography? ReadEpubBibliography(Guid bookId)
    {
        lock (gate)
        {
            var path = EpubBibliographyPath(bookId);
            return File.Exists(path) ? JsonSerializer.Deserialize<EpubBibliography>(File.ReadAllText(path), Json) : null;
        }
    }

    public void SaveEpubBibliography(Guid bookId, EpubBibliography bibliography)
    {
        lock (gate)
        {
            if (!Books().Any(book => book.Id == bookId))
                throw new InvalidOperationException("Save the book before attaching an extracted bibliography.");
            var path = EpubBibliographyPath(bookId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(bibliography, Json));
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            AutomaticBackup();
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public BibliographyText Bibliography(Guid bookId)
    {
        lock (gate)
        {
            var path = BibliographyPath(bookId);
            if (!File.Exists(path)) return new("", null);
            var bytes = File.ReadAllBytes(path);
            return new(Encoding.UTF8.GetString(bytes), Hash(bytes));
        }
    }

    public BibliographyText SaveBibliography(Guid bookId, string text, string? expectedHash)
    {
        lock (gate)
        {
            if (Books().All(b => b.Id != bookId)) throw new InvalidOperationException("Save the book before adding a bibliography.");
            var previous = Bibliography(bookId);
            if (previous.Hash != expectedHash)
                throw new InvalidOperationException("The Markdown file changed outside this editor. Copy your draft, close, and reopen the book to reload it. The external file has been preserved.");
            var path = BibliographyPath(bookId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var revisions = Path.Combine(Path.GetDirectoryName(path)!, "revisions");
            Directory.CreateDirectory(revisions);
            var revision = Path.Combine(revisions, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.md");
            if (File.Exists(path)) File.Copy(path, revision);
            else File.WriteAllText(Path.Combine(revisions, "original-paste.md"), text, new UTF8Encoding(false));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, true);
            AutomaticBackup();
            return Bibliography(bookId);
        }
    }

    public string ImportCover(string source)
    {
        lock (gate)
        {
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(extension))
                throw new InvalidOperationException("Choose a PNG, JPEG, or BMP image.");
            return StoreCover(File.ReadAllBytes(source), extension);
        }
    }

    public string ImportJpegCover(byte[] bytes) => StoreCover(bytes, ".jpg");

    internal static string CoverName(byte[] bytes, string extension) => Path.Combine("covers", Hash(bytes) + extension);

    internal string StoreCover(byte[] bytes, string extension)
    {
        lock (gate)
        {
            var relative = CoverName(bytes, extension);
            Directory.CreateDirectory(Path.Combine(Profile, "covers"));
            var destination = Path.Combine(Profile, relative);
            if (!File.Exists(destination))
            {
                var temporary = destination + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, destination);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return relative;
        }
    }

    public string? CoverPath(Edition edition)
    {
        if (string.IsNullOrEmpty(edition.CoverFile)) return null;
        var path = SafePath(Profile, edition.CoverFile);
        return File.Exists(path) ? path : null;
    }

    private static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive or profile contains an unsafe path.");
        return full;
    }

    private void AutomaticBackup()
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var today = DateTime.Today.ToString("yyyyMMdd");
            if (!Directory.EnumerateFiles(BackupDirectory, $"daily-{today}-*.zip").Any())
                Backup($"daily-{today}");
            var week = System.Globalization.ISOWeek.GetYear(DateTime.Today) + "-" + System.Globalization.ISOWeek.GetWeekOfYear(DateTime.Today).ToString("00");
            if (!Directory.EnumerateFiles(BackupDirectory, $"weekly-{week}-*.zip").Any()) Backup("weekly-" + week);
            foreach (var (pattern, retain) in new[] { ("daily-*.zip", 14), ("weekly-*.zip", 4) })
                foreach (var old in Directory.EnumerateFiles(BackupDirectory, pattern).OrderDescending().Skip(retain)) File.Delete(old);
            LastBackupError = null;
        }
        catch (Exception ex) { LastBackupError = "Your changes were saved, but automatic backup failed: " + ex.Message; }
    }

    public string Backup(string prefix = "manual")
    {
        lock (gate)
        {
            Directory.CreateDirectory(BackupDirectory);
            var staging = Path.Combine(Root, ".backup-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(BackupDirectory, $"{prefix}-{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}.zip");
            try
            {
                Directory.CreateDirectory(staging);
                using (var source = Open())
                using (var target = Open(Path.Combine(staging, "personal.sqlite"))) source.BackupDatabase(target);
                foreach (var file in Directory.EnumerateFiles(Profile, "*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked profile files cannot be backed up.");
                    var relative = Path.GetRelativePath(Profile, file);
                    if (relative == "personal.sqlite" || relative.EndsWith(".tmp")) continue;
                    var destination = SafePath(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
                var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                    .ToDictionary(f => Path.GetRelativePath(staging, f).Replace('\\', '/'), f => Hash(File.ReadAllBytes(f)));
                File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(new BackupManifest(1, DateTimeOffset.Now, files), Json));
                ZipFile.CreateFromDirectory(staging, path + ".tmp", CompressionLevel.Optimal, false);
                File.Move(path + ".tmp", path);
                return path;
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
    }

    private BackupPreview ExtractAndValidate(string archivePath, string staging)
    {
        Directory.CreateDirectory(staging);
        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup manifest is missing.");
        using var input = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(input, Json) ?? throw new InvalidDataException("Invalid manifest.");
        if (manifest.Version != 1 || !manifest.Files.ContainsKey("personal.sqlite")) throw new InvalidDataException("Unsupported or incomplete backup.");
        if (archive.Entries.Count != manifest.Files.Count + 1 || archive.Entries.Select(e => e.FullName).Distinct().Count() != archive.Entries.Count)
            throw new InvalidDataException("Backup has unexpected or duplicate entries.");
        foreach (var (relative, hash) in manifest.Files)
        {
            var target = SafePath(staging, relative);
            var entry = archive.GetEntry(relative) ?? throw new InvalidDataException("Missing file: " + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
            if (Hash(File.ReadAllBytes(target)) != hash) throw new InvalidDataException("Checksum mismatch: " + relative);
        }
        using var connection = Open(Path.Combine(staging, "personal.sqlite"), true);
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA quick_check";
        if (check.ExecuteScalar() as string != "ok") throw new InvalidDataException("Backup database integrity check failed.");
        check.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(check.ExecuteScalar()) != 1) throw new InvalidDataException("Unsupported database version.");
        var books = ReadBooks(connection);
        return new BackupPreview(archivePath, manifest.Created, books.Count, manifest.Files.Count);
    }

    public BackupPreview PreviewBackup(string archivePath)
    {
        lock (gate)
        {
            var staging = Path.Combine(Root, ".preview-" + Guid.NewGuid().ToString("N"));
            try { return ExtractAndValidate(archivePath, staging); }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
    }

    public void Restore(string archivePath)
    {
        lock (gate)
        {
            var staging = Path.Combine(Root, ".restore-" + Guid.NewGuid().ToString("N"));
            var rollback = Path.Combine(Root, ".rollback-" + Guid.NewGuid().ToString("N"));
            try
            {
                _ = ExtractAndValidate(archivePath, staging);
                Backup("pre-restore");
                Directory.Move(Profile, rollback);
                try { Directory.Move(staging, Profile); }
                catch { Directory.Move(rollback, Profile); throw; }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                // Rollback is retained if replacement succeeds; the pre-restore ZIP is the durable recovery path.
                if (Directory.Exists(rollback) && Directory.Exists(Profile)) Directory.Delete(rollback, true);
            }
        }
    }

    public void Dispose() => profileLock.Dispose();
}
