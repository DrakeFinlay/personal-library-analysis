using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

public sealed record CatalogEditionChange(string Title, string Isbn, string EditionId, int? PageCount, string? Publisher)
{
    public string Label => $"{Title} · ISBN {Isbn} · {EditionId}" + (PageCount is { } pages ? $" · {pages} pages" : "")
        + (Publisher is { } publisher ? $" · Publisher: {publisher}" : "");
}

public sealed class CatalogMatchPreview
{
    internal List<Book> Pending { get; } = [];
    internal List<CatalogEditionChange> Changes { get; } = [];
    public IReadOnlyList<CatalogEditionChange> Editions => Changes;
    public int Ambiguous { get; internal set; }
}

public static class CatalogReconciliation
{
    public static CatalogMatchPreview Preview(LibraryStore store, OfflineCatalog catalog)
    {
        if (!catalog.Installed) throw new InvalidOperationException("Install the offline catalog first.");
        var preview = new CatalogMatchPreview();
        var matches = new Dictionary<string, IReadOnlyList<MetadataMatch>>();
        foreach (var book in store.Books().Where(book => !book.Excluded))
        {
            var changed = false;
            foreach (var edition in book.Editions)
            {
                if (OpenLibraryLookup.IsbnKey(edition.Isbn) is not { Length: > 0 } isbn
                    || (edition.OpenLibraryEditionId != null && edition.PageCount != null && edition.Publisher != null)) continue;
                if (!matches.TryGetValue(isbn, out var candidates))
                    matches[isbn] = candidates = catalog.Search("", "", isbn);
                var candidate = candidates.Count == 1 ? candidates[0] : null;
                if (candidates.Count == 0) continue;
                if (candidate?.Edition is not { } metadata || candidate.EditionId == null
                    || (edition.OpenLibraryEditionId is { } existing && existing != candidate.EditionId)
                    || (book.OpenLibraryWorkId is { } work && candidate.WorkId is { } other && work != other)
                    || !BookIdentity.MatchesTitleAuthor(book, candidate))
                {
                    preview.Ambiguous++;
                    continue;
                }
                var newPages = edition.PageCount == null ? metadata.Pages : null;
                var publisher = edition.Publisher == null && !string.IsNullOrWhiteSpace(metadata.Publisher)
                    ? metadata.Publisher.Trim() : null;
                if (edition.OpenLibraryEditionId != null && newPages == null && publisher == null) continue;
                edition.OpenLibraryEditionId = candidate.EditionId;
                edition.PageCount ??= newPages;
                edition.Publisher ??= publisher;
                preview.Changes.Add(new(book.Title, isbn, candidate.EditionId, newPages, publisher));
                changed = true;
            }
            if (changed) preview.Pending.Add(book);
        }
        return preview;
    }
}
