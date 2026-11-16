using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class BookEditor : Window
{
    private readonly LibraryStore store;
    private readonly Book book;
    private readonly TextBox title, author, year, shelves, rating, review, notes, bibliography;
    private readonly TextBox editionLabel, isbn, publisher, language, editionYear, pages;
    private readonly CheckBox physical, ebook, audio, readNext, excluded;
    private readonly ComboBox editionPicker;
    private readonly StackPanel readingPanel = new();
    private readonly Dictionary<Guid, byte[]> pendingCovers = [];
    private Edition editingEdition;
    private BibliographyText originalBibliography;
    private bool switchingEdition;
    private bool changed;
    private bool allowClose;
    private bool initializing = true;
    private readonly List<(Guid Id, TextBox Start, TextBox Finish)> readingDates = [];

    public BookEditor(LibraryStore store, Book source)
    {
        this.store = store; book = LibraryStore.Clone(source); editingEdition = book.SelectedEdition;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        book.SelectedEditionId = editingEdition.Id; originalBibliography = store.Bibliography(book.Id);
        Title = string.IsNullOrWhiteSpace(book.Title) ? "Add a book" : book.Title;
        Width = 820; Height = 820; MinWidth = 660; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var bottom = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        bottom.Children.Add(Ui.Button("Save & close", () => { SaveAll(); allowClose = true; Close(); }, true));
        bottom.Children.Add(Ui.Button("Cancel / close", Close)); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var tabs = new TabControl(); root.Children.Add(tabs); Content = root;
        var metadata = new StackPanel { Margin = new Thickness(10) };
        var download = Ui.Button("Find metadata or a cover…", DownloadMetadata);
        download.HorizontalAlignment = HorizontalAlignment.Left;
        metadata.Children.Add(download);
        title = Ui.Input(metadata, "Title", book.Title); author = Ui.Input(metadata, "Author / contributors", book.Author);
        year = Ui.Input(metadata, "Original publication year (negative for BCE; blank if unknown)", book.OriginalYear?.ToString() ?? "");
        shelves = Ui.Input(metadata, "Shelves (comma separated)", string.Join(", ", book.Shelves));
        readNext = Check(metadata, "Add to Read next", book.ReadNext);
        excluded = Check(metadata, "Excluded content (manga/comics or outside this library's scope)", book.Excluded);
        metadata.Children.Add(Ui.Heading("Editions & ownership", 23));
        editionPicker = new ComboBox { DisplayMemberPath = "Label", ItemsSource = book.Editions, SelectedItem = editingEdition };
        metadata.Children.Add(editionPicker);
        metadata.Children.Add(Ui.Button("+ Add another edition", () =>
        {
            CaptureEdition(); var edition = new Edition { Label = "Another edition" }; book.Editions.Add(edition);
            switchingEdition = true; editionPicker.ItemsSource = null; editionPicker.ItemsSource = book.Editions; editionPicker.SelectedItem = edition;
            switchingEdition = false; editingEdition = edition; LoadEdition(); changed = true;
        }));
        editionLabel = Ui.Input(metadata, "Edition label", editingEdition.Label);
        isbn = Ui.Input(metadata, "ISBN (optional)", editingEdition.Isbn);
        publisher = Ui.Input(metadata, "Publisher (optional)", editingEdition.Publisher ?? "");
        language = Ui.Input(metadata, "Language", editingEdition.Language);
        editionYear = Ui.Input(metadata, "Edition publication year", editingEdition.PublicationYear?.ToString() ?? "");
        pages = Ui.Input(metadata, "Page count", editingEdition.PageCount?.ToString() ?? "");
        var holdings = new WrapPanel(); physical = Check(holdings, "Physical", false); ebook = Check(holdings, "Ebook", false); audio = Check(holdings, "Audiobook", false); metadata.Children.Add(holdings);
        metadata.Children.Add(Ui.Text("Ownership is independent of reading status. Leave all unchecked for a borrowed or unowned book.", 12, "MutedBrush"));
        metadata.Children.Add(Ui.Button("Choose cover image…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Cover image|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(this) == true)
            {
                editingEdition.CoverFile = store.ImportCover(dialog.FileName);
                pendingCovers.Remove(editingEdition.Id);
                changed = true;
                MessageBox.Show(this, "Cover copied into this local profile.", "Cover selected");
            }
        }));
        metadata.Children.Add(Ui.Text("The selected edition is used for the next reading. Existing readings keep their edition.", 12, "MutedBrush"));
        AddTab(tabs, "Book & editions", metadata); LoadEdition();
        AddTab(tabs, "Reading & history", readingPanel); RenderReading();
        var writing = new StackPanel { Margin = new Thickness(10) };
        rating = Ui.Input(writing, "Rating (whole stars 1–5; blank means unrated)", book.Rating?.ToString() ?? "");
        review = Ui.Input(writing, "Review", book.Review, true); notes = Ui.Input(writing, "Private notes", book.Notes, true);
        AddTab(tabs, "Review & notes", writing);
        var references = new StackPanel { Margin = new Thickness(10) };
        var savedCitations = store.ReadEpubBibliography(book.Id);
        var citationActions = new WrapPanel();
        var extracted = new StackPanel();
        void RenderExtracted()
        {
            extracted.Children.Clear();
            if (savedCitations == null) return;
            var preview = Ui.Input(extracted, $"Bibliography Markdown · {savedCitations.Entries.Length} extracted citations",
                savedCitations.ToMarkdown(), true);
            preview.IsReadOnly = true;
            preview.Height = 340;
        }
        var browse = Ui.Button("Browse EPUB citations", () =>
        {
            if (savedCitations != null) new EpubBibliographyWindow(store, book, savedCitations) { Owner = this }.ShowDialog();
        });
        browse.IsEnabled = savedCitations != null;
        citationActions.Children.Add(browse);
        if (book.Calibre is { } epubSource && epubSource.Metadata.Files.FirstOrDefault(file => file.Format.Equals("EPUB", StringComparison.OrdinalIgnoreCase)) is { } epubFile)
        {
            citationActions.Children.Add(Ui.Button("Extract bibliography from EPUB", () =>
            {
                var path = CalibreImporter.ResolvePath(epubSource.LibraryFolder, epubFile.RelativePath);
                if (!File.Exists(path)) throw new IOException("The EPUB is unavailable. Refresh the Calibre connection.");
                var citations = EpubBibliographyReader.Read(path!);
                store.SaveEpubBibliography(book.Id, citations);
                savedCitations = citations;
                browse.IsEnabled = true;
                RenderExtracted();
            }));
        }
        references.Children.Add(citationActions);
        references.Children.Add(extracted);
        RenderExtracted();
        bibliography = Ui.Input(references, "Personal bibliography Markdown", originalBibliography.Text, true);
        references.Children.Add(Ui.Text(store.BibliographyPath(book.Id), 12, "MutedBrush"));
        AddTab(tabs, "Bibliography", references);
        if (book.Calibre is { } calibre) AddTab(tabs, "Calibre", CalibreDetails(calibre));
        editionPicker.SelectionChanged += (_, _) =>
        {
            if (switchingEdition || editionPicker.SelectedItem is not Edition selected) return;
            try { CaptureEdition(); editingEdition = selected; LoadEdition(); changed = true; }
            catch (Exception ex) { switchingEdition = true; editionPicker.SelectedItem = editingEdition; switchingEdition = false; MessageBox.Show(this, ex.Message); }
        };
        root.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!initializing) changed = true; }));
        initializing = false;
        Closing += (_, args) =>
        {
            if (!allowClose && changed && MessageBox.Show(this, "Discard unsaved edits in this window? Previously saved reading actions are kept.", "Unsaved edits", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) args.Cancel = true;
        };
    }

    private CheckBox Check(Panel panel, string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.Click += (_, _) => { if (!initializing) changed = true; }; panel.Children.Add(box); return box;
    }

    private static void AddTab(TabControl tabs, string title, UIElement content) => tabs.Items.Add(new TabItem { Header = title, Content = Ui.Scroll(content) });

    private void DownloadMetadata()
    {
        var seedIsbn = isbn.Text.Trim();
        var seedTitle = title.Text.Trim();
        if (seedIsbn.Length == 0 && OpenLibraryLookup.NormalizeIsbn(seedTitle) is { } titleIsbn)
        {
            seedIsbn = titleIsbn;
            seedTitle = "";
        }
        var lookup = new MetadataLookupWindow(seedTitle, author.Text.Trim(), seedIsbn, editBook: true,
            hasCover: store.CoverPath(editingEdition) != null || pendingCovers.ContainsKey(editingEdition.Id), store: store) { Owner = this };
        if (lookup.ShowDialog() != true || lookup.SelectedMatch is not { } match) return;
        if (lookup.CoverOnly && lookup.SelectedCover is { } cover)
        {
            pendingCovers[editingEdition.Id] = cover;
            changed = true;
            return;
        }
        ApplyMetadata(match, lookup.SelectedEdition, lookup.SelectedCover);
    }

    public void ApplyMetadata(MetadataMatch match, EditionMetadata? edition, byte[]? cover)
    {
        title.Text = match.Title;
        Title = match.Title;
        if (match.Author.Length > 0) author.Text = match.Author;
        if (match.OriginalYear is { } originalYear) year.Text = originalYear.ToString();
        if (match.WorkId != null) book.OpenLibraryWorkId = match.WorkId;
        if (edition != null)
        {
            if (edition.Isbn.Length > 0) isbn.Text = edition.Isbn;
            if (!string.IsNullOrWhiteSpace(edition.Publisher)) publisher.Text = edition.Publisher.Trim();
            if (edition.Year is { } published) editionYear.Text = published.ToString();
            if (edition.Pages is { } count) pages.Text = count.ToString();
            editingEdition.OpenLibraryEditionId = match.EditionId;
        }
        if (cover != null) pendingCovers[editingEdition.Id] = cover;
        changed = true;
    }

    private static UIElement CalibreDetails(CalibreReference source)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        var metadata = source.Metadata;
        panel.Children.Add(Ui.Heading("From Calibre", 23));
        if (metadata.Publisher.Length > 0) panel.Children.Add(Ui.Text("Publisher · " + metadata.Publisher));
        if (metadata.Series.Length > 0)
            panel.Children.Add(Ui.Text($"Series · {metadata.Series}{(metadata.SeriesIndex is { } index ? $" #{index:g}" : "")}"));
        if (metadata.Tags.Length > 0) panel.Children.Add(Ui.Text("Calibre tags · " + string.Join(", ", metadata.Tags)));
        if (metadata.Identifiers.Length > 0) panel.Children.Add(Ui.Text(string.Join("\n", metadata.Identifiers)));
        panel.Children.Add(Ui.Heading("Ebook files", 20));
        foreach (var file in metadata.Files)
        {
            var path = CalibreImporter.ResolvePath(source.LibraryFolder, file.RelativePath);
            if (File.Exists(path))
            {
                var canOpen = Path.GetExtension(path).ToLowerInvariant() is ".epub" or ".pdf" or ".mobi" or ".azw3" or ".azw" or ".djvu";
                panel.Children.Add(Ui.Button(canOpen ? $"Open {file.Format}" : $"Show {file.Format} folder", () =>
                {
                    if (!File.Exists(path)) throw new IOException("This ebook moved or is unavailable. Refresh the Calibre connection.");
                    Process.Start(new ProcessStartInfo(canOpen ? path! : Path.GetDirectoryName(path)!) { UseShellExecute = true });
                }));
            }
            else panel.Children.Add(Ui.Text($"{file.Format} file unavailable. Refresh the Calibre connection after it is restored.", 13, "MutedBrush"));
        }
        if (metadata.Files.Length == 0) panel.Children.Add(Ui.Text("Calibre lists no ebook files for this record.", 13, "MutedBrush"));
        if (metadata.Description.Length > 0)
        {
            panel.Children.Add(Ui.Heading("Description", 20));
            panel.Children.Add(Ui.Text(metadata.Description));
        }
        return panel;
    }
    private static int? OptionalInt(string input, string label)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (!int.TryParse(input, out var value)) throw new InvalidOperationException(label + " must be a whole number or blank.");
        return value;
    }
    private static DateOnly? OptionalDate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (!DateOnly.TryParseExact(input.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidOperationException("Use YYYY-MM-DD or leave the date blank for undated history.");
        return date;
    }

    private void CaptureEdition()
    {
        // Parse first so an invalid field does not partly update the edition.
        var pageCount = OptionalInt(pages.Text, "Page count"); var publication = OptionalInt(editionYear.Text, "Edition year");
        if (pageCount is <= 0 || publication == 0) throw new InvalidOperationException("Page count must be positive; edition year cannot be zero.");
        editingEdition.Label = string.IsNullOrWhiteSpace(editionLabel.Text) ? "Default edition" : editionLabel.Text.Trim();
        editingEdition.Isbn = isbn.Text.Trim(); editingEdition.Language = language.Text.Trim();
        var publisherName = publisher.Text.Trim();
        if (publisherName.Length > 0 || editingEdition.Publisher != null) editingEdition.Publisher = publisherName;
        editingEdition.PageCount = pageCount; editingEdition.PublicationYear = publication;
        editingEdition.Holdings = [];
        if (physical.IsChecked == true) editingEdition.Holdings.Add("Physical");
        if (ebook.IsChecked == true) editingEdition.Holdings.Add("Ebook");
        if (audio.IsChecked == true) editingEdition.Holdings.Add("Audiobook");
        book.SelectedEditionId = editingEdition.Id;
    }

    private void LoadEdition()
    {
        editionLabel.Text = editingEdition.Label; isbn.Text = editingEdition.Isbn; language.Text = editingEdition.Language;
        publisher.Text = editingEdition.Publisher ?? "";
        editionYear.Text = editingEdition.PublicationYear?.ToString() ?? ""; pages.Text = editingEdition.PageCount?.ToString() ?? "";
        physical.IsChecked = editingEdition.Holdings.Contains("Physical"); ebook.IsChecked = editingEdition.Holdings.Contains("Ebook"); audio.IsChecked = editingEdition.Holdings.Contains("Audiobook");
    }

    private void CaptureMetadata()
    {
        CaptureEdition(); book.Title = title.Text; book.Author = author.Text; book.OriginalYear = OptionalInt(year.Text, "Original year");
        book.Shelves = shelves.Text.Split(',').ToList(); book.Rating = OptionalInt(rating.Text, "Rating");
        book.Review = review.Text; book.Notes = notes.Text; book.Excluded = excluded.IsChecked == true;
        book.ReadNext = readNext.IsChecked == true && book.Status == ReadingStatus.ToRead;
        foreach (var (id, start, finish) in readingDates)
        {
            var reading = book.Readings.Single(r => r.Id == id);
            reading.Started = OptionalDate(start.Text); reading.Finished = OptionalDate(finish.Text);
        }
    }

    private void SaveAll()
    {
        CaptureMetadata();
        if (bibliography.Text != originalBibliography.Text && store.Bibliography(book.Id).Hash != originalBibliography.Hash)
            throw new InvalidOperationException("The bibliography changed outside this editor. Copy your draft and reopen the book before saving. No book changes were saved.");
        ReadingRules.Validate(book);
        foreach (var edition in book.Editions)
            if (pendingCovers.TryGetValue(edition.Id, out var cover)) edition.CoverFile = store.ImportJpegCover(cover);
        store.Save(book);
        pendingCovers.Clear();
        if (bibliography.Text != originalBibliography.Text) originalBibliography = store.SaveBibliography(book.Id, bibliography.Text, originalBibliography.Hash);
        changed = false; Title = book.Title;
    }

    private void ReadingAction(Action<Book> action)
    {
        // Validate the operation before saving any metadata or replacing the draft.
        CaptureMetadata(); var candidate = LibraryStore.Clone(book); action(candidate); ReadingRules.Validate(candidate);
        SaveAll();
        book.Readings = candidate.Readings; book.Status = candidate.Status; book.ReadNext = candidate.ReadNext;
        book.ToReadAddedAt = candidate.ToReadAddedAt;
        store.Save(book); readNext.IsChecked = book.ReadNext; RenderReading(); changed = false;
    }

    private void RenderReading()
    {
        readingDates.Clear(); readingPanel.Children.Clear(); readingPanel.Margin = new Thickness(10);
        readingPanel.Children.Add(Ui.Heading(ReadingRules.Label(book.Status), 25));
        readingPanel.Children.Add(Ui.Text("Reading actions save immediately. You can start now and log progress later. Blank dates remain undated.", 13, "MutedBrush"));
        var date = Ui.Input(readingPanel, "Action date (YYYY-MM-DD; blank for unknown)", DateTime.Today.ToString("yyyy-MM-dd"));
        var actions = new WrapPanel();
        if (book.ActiveReading == null)
        {
            actions.Children.Add(Ui.Button(book.Readings.Count > 0 ? "Start / reread" : "Start reading", () => ReadingAction(b => ReadingRules.Start(b, OptionalDate(date.Text))), true));
            actions.Children.Add(Ui.Button("Record completed reading", () => ReadingAction(b => { var completed = OptionalDate(date.Text); ReadingRules.Start(b, null); ReadingRules.Finish(b, completed, ReadingOutcome.Completed); })));
            actions.Children.Add(Ui.Button("Mark to read", () => ReadingAction(b => ReadingRules.Classify(b, ReadingStatus.ToRead))));
            actions.Children.Add(Ui.Button("Reference only", () => ReadingAction(b => ReadingRules.Classify(b, ReadingStatus.ReferenceOnly))));
            if (book.Status != ReadingStatus.Unclassified)
                actions.Children.Add(Ui.Button("Mark unclassified", () => ReadingAction(b => ReadingRules.Classify(b, ReadingStatus.Unclassified))));
        }
        else
        {
            readingPanel.Children.Add(Ui.Text(ReadingRules.ProgressText(book), 17));
            var last = book.ActiveReading.Progress.LastOrDefault();
            var value = Ui.Input(readingPanel, "Progress (optional until you save an update)", (last?.Value ?? 0).ToString(CultureInfo.CurrentCulture));
            var unit = new ComboBox { ItemsSource = new[] { "Page", "Percent" }, SelectedIndex = last?.Unit == ProgressUnit.Percent ? 1 : 0 }; readingPanel.Children.Add(unit);
            readingPanel.Children.Add(Ui.Button("Save progress", () =>
            {
                if (!decimal.TryParse(value.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) throw new InvalidOperationException("Enter a page number or percentage.");
                ReadingAction(b => ReadingRules.Progress(b, amount, unit.SelectedIndex == 0 ? ProgressUnit.Page : ProgressUnit.Percent));
            }, true));
            actions.Children.Add(Ui.Button("Finish book", () => ReadingAction(b => ReadingRules.Finish(b, OptionalDate(date.Text), ReadingOutcome.Completed)), true));
            actions.Children.Add(Ui.Button("Partially read", () => ReadingAction(b => ReadingRules.Finish(b, OptionalDate(date.Text), ReadingOutcome.PartiallyRead))));
            actions.Children.Add(Ui.Button("Did not finish", () => ReadingAction(b => ReadingRules.Finish(b, OptionalDate(date.Text), ReadingOutcome.DidNotFinish))));
        }
        readingPanel.Children.Add(actions); readingPanel.Children.Add(Ui.Heading("Reading history", 23));
        if (book.Readings.Count == 0) readingPanel.Children.Add(Ui.Text("No reading records yet.", 13, "MutedBrush"));
        foreach (var reading in book.Readings.AsEnumerable().Reverse())
        {
            var entry = new StackPanel();
            entry.Children.Add(Ui.Text($"{ReadingRules.OutcomeLabel(reading.Outcome)} · {book.Editions.Single(e => e.Id == reading.EditionId).Label}", 16));
            var start = Ui.Input(entry, "Started (YYYY-MM-DD or blank)", reading.Started?.ToString("yyyy-MM-dd") ?? "");
            var finish = Ui.Input(entry, "Finished (YYYY-MM-DD or blank)", reading.Finished?.ToString("yyyy-MM-dd") ?? "");
            readingDates.Add((reading.Id, start, finish));
            entry.Children.Add(Ui.Button("Save dates", () => ReadingAction(b =>
            {
                var item = b.Readings.Single(r => r.Id == reading.Id); item.Started = OptionalDate(start.Text); item.Finished = OptionalDate(finish.Text);
            })));
            foreach (var progress in reading.Progress.AsEnumerable().Reverse().Take(20))
                entry.Children.Add(Ui.Text($"{progress.RecordedAt:g} · {progress.Value:0.##} {progress.Unit}", 12, "MutedBrush"));
            if (reading.Progress.Count > 20) entry.Children.Add(Ui.Text($"Showing latest 20 of {reading.Progress.Count} saved updates.", 12, "MutedBrush"));
            readingPanel.Children.Add(Ui.Card(entry));
        }
    }
}
