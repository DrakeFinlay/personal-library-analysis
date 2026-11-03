using System.Globalization;
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
    private readonly TextBox editionLabel, isbn, language, editionYear, pages;
    private readonly CheckBox physical, ebook, audio, readNext, excluded;
    private readonly ComboBox editionPicker;
    private readonly StackPanel readingPanel = new();
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
        language = Ui.Input(metadata, "Language", editingEdition.Language);
        editionYear = Ui.Input(metadata, "Edition publication year", editingEdition.PublicationYear?.ToString() ?? "");
        pages = Ui.Input(metadata, "Page count", editingEdition.PageCount?.ToString() ?? "");
        var holdings = new WrapPanel(); physical = Check(holdings, "Physical", false); ebook = Check(holdings, "Ebook", false); audio = Check(holdings, "Audiobook", false); metadata.Children.Add(holdings);
        metadata.Children.Add(Ui.Text("Ownership is independent of reading status. Leave all unchecked for a borrowed or unowned book.", 12, "MutedBrush"));
        metadata.Children.Add(Ui.Button("Choose cover image…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Cover image|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(this) == true) { editingEdition.CoverFile = store.ImportCover(dialog.FileName); changed = true; MessageBox.Show(this, "Cover copied into this local profile.", "Cover selected"); }
        }));
        metadata.Children.Add(Ui.Text("The selected edition is used for the next reading. Existing readings keep their edition.", 12, "MutedBrush"));
        AddTab(tabs, "Book & editions", metadata); LoadEdition();
        AddTab(tabs, "Reading & history", readingPanel); RenderReading();
        var writing = new StackPanel { Margin = new Thickness(10) };
        rating = Ui.Input(writing, "Rating (whole stars 1–5; blank means unrated)", book.Rating?.ToString() ?? "");
        review = Ui.Input(writing, "Review", book.Review, true); notes = Ui.Input(writing, "Private notes", book.Notes, true);
        AddTab(tabs, "Review & notes", writing);
        var references = new StackPanel { Margin = new Thickness(10) };
        references.Children.Add(Ui.Text("Paste a bibliography as plain text or Markdown. Saving retains the initial paste and later revisions. Reference matching comes in Stage B.", 13, "MutedBrush"));
        bibliography = Ui.Input(references, "Bibliography Markdown", originalBibliography.Text, true); bibliography.MinHeight = 400;
        references.Children.Add(Ui.Text(store.BibliographyPath(book.Id), 12, "MutedBrush"));
        AddTab(tabs, "Bibliography", references);
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
        store.Save(book);
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
        readingPanel.Children.Add(Ui.Text("Reading actions save immediately. Blank dates remain undated; rereads create a separate reading record.", 13, "MutedBrush"));
        var date = Ui.Input(readingPanel, "Action date (YYYY-MM-DD; blank for unknown)", DateTime.Today.ToString("yyyy-MM-dd"));
        var actions = new WrapPanel();
        if (book.ActiveReading == null)
        {
            actions.Children.Add(Ui.Button(book.Readings.Count > 0 ? "Start / reread" : "Start reading", () => ReadingAction(b => ReadingRules.Start(b, OptionalDate(date.Text))), true));
            actions.Children.Add(Ui.Button("Record completed reading", () => ReadingAction(b => { var completed = OptionalDate(date.Text); ReadingRules.Start(b, null); ReadingRules.Finish(b, completed, ReadingOutcome.Completed); })));
            actions.Children.Add(Ui.Button("Mark to read", () => ReadingAction(b => { if (b.Status != ReadingStatus.ToRead) b.ToReadAddedAt = DateTimeOffset.Now; b.Status = ReadingStatus.ToRead; })));
            actions.Children.Add(Ui.Button("Reference only", () => ReadingAction(b => { b.Status = ReadingStatus.ReferenceOnly; b.ReadNext = false; })));
        }
        else
        {
            readingPanel.Children.Add(Ui.Text(ReadingRules.ProgressText(book), 17));
            var value = Ui.Input(readingPanel, "Progress value", "");
            var unit = new ComboBox { ItemsSource = new[] { "Page", "Percent" }, SelectedIndex = 0 }; readingPanel.Children.Add(unit);
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
