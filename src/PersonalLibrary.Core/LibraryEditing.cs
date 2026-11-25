namespace PersonalLibrary.Core;

public enum LibrarySort { ToReadPosition, DateAdded, Title, Author }

public sealed record LibraryDisplay(int Columns = 10, int TitleSize = 18, int DetailSize = 14)
{
    public LibraryDisplay Validated() => new(Math.Clamp(Columns, 1, 20), Math.Clamp(TitleSize, 12, 32), Math.Clamp(DetailSize, 10, 24));
}

public static class LibrarySelection
{
    public static void Change(HashSet<Guid> selected, IReadOnlyList<Guid> ordered, Guid? anchor, Guid target, bool value, bool range)
    {
        var ids = ordered.ToList();
        var start = anchor.HasValue ? ids.IndexOf(anchor.Value) : -1;
        var end = ids.IndexOf(target);
        if (end < 0) return;
        var affected = range && start >= 0 ? ids.Skip(Math.Min(start, end)).Take(Math.Abs(start - end) + 1) : [target];
        foreach (var id in affected)
            if (value) selected.Add(id); else selected.Remove(id);
    }
}

// Null means keep the existing value; empty strings deliberately clear a text field.
public sealed record BulkBookChanges(string? Language = null, string? Publisher = null, string? Author = null,
    int? OriginalYear = null, bool SetOriginalYear = false, int? EditionYear = null, bool SetEditionYear = false,
    int? Pages = null, bool SetPages = false, ReadingStatus? Status = null, int? Rating = null, bool SetRating = false,
    string? AddShelves = null)
{
    public void Apply(Book book)
    {
        var edition = book.SelectedEdition;
        if (Language != null) edition.Language = Language.Trim();
        if (Publisher != null) edition.Publisher = Publisher.Trim();
        if (Author != null) book.Author = Author.Trim();
        if (SetOriginalYear) book.OriginalYear = OriginalYear;
        if (SetEditionYear) edition.PublicationYear = EditionYear;
        if (SetPages) edition.PageCount = Pages;
        if (SetRating) book.Rating = Rating;
        if (Status.HasValue) ReadingRules.Classify(book, Status.Value);
        if (AddShelves != null)
            foreach (var shelf in AddShelves.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                book.Shelves.Add(shelf);
        ReadingRules.Validate(book);
    }
}
