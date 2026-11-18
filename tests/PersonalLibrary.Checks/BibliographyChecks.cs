using System.IO.Compression;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class BibliographyChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var path = Path.Combine(workspace, "synthetic-bibliography.epub");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(archive, "OEBPS/notes.xhtml", "<html><body><h1>Notes</h1><p>Unrelated notes</p></body></html>");
            Write(archive, "OEBPS/appendix.xhtml", """
                <html xmlns="http://www.w3.org/1999/xhtml"><body><section>
                <h1>Bibliography</h1><p>Source edition note.</p><p class="sec">General</p>
                <p class="bib" id="citation1">Reader, A: <em>First title</em> (2020)</p>
                <p class="sec">History</p>
                <p class="bib">Unknown, B: Plain text citation (2021)</p>
                </section></body></html>
                """);
        }
        var bibliography = EpubBibliographyReader.Read(path);
        check(bibliography.Entries.Length == 2 && bibliography.Entries[0].Title == "First title"
            && bibliography.Entries[0].Anchor == "citation1" && bibliography.Entries[1].Section == "History"
            && bibliography.Entries[1].Text.Contains("Plain text citation") && bibliography.IntroductoryNotes.Single() == "Source edition note.",
            "EPUB extraction retains each bibliography entry, section and source location");
        using var store = new LibraryStore(Path.Combine(workspace, "epub-bibliography-target"));
        var book = new Book { Title = "Synthetic source" };
        store.Save(book);
        store.SaveBibliography(book.Id, "Existing reader notes", null);
        store.SaveEpubBibliography(book.Id, bibliography);
        var saved = store.ReadEpubBibliography(book.Id);
        check(saved?.Entries.Length == 2 && store.Bibliography(book.Id).Text == "Existing reader notes",
            "Saving EPUB citations preserves editable Markdown");
        var photo = Path.Combine(workspace, "synthetic-bibliography-photo.jpg");
        File.WriteAllBytes(photo, [6, 9, 2, 6]);
        var draft = store.Bibliography(book.Id);
        var savedText = store.SaveBibliography(book.Id, "Reviewed scan text", draft.Hash, [photo]);
        var sourceFolder = Path.Combine(Path.GetDirectoryName(store.BibliographyPath(book.Id))!, "sources");
        store.SaveBibliography(book.Id, "Corrected scan text", savedText.Hash, [photo]);
        var storedPhotos = Directory.GetFiles(sourceFolder, "*.jpg", SearchOption.AllDirectories);
        check(storedPhotos.Length == 1 && File.ReadAllBytes(storedPhotos[0]).SequenceEqual(File.ReadAllBytes(photo))
            && store.Bibliography(book.Id).Text == "Corrected scan text" && store.ReadEpubBibliography(book.Id)?.Entries.Length == 2,
            "Saving reviewed OCR text preserves and deduplicates original photos alongside existing EPUB citations");
        var markdown = saved!.ToMarkdown();
        check(markdown.Contains("Source edition note.") && markdown.Contains("## General") &&
            markdown.Contains("1. Reader, A: First title") && markdown.Contains("## History") &&
            markdown.Contains("2. Unknown, B: Plain text citation"), "The Markdown preview includes every saved section and citation");

        var mixed = Path.Combine(workspace, "synthetic-mixed-bibliography.epub");
        using (var archive = ZipFile.Open(mixed, ZipArchiveMode.Create))
        {
            Write(archive, "contents.xhtml", "<html><body><p><a href='works.xhtml'>Bibliography</a></p><p>Another chapter</p></body></html>");
            Write(archive, "works.xhtml", """
                <html><body><p>Preceding chapter text.</p><p><b>Selected Bibliography</b></p>
                <p class="BIB1" id="reference">Author, A.&nbsp;<em>First work</em> (2021).</p>
                <p class="bib-bot">Author, B. <em>Second work</em> (2022).</p>
                <h2>Index</h2><p>Unrelated index entry.</p></body></html>
                """);
            Write(archive, "other-works.xhtml", """
                <html><body><h2><a href="contents.xhtml#other-works">Bibliography</a></h2>
                <table><tr><td><h2>Table heading</h2><p>Not a citation.</p></td></tr></table>
                <dl><dt class="biblio">Author, C. <em>Third work</em> (2023).</dt></dl>
                <h2>Index of Names</h2><p>Irrelevant name.</p></body></html>
                """);
        }
        var scoped = EpubBibliographyReader.Read(mixed);
        check(scoped.Entries.Select(citation => citation.Title).SequenceEqual(["First work", "Second work", "Third work"]) &&
              scoped.Entries[0].Anchor == "reference" && scoped.Entries[0].Text.Contains("A. First work") &&
              scoped.UnreadableDocuments == 0 && scoped.IntroductoryNotes.Length == 0,
            "EPUB extraction handles styled headings, backlinks, definition lists, and HTML entities while excluding chapter text, navigation, tables, and indexes");
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
