using System.Security.Cryptography;
using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record EpubBibliographyImportPreview(Guid BookId, string SourcePath,
    EpubBibliography Bibliography, string? ExpectedBibliographyHash);

public sealed partial class LibraryStore
{
    public EpubBibliographyImportPreview PreviewEpubBibliography(Guid bookId, string sourcePath)
    {
        string? expected;
        lock (gate)
        {
            if (FindBook(bookId) == null) throw new InvalidOperationException("Save the source book before importing a bibliography.");
            var current = EpubBibliographyPath(bookId);
            expected = File.Exists(current) ? Hash(File.ReadAllBytes(current)) : null;
        }
        var path = Path.GetFullPath(sourcePath);
        if (!Path.GetExtension(path).Equals(".epub", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an EPUB file.");
        using var input = File.OpenRead(path);
        var bibliography = EpubBibliographyReader.Read(input, Path.GetFileName(path), 128_000_000);
        return new(bookId, path, bibliography, expected);
    }

    public string ApplyEpubBibliography(EpubBibliographyImportPreview preview)
    {
        lock (gate)
        {
            if (FindBook(preview.BookId) == null) throw new InvalidOperationException("This source book was removed. Choose a book again.");
            var current = EpubBibliographyPath(preview.BookId);
            var currentHash = File.Exists(current) ? Hash(File.ReadAllBytes(current)) : null;
            if (currentHash != preview.ExpectedBibliographyHash)
                throw new InvalidOperationException("The saved EPUB bibliography changed. Analyze the file again before replacing it.");
            using var source = File.OpenRead(preview.SourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(source));
            if (hash != preview.Bibliography.SourceSha256)
                throw new InvalidOperationException("The EPUB changed after analysis. Analyze it again before saving.");
            source.Position = 0;
            var folder = Path.Combine(Path.GetDirectoryName(current)!, "sources", hash);
            var retained = Path.Combine(folder, Path.GetFileName(preview.SourcePath));
            var backup = Backup("pre-epub-bibliography-import"); PreviewBackup(backup);
            Directory.CreateDirectory(folder);
            var added = false;
            var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (!File.Exists(retained))
                {
                    using (var copy = File.Create(temporary)) source.CopyTo(copy);
                    File.Move(temporary, retained); added = true;
                }
                else if (Hash(File.ReadAllBytes(retained)) != hash)
                    throw new InvalidDataException("The retained source copy is damaged. Restore it from a verified backup before replacing this bibliography.");
                SaveEpubBibliography(preview.BookId, preview.Bibliography);
                return backup;
            }
            catch { if (added) File.Delete(retained); throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
