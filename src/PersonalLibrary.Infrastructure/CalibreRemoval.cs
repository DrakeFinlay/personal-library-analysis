using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record CalibreRemovalTarget(Guid PersonalBookId, Guid SourceBookId, long CalibreId,
    string Title, string Directory, string[] Formats);

public sealed record CalibreRemovalPlan(string Folder, Guid LibraryId, CalibreRemovalTarget[] Targets);

public sealed record BookRemovalResult(int Removed, string? Warning);

public static class CalibreRemoval
{
    private static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Calibre2", "calibredb.exe");

    public static bool CanPlan(IReadOnlyList<Book> books) =>
        books.Count > 0 && books[0].Calibre is { } first && books.All(book => book.Calibre is { } source
            && source.LibraryId == first.LibraryId
            && string.Equals(source.LibraryFolder, first.LibraryFolder, StringComparison.OrdinalIgnoreCase));

    public static CalibreRemovalPlan Preview(IReadOnlyList<Book> books)
    {
        if (!CanPlan(books)) throw new InvalidOperationException("Select books from the same Calibre library to remove them together.");
        if (!File.Exists(Executable)) throw new InvalidOperationException("Install Calibre in its standard Windows location to remove books from Calibre.");
        var first = books[0].Calibre!;
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first.LibraryFolder));
        using var connection = Open(folder, first.LibraryId);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, title, path FROM books WHERE lower(uuid) = $uuid
            """;
        var uuid = command.Parameters.Add("$uuid", SqliteType.Text);
        var targets = new List<CalibreRemovalTarget>();
        foreach (var book in books)
        {
            var sourceId = book.Calibre!.BookId;
            uuid.Value = sourceId.ToString();
            long calibreId;
            string title, directory, relative;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                    throw new InvalidOperationException($"Calibre no longer contains “{book.Title}”. Refresh the connection or remove only its Personal Library entry.");
                calibreId = reader.GetInt64(0);
                title = reader.GetString(1);
                relative = reader.GetString(2);
                directory = CalibreImporter.ResolvePath(folder, relative)
                    ?? throw new InvalidDataException("A selected Calibre book folder is outside the library.");
                if (reader.Read()) throw new InvalidDataException("Calibre returned a duplicate book identity.");
            }
            // Calibre removes the entire book directory, including extra files.
            var parts = Path.GetRelativePath(folder, directory).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0].StartsWith('.'))
                throw new InvalidDataException("Calibre did not report a dedicated book folder.");
            EnsureSafeDirectory(folder, directory);
            using var files = connection.CreateCommand();
            files.Transaction = transaction;
            files.CommandText = "SELECT format FROM data WHERE book = $id ORDER BY format";
            files.Parameters.AddWithValue("$id", calibreId);
            using var fileReader = files.ExecuteReader();
            var formats = new List<string>();
            while (fileReader.Read()) formats.Add(fileReader.GetString(0));
            targets.Add(new CalibreRemovalTarget(book.Id, sourceId, calibreId, title, directory, formats.ToArray()));
        }
        using var paths = connection.CreateCommand();
        paths.Transaction = transaction;
        paths.CommandText = "SELECT id, path FROM books";
        using var catalogue = paths.ExecuteReader();
        while (catalogue.Read())
        {
            var id = catalogue.GetInt64(0);
            var path = CalibreImporter.ResolvePath(folder, catalogue.GetString(1));
            if (path != null && targets.Any(target => target.CalibreId != id &&
                (target.Directory.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                 target.Directory.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith(target.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("A selected Calibre folder overlaps another book's folder.");
        }
        return new CalibreRemovalPlan(folder, first.LibraryId, targets.ToArray());
    }

    internal static (Guid[] Removed, string? Warning) Apply(CalibreRemovalPlan confirmed, IReadOnlyList<Book> books)
    {
        var current = Preview(books);
        if (current.LibraryId != confirmed.LibraryId || current.Folder != confirmed.Folder
            || current.Targets.Length != confirmed.Targets.Length
            || current.Targets.Zip(confirmed.Targets).Any(pair => !SameTarget(pair.First, pair.Second)))
            throw new InvalidOperationException("The Calibre selection changed after preview. Review the current books and formats before trying again.");

        var command = new ProcessStartInfo(Executable)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        command.ArgumentList.Add("remove");
        command.ArgumentList.Add("--with-library");
        command.ArgumentList.Add(current.Folder);
        command.ArgumentList.Add(string.Join(',', current.Targets.Select(target => target.CalibreId.ToString(CultureInfo.InvariantCulture))));
        using var process = Process.Start(command) ?? throw new InvalidOperationException("Calibre could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);

        using var verification = Open(current.Folder, current.LibraryId);
        using var check = verification.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM books WHERE id = $id OR lower(uuid) = $uuid";
        var id = check.Parameters.Add("$id", SqliteType.Integer);
        var uuid = check.Parameters.Add("$uuid", SqliteType.Text);
        var removed = new List<Guid>();
        foreach (var target in current.Targets)
        {
            id.Value = target.CalibreId;
            uuid.Value = target.SourceBookId.ToString();
            if (Convert.ToInt64(check.ExecuteScalar()) == 0 && !Directory.Exists(target.Directory))
                removed.Add(target.PersonalBookId);
        }
        var warning = process.ExitCode == 0 && removed.Count == current.Targets.Length ? null :
            $"Calibre removal was verified for {removed.Count} of {current.Targets.Length} books. " +
            "Close Calibre before retrying. Check Calibre's trash for any books it already moved.\n\n" +
            string.Join("\n", new[] { stderr.Result.Trim(), stdout.Result.Trim() }.Where(value => value.Length > 0));
        return (removed.ToArray(), warning);
    }

    private static SqliteConnection Open(string folder, Guid identity)
    {
        var database = Path.Combine(folder, "metadata.db");
        if (!File.Exists(database)) throw new IOException("The saved Calibre library folder is unavailable. Refresh the connection first.");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT uuid FROM library_id LIMIT 1";
            if (!Guid.TryParse(command.ExecuteScalar() as string, out var found) || found != identity)
                throw new InvalidDataException("The folder now points to a different Calibre library.");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static bool SameTarget(CalibreRemovalTarget left, CalibreRemovalTarget right) =>
        left.PersonalBookId == right.PersonalBookId && left.SourceBookId == right.SourceBookId
        && left.CalibreId == right.CalibreId && left.Title == right.Title && left.Directory == right.Directory
        && left.Formats.SequenceEqual(right.Formats);

    private static void EnsureSafeDirectory(string root, string directory)
    {
        var ancestor = new DirectoryInfo(directory);
        while (true)
        {
            if (!ancestor.Exists || (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A selected book folder is missing or is a linked directory. Refresh the connection first.");
            if (ancestor.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
            ancestor = ancestor.Parent ?? throw new InvalidDataException("The book folder is outside the library.");
        }
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(directory));
        while (pending.TryPop(out var folder))
        {
            foreach (var child in folder.EnumerateFileSystemInfos())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A selected book folder contains a linked file or folder.");
                if (child is DirectoryInfo subfolder) pending.Push(subfolder);
            }
        }
    }
}
