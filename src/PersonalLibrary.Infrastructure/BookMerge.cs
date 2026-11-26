using PersonalLibrary.Core;

namespace PersonalLibrary.Infrastructure;

internal static class BookMerge
{
    public static Book Combine(Book keep, Book duplicate)
    {
        if (keep.Calibre != null && duplicate.Calibre != null)
            throw new InvalidOperationException("Both records have Calibre links. Review them in Calibre before merging.");
        if (keep.Goodreads != null && duplicate.Goodreads != null)
            throw new InvalidOperationException("Both records have Goodreads links. Review the two source entries before merging.");
        if (keep.ActiveReading != null && duplicate.ActiveReading != null)
            throw new InvalidOperationException("Both records have an active reading. Review their progress and resolve one reading before merging.");
        if (keep.OpenLibraryWorkId != null && duplicate.OpenLibraryWorkId != null && keep.OpenLibraryWorkId != duplicate.OpenLibraryWorkId)
            throw new InvalidOperationException("The records link to different Open Library works. Verify their identities before merging.");

        var book = LibraryStore.Clone(keep);
        var other = LibraryStore.Clone(duplicate);
        var preferred = book.Goodreads != null || other.Goodreads == null ? book : other;
        var alternate = preferred == book ? other : book;
        var active = book.ActiveReading ?? other.ActiveReading;
        var meaningfulStatus = ReadingRules.IsSpecialCollection(book.Status) ? book
            : ReadingRules.IsSpecialCollection(other.Status) ? other
            : preferred.Status != ReadingStatus.Unclassified ? preferred : alternate;

        book.Title = preferred.Title;
        book.Author = string.IsNullOrWhiteSpace(preferred.Author) || preferred.Author.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? alternate.Author : preferred.Author;
        book.OriginalYear = preferred.OriginalYear ?? alternate.OriginalYear;
        book.OpenLibraryWorkId ??= other.OpenLibraryWorkId;
        book.SelectedEditionId = active?.EditionId ?? preferred.SelectedEdition.Id;
        book.Editions.AddRange(other.Editions);
        book.Readings.AddRange(other.Readings);
        book.Status = active != null ? ReadingStatus.CurrentlyReading : meaningfulStatus.Status;
        if (other.ActiveReading != null)
        {
            book.ReadingSectionId = other.ReadingSectionId;
            book.SortOrder = other.SortOrder;
        }
        else if (active == null) book.ReadingSectionId ??= other.ReadingSectionId;
        book.ToReadAddedAt = meaningfulStatus.ToReadAddedAt;
        if (!book.ToReadPositionIsLocal && book.ToReadPosition == null)
        { book.ToReadPosition = other.ToReadPosition; book.ToReadPositionIsLocal = other.ToReadPositionIsLocal; }
        if (!book.DateAddedIsLocal && (book.DateAdded == null || other.DateAdded < book.DateAdded))
        { book.DateAdded = other.DateAdded; book.DateAddedIsLocal = other.DateAddedIsLocal; }
        book.RecommendationDismissed |= other.RecommendationDismissed;
        if (book.RecommendationSnoozedUntil == null || other.RecommendationSnoozedUntil > book.RecommendationSnoozedUntil)
            book.RecommendationSnoozedUntil = other.RecommendationSnoozedUntil;
        if (book.RecommendationDismissed && book.Status == ReadingStatus.ToRead) Recommendations.Dismiss(book);
        book.ReadNext = book.Status == ReadingStatus.ToRead && (book.ReadNext || other.ReadNext);
        book.Excluded |= other.Excluded;
        book.Shelves.AddRange(other.Shelves);
        book.ToReadGroups.AddRange(other.ToReadGroups);
        book.PlannedReadingYears = book.PlannedReadingYears.Union(other.PlannedReadingYears).Order().ToList();
        book.Calibre ??= other.Calibre;
        book.Goodreads ??= other.Goodreads;

        var alternateNotes = book.Notes == other.Notes ? "" : other.Notes;
        var retainedReview = preferred.Review.Length > 0 ? preferred.Review : alternate.Review;
        var alternateReview = alternate.Review.Length > 0 && alternate.Review != retainedReview
            ? $"Alternate review from merged record:\n{alternate.Review}" : "";
        var retainedRating = preferred.Rating ?? alternate.Rating;
        var alternateRating = alternate.Rating is { } stars && stars != retainedRating
            ? $"Alternate rating from merged record: {stars}/5" : "";
        book.Review = retainedReview;
        book.Rating = retainedRating;
        book.Notes = string.Join("\n\n", new[] { book.Notes, alternateNotes, alternateReview, alternateRating }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        ReadingRules.Validate(book);
        return book;
    }
}
