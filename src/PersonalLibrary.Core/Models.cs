namespace PersonalLibrary.Core;

public enum ReadingStatus { ToRead, CurrentlyReading, Read, PartiallyRead, DidNotFinish, ReferenceOnly }
public enum ReadingOutcome { InProgress, Completed, PartiallyRead, DidNotFinish }
public enum ProgressUnit { Page, Percent }

public sealed class Edition
{
    public override string ToString() => Label;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "Default edition";
    public string Isbn { get; set; } = "";
    public string Language { get; set; } = "English";
    public int? PublicationYear { get; set; }
    public int? PageCount { get; set; }
    public string? CoverFile { get; set; }
    public List<string> Holdings { get; set; } = [];
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
    public ReadingOutcome Outcome { get; set; }
    public List<ProgressEntry> Progress { get; set; } = [];
}

public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int? OriginalYear { get; set; }
    public ReadingStatus Status { get; set; } = ReadingStatus.ToRead;
    public List<string> Shelves { get; set; } = [];
    public List<Edition> Editions { get; set; } = [new()];
    public Guid SelectedEditionId { get; set; }
    public List<Reading> Readings { get; set; } = [];
    public int? Rating { get; set; }
    public string Review { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Excluded { get; set; }
    public bool ReadNext { get; set; }
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
        ReadingOutcome.DidNotFinish => "Did not finish", _ => "Partially read"
    };
    public static string Label(ReadingStatus status) => status switch
    {
        ReadingStatus.ToRead => "To read", ReadingStatus.CurrentlyReading => "Currently reading",
        ReadingStatus.Read => "Read", ReadingStatus.PartiallyRead => "Partially read",
        ReadingStatus.DidNotFinish => "Did not finish", _ => "Reference only"
    };

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
        if (!Enum.IsDefined(outcome) || outcome == ReadingOutcome.InProgress) throw new InvalidOperationException("Choose a finished outcome.");
        var reading = book.ActiveReading ?? throw new InvalidOperationException("There is no active reading to finish.");
        if (date.HasValue && reading.Started.HasValue && date < reading.Started)
            throw new InvalidOperationException("Finish date cannot precede start date.");
        reading.Finished = date;
        reading.Outcome = outcome;
        book.Status = outcome switch { ReadingOutcome.Completed => ReadingStatus.Read,
            ReadingOutcome.DidNotFinish => ReadingStatus.DidNotFinish, _ => ReadingStatus.PartiallyRead };
    }

    public static int CompletedInYear(IEnumerable<Book> books, int year) => books.Where(b => !b.Excluded)
        .Sum(b => b.Readings.Count(r => r.Outcome == ReadingOutcome.Completed && r.Finished?.Year == year));

    public static string ProgressText(Book book)
    {
        var reading = book.ActiveReading;
        var progress = reading?.Progress.LastOrDefault();
        if (progress == null) return "Ready for the next chapter";
        var pages = book.Editions.Single(e => e.Id == reading!.EditionId).PageCount;
        return progress.Unit == ProgressUnit.Percent ? $"{progress.Value:0.#}% complete" :
            pages.HasValue ? $"Page {progress.Value:0} of {pages} · {progress.Value / pages.Value * 100:0}%" : $"Page {progress.Value:0}";
    }
}
