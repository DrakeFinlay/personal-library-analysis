namespace PersonalLibrary.Core;

public enum ReadingStatus { ToRead, CurrentlyReading, Read, PartiallyRead, DidNotFinish, ReferenceOnly, Unclassified, MusicBook, PhotographyArtBook }
public enum ReadingOutcome { InProgress, Completed, PartiallyRead, DidNotFinish, Superseded }
public enum ProgressUnit { Page, Percent }
public enum PurchaseCondition { New, Used }
public enum PurchaseCurrency { CAD, USD, JPY, EUR }

public sealed class PhysicalCopy
{
    public int? PurchaseYear { get; set; }
    public decimal? PurchasePrice { get; set; }
    public PurchaseCurrency PaidCurrency { get; set; }
    public string? PurchasedFrom { get; set; }
    public PurchaseCondition? PurchaseCondition { get; set; }
    public bool IsGift { get; set; }
    public bool IsLibraryLoan { get; set; }
    public bool Sold { get; set; }
    public decimal? SoldFor { get; set; }
    public PurchaseCurrency SaleCurrency { get; set; }
}

public sealed record ReadingSection(Guid Id, string Name)
{
    public const string UnassignedName = "Other books";
}

public sealed class Edition
{
    public override string ToString() => Label;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "Default edition";
    public string Isbn { get; set; } = "";
    // Null allows a source to fill the field. An empty string records a cleared value.
    public string? Publisher { get; set; }
    public string Language { get; set; } = "English";
    public int? PublicationYear { get; set; }
    public int? PageCount { get; set; }
    public string? CoverFile { get; set; }
    public string? CoverSource { get; set; }
    public string? CoverSourceIsbn { get; set; }
    public bool CoverIsAlternateEdition { get; set; }
    public string? OpenLibraryEditionId { get; set; }
    public List<string> Holdings { get; set; } = [];
    public decimal? Msrp { get; set; }
    public PurchaseCurrency MsrpCurrency { get; set; }
    public string? MsrpSource { get; set; }
    public string? MsrpSourceIsbn { get; set; }
    public bool MsrpIsEstimated { get; set; }
    public decimal? PurchasePrice { get; set; }
    public PurchaseCurrency PaidCurrency { get; set; }
    public string? PurchasedFrom { get; set; }
    public PurchaseCondition? PurchaseCondition { get; set; }
    public bool IsGift { get; set; }
    public bool IsLibraryLoan { get; set; }
    public List<PhysicalCopy> PhysicalCopies { get; set; } = [];
    public bool HasLegacyPurchase => PurchasePrice.HasValue || PurchaseCondition.HasValue || !string.IsNullOrWhiteSpace(PurchasedFrom) || IsGift || IsLibraryLoan;

    public PhysicalCopy LegacyPurchase() => new()
    {
        PurchasePrice = PurchasePrice, PaidCurrency = PaidCurrency,
        PurchasedFrom = PurchasedFrom, PurchaseCondition = PurchaseCondition, IsGift = IsGift, IsLibraryLoan = IsLibraryLoan
    };

    public void SetLegacyPurchase(PhysicalCopy purchase)
    {
        PurchasePrice = purchase.PurchasePrice;
        PaidCurrency = purchase.PaidCurrency;
        PurchasedFrom = purchase.PurchasedFrom;
        PurchaseCondition = purchase.PurchaseCondition;
        IsGift = purchase.IsGift;
        IsLibraryLoan = purchase.IsLibraryLoan;
    }

    public void UpdatePhysicalHolding()
    {
        if (PhysicalCopies.Count == 0) return;
        Holdings.RemoveAll(holding => holding == "Physical");
        if (PhysicalCopies.Any(copy => !copy.Sold && !copy.IsLibraryLoan)) Holdings.Add("Physical");
    }
}

public sealed class ProgressEntry
{
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.Now;
    public decimal Value { get; set; }
    public ProgressUnit Unit { get; set; }
}

public sealed class Reading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EditionId { get; set; }
    public DateOnly? Started { get; set; }
    public DateOnly? Finished { get; set; }
    public int? ApproximateFinishYear { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int? CompletionYear => Finished?.Year ?? ApproximateFinishYear;
    public ReadingOutcome Outcome { get; set; }
    public List<ProgressEntry> Progress { get; set; } = [];
}

public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int? OriginalYear { get; set; }
    public string? OpenLibraryWorkId { get; set; }
    public ReadingStatus Status { get; set; } = ReadingStatus.ToRead;
    public List<string> Shelves { get; set; } = [];
    public List<string> ToReadGroups { get; set; } = [];
    public List<Edition> Editions { get; set; } = [new()];
    public Guid SelectedEditionId { get; set; }
    public List<Reading> Readings { get; set; } = [];
    public int? Rating { get; set; }
    public string Review { get; set; } = "";
    public string Notes { get; set; } = "";
    public CalibreReference? Calibre { get; set; }
    public GoodreadsReference? Goodreads { get; set; }
    public bool Excluded { get; set; }
    public bool ReadNext { get; set; }
    public List<int> PlannedReadingYears { get; set; } = [];
    public DateTimeOffset? RecommendationSnoozedUntil { get; set; }
    public bool RecommendationDismissed { get; set; }
    public Guid? ReadingSectionId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset ToReadAddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public Edition SelectedEdition => Editions.FirstOrDefault(e => e.Id == SelectedEditionId) ?? Editions.First();
    public Reading? ActiveReading => Readings.SingleOrDefault(r => r.Outcome == ReadingOutcome.InProgress);
}

public static class ReadingRules
{
    public static string OutcomeLabel(ReadingOutcome outcome) => outcome switch
    {
        ReadingOutcome.InProgress => "In progress", ReadingOutcome.Completed => "Completed",
        ReadingOutcome.DidNotFinish => "Did not finish", ReadingOutcome.Superseded => "Superseded by Goodreads import",
        _ => "Partially read"
    };
    public static string Label(ReadingStatus status) => status switch
    {
        ReadingStatus.ToRead => "To read", ReadingStatus.CurrentlyReading => "Currently reading",
        ReadingStatus.Read => "Read", ReadingStatus.PartiallyRead => "Partially read",
        ReadingStatus.DidNotFinish => "Did not finish", ReadingStatus.Unclassified => "Unclassified",
        ReadingStatus.MusicBook => "Music book", ReadingStatus.PhotographyArtBook => "Photography / art book",
        _ => "Reference only"
    };

    public static bool IsSpecialCollection(ReadingStatus status) => status is ReadingStatus.MusicBook or ReadingStatus.PhotographyArtBook;

    internal static bool CountsAsReading(Book book) =>
        !book.Excluded && book.Status != ReadingStatus.ReferenceOnly && !IsSpecialCollection(book.Status);

    public static void Classify(Book book, ReadingStatus status)
    {
        if (!Enum.IsDefined(status)) throw new InvalidOperationException("Choose a reading status.");
        if (book.Status == status) return;
        if (book.ActiveReading != null)
            throw new InvalidOperationException($"Finish or abandon the active reading of “{book.Title}” before changing its status.");
        if (status == ReadingStatus.CurrentlyReading)
        {
            Start(book, null);
            return;
        }
        ReadingOutcome? outcome = status switch
        {
            ReadingStatus.Read => ReadingOutcome.Completed,
            ReadingStatus.PartiallyRead => ReadingOutcome.PartiallyRead,
            ReadingStatus.DidNotFinish => ReadingOutcome.DidNotFinish,
            _ => null
        };
        if (outcome is { } value && book.Readings.All(reading => reading.Outcome != value))
            book.Readings.Add(new Reading { EditionId = book.SelectedEdition.Id, Outcome = value });
        book.Status = status;
        if (status == ReadingStatus.ToRead)
        {
            book.ToReadAddedAt = DateTimeOffset.Now;
            book.RecommendationDismissed = false;
        }
        else book.ReadNext = false;
    }

    public static void Validate(Book book)
    {
        if (string.IsNullOrWhiteSpace(book.Title)) throw new InvalidOperationException("A title is required.");
        if (book.Rating is < 1 or > 5) throw new InvalidOperationException("Ratings must be whole stars from 1 to 5.");
        if (!Enum.IsDefined(book.Status)) throw new InvalidOperationException("Unknown reading status.");
        if (book.Editions.Count == 0 || book.Editions.Select(e => e.Id).Distinct().Count() != book.Editions.Count)
            throw new InvalidOperationException("At least one uniquely identified edition is required.");
        if (book.SelectedEditionId != Guid.Empty && book.Editions.All(e => e.Id != book.SelectedEditionId))
            throw new InvalidOperationException("The selected edition does not belong to this book.");
        foreach (var edition in book.Editions)
        {
            if (edition.PageCount is <= 0) throw new InvalidOperationException("Page counts must be positive or blank.");
            if (edition.PublicationYear == 0) throw new InvalidOperationException("Use negative years for BCE; there is no year zero.");
            PurchaseStatistics.ValidateAmount(edition.Msrp, "MSRP");
            PurchaseStatistics.ValidateAmount(edition.PurchasePrice, "Purchase price");
            if (!Enum.IsDefined(edition.MsrpCurrency) || !Enum.IsDefined(edition.PaidCurrency))
                throw new InvalidOperationException("Choose CAD, USD, JPY, or EUR for each amount.");
            if (edition.PurchaseCondition is { } condition && !Enum.IsDefined(condition))
                throw new InvalidOperationException("Choose New, Used, or leave purchase condition blank.");
            if (edition.IsGift && edition.IsLibraryLoan)
                throw new InvalidOperationException("Choose gift or library for an individual copy.");
            foreach (var copy in edition.PhysicalCopies)
            {
                if (copy.IsGift && copy.IsLibraryLoan || copy.IsLibraryLoan && copy.Sold)
                    throw new InvalidOperationException("A library loan cannot be a gift or a sold copy.");
                PurchaseStatistics.ValidateAmount(copy.PurchasePrice, "Purchase price");
                PurchaseStatistics.ValidateAmount(copy.SoldFor, "Sold for");
                if (copy.PurchaseYear is < 1 or > 9999)
                    throw new InvalidOperationException("Use a four-digit purchase year or leave it blank.");
                if (!Enum.IsDefined(copy.PaidCurrency) || !Enum.IsDefined(copy.SaleCurrency))
                    throw new InvalidOperationException("Choose CAD, USD, JPY, or EUR for each amount.");
                if (copy.PurchaseCondition is { } copyCondition && !Enum.IsDefined(copyCondition))
                    throw new InvalidOperationException("Choose New, Used, or leave purchase condition blank.");
                if (!copy.Sold && copy.SoldFor.HasValue)
                    throw new InvalidOperationException("Mark the physical copy sold to enter a sale amount.");
            }
        }
        if (book.OriginalYear == 0) throw new InvalidOperationException("Use negative years for BCE; there is no year zero.");
        if (book.Readings.Select(r => r.Id).Distinct().Count() != book.Readings.Count)
            throw new InvalidOperationException("Reading identities must be unique.");
        var active = book.Readings.Count(r => r.Outcome == ReadingOutcome.InProgress);
        if (active > 1 || (book.Status == ReadingStatus.CurrentlyReading) != (active == 1))
            throw new InvalidOperationException("Currently reading must have exactly one active reading.");
        if (book.Status == ReadingStatus.Read && !book.Readings.Any(r => r.Outcome == ReadingOutcome.Completed))
            throw new InvalidOperationException("Read books need a completed reading; its dates can be blank.");
        foreach (var reading in book.Readings)
        {
            if (!Enum.IsDefined(reading.Outcome) || book.Editions.All(e => e.Id != reading.EditionId))
                throw new InvalidOperationException("Reading edition or outcome is invalid.");
            if (reading.Started.HasValue && reading.Finished.HasValue && reading.Finished < reading.Started)
                throw new InvalidOperationException("Finish date cannot precede start date.");
            if (reading.ApproximateFinishYear is { } year && (year is < 1 or > 9999 || reading.Finished != null ||
                reading.Outcome != ReadingOutcome.Completed || reading.Started?.Year > year))
                throw new InvalidOperationException("A year-only finish must be valid, follow the start year, and belong to a completed reading.");
            if (reading.Outcome == ReadingOutcome.InProgress && reading.Finished.HasValue)
                throw new InvalidOperationException("An active reading cannot have a finish date.");
            foreach (var progress in reading.Progress)
                if (!Enum.IsDefined(progress.Unit) || progress.Value < 0 ||
                    (progress.Unit == ProgressUnit.Percent && progress.Value > 100))
                    throw new InvalidOperationException("Saved progress is invalid.");
        }
    }

    public static void Start(Book book, DateOnly? date)
    {
        if (book.ActiveReading != null) throw new InvalidOperationException("This book already has an active reading.");
        book.Readings.Add(new Reading { EditionId = book.SelectedEdition.Id, Started = date });
        book.Status = ReadingStatus.CurrentlyReading;
        book.ReadNext = false;
    }

    public static void Progress(Book book, decimal value, ProgressUnit unit)
    {
        if (!Enum.IsDefined(unit)) throw new InvalidOperationException("Choose page or percentage progress.");
        var reading = book.ActiveReading ?? throw new InvalidOperationException("Start a reading first.");
        var edition = book.Editions.Single(e => e.Id == reading.EditionId);
        if (value < 0 || (unit == ProgressUnit.Percent && value > 100) ||
            (unit == ProgressUnit.Page && value != decimal.Truncate(value)))
            throw new InvalidOperationException("Use a whole page number or a percentage from 0 to 100.");
        if (unit == ProgressUnit.Page && edition.PageCount.HasValue && value > edition.PageCount)
            throw new InvalidOperationException("Page exceeds this reading edition's page count. Correct that edition first.");
        reading.Progress.Add(new ProgressEntry { Value = value, Unit = unit });
    }

    public static void Finish(Book book, DateOnly? date, ReadingOutcome outcome)
    {
        if (outcome is not (ReadingOutcome.Completed or ReadingOutcome.PartiallyRead or ReadingOutcome.DidNotFinish))
            throw new InvalidOperationException("Choose a finished outcome.");
        var reading = book.ActiveReading ?? throw new InvalidOperationException("There is no active reading to finish.");
        if (date.HasValue && reading.Started.HasValue && date < reading.Started)
            throw new InvalidOperationException("Finish date cannot precede start date.");
        reading.Finished = date;
        reading.Outcome = outcome;
        book.Status = outcome switch { ReadingOutcome.Completed => ReadingStatus.Read,
            ReadingOutcome.DidNotFinish => ReadingStatus.DidNotFinish, _ => ReadingStatus.PartiallyRead };
    }

    internal static IEnumerable<Reading> CompletedReadings(Book book) =>
        CountsAsReading(book) ? book.Readings.Where(reading => reading.Outcome == ReadingOutcome.Completed) : [];

    public static int CompletedInYear(IEnumerable<Book> books, int year) =>
        books.SelectMany(CompletedReadings).Count(reading => reading.CompletionYear == year);

    public static string ProgressText(Book book)
    {
        var reading = book.ActiveReading;
        var progress = reading?.Progress.LastOrDefault();
        if (progress == null) return "No progress logged yet";
        var pages = book.Editions.Single(e => e.Id == reading!.EditionId).PageCount;
        return progress.Unit == ProgressUnit.Percent ? $"{progress.Value:0.#}% complete" :
            pages.HasValue ? $"Page {progress.Value:0} of {pages} · {progress.Value / pages.Value * 100:0}%" : $"Page {progress.Value:0}";
    }
}
