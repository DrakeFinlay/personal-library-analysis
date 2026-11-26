using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

internal static class LocalCitationChecks
{
    public static void Run(string workspace, Action<bool, string> check)
    {
        var source = new Book { Title = "A Fictional Bibliography", Status = ReadingStatus.Unclassified };
        var second = new Book { Title = "Another Source" };
        var target = new Book { Title = "A History of Quiet Cities: An Imaginary Survey", Author = "Theo Ashford" };
        var wrongAuthor = new Book { Title = target.Title, Author = "Someone Else" };
        var excluded = new Book { Title = "Excluded History", Author = "Theo Ashford", Excluded = true };
        var citation = new EpubCitation(1, "History", "Ashford, Theo. A History of Quiet Cities. Fictional Press, 2020.", "A History of Quiet Cities", "Ashford, Theo", "bibliography", null);
        Book[] library = [source, second, target, wrongAuthor, excluded];
        var aggregate = BibliographyMatches.All(library, id => id == source.Id || id == second.Id ? [citation, citation] : []);
        check(aggregate.Count == 1 && aggregate[0].Book.Id == target.Id && aggregate[0].Evidence.Count == 2,
            "Combined bibliography view deduplicates local titles, retains source evidence, and ignores source ratings and ownership");
        var text = "Ashford, Theo. A History of Quiet\nCities. Fictional Press, 2020.\nElse, Someone. An unrelated title. Another Press, 2021.";
        var scanned = BibliographyMatches.ReadText(text, library);
        check(scanned.Any(item => BibliographyMatches.Find(item, library).Any(book => book.Id == target.Id)) &&
            !scanned.Any(item => BibliographyMatches.Find(item, library).Any(book => book.Id == wrongAuthor.Id)),
            "Wrapped OCR references match known titles only with corresponding author evidence");
        var shortTitle = new Book { Title = "Dune", Author = "Frank Herbert" };
        check(BibliographyMatches.ReadText("Herbert, Frank. Dune. Fictional reprint, 2020.", [shortTitle])
            .Any(item => BibliographyMatches.Find(item, [shortTitle]).Length == 1),
            "Short cited titles can match when the author supplies identifying evidence");
        using var store = new LibraryStore(Path.Combine(workspace, "combined-bibliographies"));
        store.SetSetting("synthetic", "true");
        store.SaveBatch(library);
        store.SaveBibliography(source.Id, text, null);
        store.SaveEpubBibliography(second.Id, new("Fictional EPUB", "hash", [], [citation], 0));
        var persisted = BibliographyMatches.All(store.Books(), id => (store.ReadEpubBibliography(id)?.Entries ?? [])
            .Concat(store.ReadOnlineBibliography(id)?.Entries ?? []).Concat(BibliographyMatches.ReadText(store.Bibliography(id).Text, library)));
        check(persisted.Single().Evidence.Select(item => item.Source.Id).Distinct().Count() == 2 && store.Bibliography(source.Id).Text == text,
            "Pasted/scanned text and EPUB evidence combine while the original editable OCR text stays intact");
    }
}
