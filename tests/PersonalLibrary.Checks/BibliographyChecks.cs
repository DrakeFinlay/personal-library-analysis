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
        var markdown = saved!.ToMarkdown();
        check(markdown.Contains("Source edition note.") && markdown.Contains("## General") &&
            markdown.Contains("1. Reader, A: First title") && markdown.Contains("## History") &&
            markdown.Contains("2. Unknown, B: Plain text citation"), "The Markdown preview includes every saved section and citation");
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
