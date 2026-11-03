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
            ReadingRules.Validate(book);
            books.Add(book);
        }
        return books.OrderBy(b => b.SortOrder).ThenBy(b => b.Title).ToList();
    }

    public void Save(Book book)
    {
        lock (gate)
        {
            ReadingRules.Validate(book);
            book.UpdatedAt = DateTimeOffset.Now;
            book.Title = book.Title.Trim();
            book.Shelves = book.Shelves.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO books(id,document) VALUES($id,$document) ON CONFLICT(id) DO UPDATE SET document=excluded.document";
                command.Parameters.AddWithValue("$id", book.Id.ToString());
                command.Parameters.AddWithValue("$document", JsonSerializer.Serialize(book, Json));
                command.ExecuteNonQuery();
            }
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

    public void SetSetting(string key, string value)
    {
        lock (gate)
        {
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$value", value);
                command.ExecuteNonQuery();
            }
            AutomaticBackup();
        }
    }

    public string BibliographyPath(Guid bookId) => Path.Combine(Profile, "bibliographies", bookId.ToString(), "bibliography.md");
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
            var bytes = File.ReadAllBytes(source);
            var relative = Path.Combine("covers", Hash(bytes) + extension);
            Directory.CreateDirectory(Path.Combine(Profile, "covers"));
            File.WriteAllBytes(Path.Combine(Profile, relative), bytes);
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
