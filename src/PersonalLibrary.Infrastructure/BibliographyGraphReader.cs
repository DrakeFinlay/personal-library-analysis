using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed partial class LibraryStore
{
    public EpubCitation[] ReadCitationEvidence(Guid id, IReadOnlyList<Book>? library = null)
    {
        lock (gate)
        {
            library ??= Books(); var text = Bibliography(id).Text; var identities = CitationIdentities().Entries;
            return ReviewedCitations(id, CitationProcessing.Process((ReadEpubBibliography(id)?.Entries ?? []).Concat(ReadOnlineBibliography(id)?.Entries ?? []), identities)
                .Concat(BibliographyMatches.ReadText(text, library, identities, Hash(System.Text.Encoding.UTF8.GetBytes(text)))));
        }
    }
    public BibliographyGraph ReadBibliographyGraph(bool originalTextOnly = false)
    {
        lock (gate)
        {
            var library = Books();
            return BibliographyGraph.Build(library, id =>
            {
                var citations = ReadCitationEvidence(id, library);
                if (!originalTextOnly) return citations;
                var source = library.Single(book => book.Id == id);
                return citations.Where(citation => CitationProvenance.Assess(source, citation,
                    BibliographyMatches.Find(citation, library) is [var target] ? target : null).EligibleOriginalText);
            });
        }
    }
}
