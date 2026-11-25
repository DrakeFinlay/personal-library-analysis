using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed partial class LibraryStore
{
    public BibliographyGraph ReadBibliographyGraph()
    {
        lock (gate)
        {
            var library = Books();
            return BibliographyGraph.Build(library, id =>
                (ReadEpubBibliography(id)?.Entries ?? []).Concat(ReadOnlineBibliography(id)?.Entries ?? [])
                    .Concat(BibliographyMatches.ReadText(Bibliography(id).Text, library)));
        }
    }
}
