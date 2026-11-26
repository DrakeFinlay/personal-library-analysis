using System.IO.Compression;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class EpubImportChecks
{
    public static void Run(string workspace, Action<bool, string> check, Action<Action, string> reject)
    {
        var path = Path.Combine(workspace, "manual-source.epub");
        Write(path, "First fictional work");
        using var store = new LibraryStore(Path.Combine(workspace, "epub-import"));
        var book = new Book { Title = "Unlinked synthetic source" }; store.Save(book);
        store.SaveBibliography(book.Id, "Keep reader notes", null);
        var preview = store.PreviewEpubBibliography(book.Id, path);
        check(preview.Bibliography.Entries.Single().Title == "First fictional work" && !store.HasEpubBibliography(book.Id),
            "Manual EPUB analysis recognizes Bibliography of Works Cited without saving or requiring Calibre");
        store.SaveOnlineBibliography(book.Id, preview.Bibliography with { OnlineSource = new("Synthetic", "Source", "A", "2020",
            "https://example.invalid/item", "https://example.invalid/source.epub", DateTimeOffset.UtcNow) });
        var before = store.Changes(book.Id).Count;
        var backup = store.ApplyEpubBibliography(preview);
        var saved = store.ReadEpubBibliography(book.Id)!;
        var sources = Directory.GetFiles(Path.GetDirectoryName(store.BibliographyPath(book.Id))!, "*.epub", SearchOption.AllDirectories);
        check(File.Exists(backup) && sources.Length == 1 && File.ReadAllBytes(sources[0]).SequenceEqual(File.ReadAllBytes(path)) &&
            saved.Entries.Single().Title == "First fictional work" && store.Bibliography(book.Id).Text == "Keep reader notes" && store.HasOnlineBibliography(book.Id),
            "Reviewed EPUB import verifies a backup, retains the exact source, and preserves personal and online bibliographies");
        check(store.Changes(book.Id).Count == before + 1 && store.Changes(book.Id)[0].Details!.Contains("First fictional work"),
            "EPUB import records bibliography evidence in source-book change history");
        var stale = store.PreviewEpubBibliography(book.Id, path);
        Write(path, "Replacement fictional work");
        reject(() => store.ApplyEpubBibliography(stale), "Import rejects an EPUB changed since analysis");
        var replacement = store.PreviewEpubBibliography(book.Id, path);
        store.SaveEpubBibliography(book.Id, saved with { SourceFile = "another-edit.epub" });
        reject(() => store.ApplyEpubBibliography(replacement), "Import rejects a bibliography changed since analysis");
        store.ApplyEpubBibliography(store.PreviewEpubBibliography(book.Id, path));
        check(store.ReadEpubBibliography(book.Id)!.Entries.Single().Title == "Replacement fictional work" &&
            store.Changes(book.Id).Any(change => change.Details?.Contains("First fictional work") == true),
            "Replacement EPUB bibliography saves after reanalysis and retains the earlier evidence in history");
        reject(() => store.PreviewEpubBibliography(Guid.NewGuid(), path), "EPUB import requires an existing source book");
        var empty = Path.Combine(workspace, "no-bibliography.epub");
        using (var zip = ZipFile.Open(empty, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("chapter.xhtml").Open())) writer.Write("<html><body><h1>A chapter</h1><p>Prose.</p></body></html>");
        var count = store.Changes(book.Id).Count;
        reject(() => store.PreviewEpubBibliography(book.Id, empty), "EPUB without a dedicated bibliography reports failure");
        check(store.Changes(book.Id).Count == count && store.ReadEpubBibliography(book.Id)!.Entries.Single().Title == "Replacement fictional work",
            "Unsuccessful analysis preserves the saved bibliography and audit history");
        var combined = Path.Combine(workspace, "combined-reading-heading.epub");
        using (var zip = ZipFile.Open(combined, ZipArchiveMode.Create))
        {
            using (var toc = new StreamWriter(zip.CreateEntry("toc.xhtml").Open()))
                toc.Write("<html><body><nav><p><a href='works.xhtml'>Bibliography and Recommended Reading</a></p></nav></body></html>");
            using var writer = new StreamWriter(zip.CreateEntry("works.xhtml").Open());
            writer.Write("<html><body><p class='toc-title'>Bibliography and<br/>Recommended Reading</p><p class='bib'>Writer, Alice. 2020. <i>A Fictional Coast.</i> Town: Press.</p><p class='bib'>Reader, Bob. [1900] 2021. <i>Another Fictional Coast.</i> Town: Press.</p></body></html>");
        }
        var discovered = EpubBibliographyReader.Read(combined);
        check(discovered.Entries.Length == 2 && discovered.Entries[0].Title == "A Fictional Coast." && discovered.Entries[0].Author == "Writer, Alice" && discovered.Entries[1].Author == "Reader, Bob",
            "Combined bibliography/reading headings with line breaks are discovered and publication/reprint years stay out of author names");
        check(discovered.Entries.All(entry => entry.ArchiveEntry == "works.xhtml" && entry.Text.Contains("Town: Press")),
            "Combined-heading extraction ignores navigation entries and preserves complete publication evidence");
    }

    private static void Write(string path, string title)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("works.xhtml").Open());
        writer.Write($"<html><body><h2>BIBLIOGRAPHY OF WORKS CITED</h2><blockquote><p>Fictional, A., <em>{title}</em> (2020).</p></blockquote></body></html>");
    }
}
