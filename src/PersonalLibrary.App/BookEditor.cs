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
    private readonly TextBox title, author, year, shelves, groups, rating, review, notes, bibliography;
    private readonly TextBox editionLabel, isbn, publisher, language, editionYear, pages;
    private readonly TextBox msrp;
    private readonly CheckBox physical, ebook, audio, excluded;
    private readonly ComboBox editionPicker;
    private readonly ComboBox msrpCurrency;
    private readonly StackPanel copyFields = new();
    private readonly TextBlock msrpProvenance = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<PurchaseFields> copyEditors = [];
    private readonly string[] vendorNames;
    private PurchaseFields? legacyEditor;
    private readonly StackPanel readingPanel = new();
    private readonly Dictionary<Guid, byte[]> pendingCovers = [];
    private Edition editingEdition;
    private BibliographyText originalBibliography;
    private bool switchingEdition;
    private bool changed;
    private bool allowClose;
    private bool initializing = true;
    private readonly List<(Guid Id, TextBox Start, TextBox Finish)> readingDates = [];

    public BookEditor(LibraryStore store, Book source, Guid? readingToShow = null)
    {
        this.store = store; book = LibraryStore.Clone(source); editingEdition = book.SelectedEdition;
        vendorNames = store.Books().SelectMany(saved => saved.Editions)
            .SelectMany(edition => edition.PhysicalCopies.Select(copy => copy.PurchasedFrom).Prepend(edition.PurchasedFrom))
            .OfType<string>().Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
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
        ShelfSuggestions.Attach(shelves, store.Books().SelectMany(savedBook => savedBook.Shelves));
        groups = Ui.Input(metadata, "To-read groups (comma separated)", string.Join(", ", book.ToReadGroups));
        ShelfSuggestions.Attach(groups, store.LibraryNames(groups: true));
        groups.ToolTip = "Create your own groups, for example Research for a book. A book can belong to several groups. Clear this field to remove all its groups.";
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
        metadata.Children.Add(Ui.Heading("Purchase details", 20));
        metadata.Children.Add(Ui.Text("Use a separate entry for each physical copy of this edition.", 12, "MutedBrush"));
        var purchaseFields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        metadata.Children.Add(purchaseFields);
        (msrp, msrpCurrency) = PurchaseAmount(purchaseFields, "MSRP");
        metadata.Children.Add(msrpProvenance);
        metadata.Children.Add(copyFields);
        metadata.Children.Add(Ui.Button("+ Add physical copy", () =>
        {
            CaptureEdition(promotePhysical: true);
            editingEdition.PhysicalCopies.Add(new PhysicalCopy());
            editingEdition.UpdatePhysicalHolding();
            LoadPurchases();
            changed = true;
            if (copyFields.Children[copyFields.Children.Count - 1] is FrameworkElement addedCopy)
                addedCopy.BringIntoView();
        }));
        metadata.Children.Add(Ui.Button("Choose cover image…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Cover image|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(this) == true)
            {
                editingEdition.CoverFile = store.ImportCover(dialog.FileName);
                editingEdition.CoverSource = null;
                editingEdition.CoverSourceIsbn = null;
                editingEdition.CoverIsAlternateEdition = false;
                editingEdition.CoverSource = null;
                editingEdition.CoverSourceIsbn = null;
                editingEdition.CoverIsAlternateEdition = false;
                pendingCovers.Remove(editingEdition.Id);
                changed = true;
                MessageBox.Show(this, "Cover copied into this local profile.", "Cover selected");
            }
        }));
        metadata.Children.Add(Ui.Text("The selected edition is used for the next reading. Existing readings keep their edition.", 12, "MutedBrush"));
        AddTab(tabs, "Book & editions", metadata); LoadEdition();
        AddTab(tabs, "Reading & history", readingPanel); RenderReading();
        if (readingToShow.HasValue)
        {
            tabs.SelectedIndex = 1;
            var entry = readingPanel.Children.OfType<Border>().FirstOrDefault(card => Equals(card.Tag, readingToShow.Value));
            if (entry != null)
            {
                entry.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                entry.BorderThickness = new Thickness(2);
                Loaded += (_, _) => entry.BringIntoView();
            }
        }
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
        var online = Ui.Button("Browse online bibliography", () =>
        {
            if (store.ReadOnlineBibliography(book.Id) is { } saved)
                new EpubBibliographyWindow(store, book, saved) { Owner = this }.ShowDialog();
        });
        online.IsEnabled = store.HasOnlineBibliography(book.Id);
        citationActions.Children.Add(online);
        citationActions.Children.Add(Ui.Button("Find Bibliography Online", () =>
        {
            var source = store.Books().Find(saved => saved.Id == book.Id)
                ?? throw new InvalidOperationException("Save the book before searching for a bibliography.");
            if (new PublicBibliographyWindow(store, source) { Owner = this }.ShowDialog() == true)
                online.IsEnabled = true;
        }));
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

    private (TextBox Amount, ComboBox Currency) PurchaseAmount(Panel fields, string label)
    {
        var field = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        fields.Children.Add(field);
        var title = Ui.Text(label, 12, "MutedBrush");
        DockPanel.SetDock(title, Dock.Top);
        field.Children.Add(title);
        var currency = new ComboBox
        {
            ItemsSource = Enum.GetValues<PurchaseCurrency>(),
            Width = 80,
            Margin = new Thickness(6, 0, 0, 8)
        };
        System.Windows.Automation.AutomationProperties.SetName(currency, $"{label} currency");
        currency.SelectionChanged += (_, _) => { if (!initializing) changed = true; };
        DockPanel.SetDock(currency, Dock.Right);
        field.Children.Add(currency);
        var amount = new TextBox { VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(amount, label);
        field.Children.Add(amount);
        return (amount, currency);
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
            editingEdition.CoverSource = match.Cover?.AbsoluteUri;
            editingEdition.CoverSourceIsbn = lookup.SelectedEdition?.Isbn;
            editingEdition.CoverIsAlternateEdition = true;
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
        if (cover != null)
        {
            pendingCovers[editingEdition.Id] = cover;
            editingEdition.CoverSource = match.Cover?.AbsoluteUri;
            editingEdition.CoverSourceIsbn = edition?.Isbn;
            editingEdition.CoverIsAlternateEdition = false;
        }
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

    private static void ApplyReadingDates(Reading reading, string start, string finish)
    {
        reading.Started = OptionalDate(start);
        var value = finish.Trim();
        if (reading.Outcome == ReadingOutcome.Completed && value.Length == 4 &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1 and <= 9999)
        {
            reading.Finished = null;
            reading.ApproximateFinishYear = year;
        }
        else
        {
            reading.Finished = OptionalDate(value);
            reading.ApproximateFinishYear = null;
        }
    }

    private void CaptureEdition(bool promotePhysical = false)
    {
        // Parse first so an invalid field does not partly update the edition.
        var pageCount = OptionalInt(pages.Text, "Page count"); var publication = OptionalInt(editionYear.Text, "Edition year");
        if (pageCount is <= 0 || publication == 0) throw new InvalidOperationException("Page count must be positive; edition year cannot be zero.");
        var retail = ReadAmount(msrp.Text, "MSRP");
        var legacy = legacyEditor?.Read() ?? editingEdition.LegacyPurchase();
        var copies = copyEditors.Select(editor => editor.Read()).ToList();
        var migrate = copies.Count == 0 &&
            (legacy.Sold || legacy.IsLibraryLoan || legacy.PurchaseYear.HasValue || (physical.IsChecked == true && promotePhysical));
        if (migrate)
        {
            copies.Add(legacy);
            legacy = new PhysicalCopy();
        }
        editingEdition.Label = string.IsNullOrWhiteSpace(editionLabel.Text) ? "Default edition" : editionLabel.Text.Trim();
        editingEdition.Isbn = isbn.Text.Trim(); editingEdition.Language = language.Text.Trim();
        var publisherName = publisher.Text.Trim();
        if (publisherName.Length > 0 || editingEdition.Publisher != null) editingEdition.Publisher = publisherName;
        editingEdition.PageCount = pageCount; editingEdition.PublicationYear = publication;
        var retailCurrency = msrpCurrency.SelectedItem is PurchaseCurrency selectedCurrency ? selectedCurrency : PurchaseCurrency.CAD;
        if (editingEdition.Msrp != retail || editingEdition.MsrpCurrency != retailCurrency)
        {
            editingEdition.MsrpSource = null;
            editingEdition.MsrpSourceIsbn = null;
            editingEdition.MsrpIsEstimated = false;
        }
        editingEdition.Msrp = retail;
        editingEdition.MsrpCurrency = retailCurrency;
        editingEdition.SetLegacyPurchase(legacy);
        editingEdition.PhysicalCopies = copies;
        editingEdition.Holdings = [];
        if (physical.IsChecked == true) editingEdition.Holdings.Add("Physical");
        if (ebook.IsChecked == true) editingEdition.Holdings.Add("Ebook");
        if (audio.IsChecked == true) editingEdition.Holdings.Add("Audiobook");
        editingEdition.UpdatePhysicalHolding();
        book.SelectedEditionId = editingEdition.Id;
        if (migrate && !promotePhysical) LoadPurchases();
    }

    private void LoadEdition()
    {
        editionLabel.Text = editingEdition.Label; isbn.Text = editingEdition.Isbn; language.Text = editingEdition.Language;
        publisher.Text = editingEdition.Publisher ?? "";
        editionYear.Text = editingEdition.PublicationYear?.ToString() ?? ""; pages.Text = editingEdition.PageCount?.ToString() ?? "";
        msrp.Text = editingEdition.Msrp?.ToString("0.##") ?? "";
        msrpCurrency.SelectedItem = editingEdition.MsrpCurrency;
        msrpProvenance.Text = editingEdition.MsrpSource is { } source
            ? $"{(editingEdition.MsrpIsEstimated ? "Estimated MSRP from a comparable edition" : "Edition list price")} · ISBN {editingEdition.MsrpSourceIsbn}\n{source}"
            : "";
        physical.IsChecked = editingEdition.Holdings.Contains("Physical"); ebook.IsChecked = editingEdition.Holdings.Contains("Ebook"); audio.IsChecked = editingEdition.Holdings.Contains("Audiobook");
        LoadPurchases();
    }

    private void LoadPurchases()
    {
        copyFields.Children.Clear();
        copyEditors.Clear();
        legacyEditor = null;
        var copies = editingEdition.PhysicalCopies;
        physical.IsEnabled = copies.Count == 0;
        if (copies.Count > 0) physical.IsChecked = copies.Any(copy => !copy.Sold && !copy.IsLibraryLoan);
        if (copies.Count == 0 || editingEdition.HasLegacyPurchase)
        {
            var fields = new StackPanel();
            if (copies.Count > 0) fields.Children.Add(Ui.Heading("Other purchase", 17));
            copyFields.Children.Add(fields);
            legacyEditor = new PurchaseFields(this, fields, editingEdition.LegacyPurchase(), copies.Count == 0);
        }
        foreach (var copy in copies)
        {
            var fields = new StackPanel();
            fields.Children.Add(Ui.Heading($"Physical copy {copyEditors.Count + 1}", 17));
            var editor = new PurchaseFields(this, fields, copy, true);
            copyEditors.Add(editor);
            copyFields.Children.Add(Ui.Card(fields));
        }
    }

    private sealed class PurchaseFields
    {
        private readonly TextBox price;
        private readonly ComboBox vendor, currency, condition;
        private readonly CheckBox gift, libraryLoan;
        private readonly TextBox? year, sale;
        private readonly ComboBox? saleCurrency;
        private readonly CheckBox? sold;

        public PurchaseFields(BookEditor owner, StackPanel parent, PhysicalCopy copy, bool canTrackPhysical)
        {
            var fields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            parent.Children.Add(fields);
            (price, currency) = owner.PurchaseAmount(fields, "Price purchased");
            price.Text = copy.PurchasePrice?.ToString("0.##") ?? "";
            currency.SelectedItem = copy.PaidCurrency;
            var vendorField = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            fields.Children.Add(vendorField);
            vendorField.Children.Add(Ui.Text("Purchased from", 12, "MutedBrush"));
            vendor = new ComboBox
            {
                IsEditable = true, IsTextSearchEnabled = false, StaysOpenOnEdit = true,
                ItemsSource = owner.vendorNames, Text = copy.PurchasedFrom ?? ""
            };
            System.Windows.Automation.AutomationProperties.SetName(vendor, "Purchased from");
            vendor.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, args) =>
            {
                if (!vendor.IsKeyboardFocusWithin) args.Handled = true;
            }));
            vendor.SelectionChanged += (_, _) => { if (!owner.initializing) owner.changed = true; };
            var attached = false;
            vendor.Loaded += (_, _) =>
            {
                if (attached || vendor.Template.FindName("PART_EditableTextBox", vendor) is not TextBox input) return;
                ShelfSuggestions.Attach(input, owner.vendorNames, commaSeparated: false);
                attached = true;
            };
            vendorField.Children.Add(vendor);
            var conditionField = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            fields.Children.Add(conditionField);
            conditionField.Children.Add(Ui.Text("Purchased new or used", 12, "MutedBrush"));
            condition = new ComboBox
            {
                ItemsSource = new[] { "Unspecified", "New", "Used" },
                SelectedIndex = copy.PurchaseCondition switch { PurchaseCondition.New => 1, PurchaseCondition.Used => 2, _ => 0 }
            };
            System.Windows.Automation.AutomationProperties.SetName(condition, "Purchased new or used");
            condition.SelectionChanged += (_, _) => owner.changed = true;
            conditionField.Children.Add(condition);
            gift = owner.Check(parent, "Given to me as a gift", copy.IsGift);
            libraryLoan = owner.Check(parent, "Library (borrowed copy)", copy.IsLibraryLoan);
            price.IsEnabled = currency.IsEnabled = !copy.IsGift && !copy.IsLibraryLoan;
            Action updateOwnership = () => { };
            void UpdateAcquisition()
            {
                price.IsEnabled = currency.IsEnabled = gift.IsChecked != true && libraryLoan.IsChecked != true;
                if (sold != null)
                {
                    if (libraryLoan.IsChecked == true) sold.IsChecked = false;
                    sold.IsEnabled = libraryLoan.IsChecked != true;
                }
                updateOwnership();
            }
            gift.Click += (_, _) =>
            {
                if (gift.IsChecked == true) libraryLoan.IsChecked = false;
                UpdateAcquisition();
            };
            libraryLoan.Click += (_, _) =>
            {
                if (libraryLoan.IsChecked == true) gift.IsChecked = false;
                UpdateAcquisition();
            };
            if (!canTrackPhysical) return;

            var yearField = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            fields.Children.Add(yearField);
            year = Ui.Input(yearField, "Physical copy purchase year (optional)", copy.PurchaseYear?.ToString() ?? "");
            sold = owner.Check(parent, "Sold physical copy", copy.Sold);
            sold.IsEnabled = !copy.IsLibraryLoan;
            var saleFields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, IsEnabled = copy.Sold && !copy.IsLibraryLoan };
            parent.Children.Add(saleFields);
            (sale, saleCurrency) = owner.PurchaseAmount(saleFields, "Sold for (optional)");
            sale.Text = copy.SoldFor?.ToString("0.##") ?? "";
            saleCurrency.SelectedItem = copy.SaleCurrency;
            var ownedBeforeSale = owner.physical.IsChecked == true;
            var restricted = false;
            sold.Click += (_, _) => UpdateOwnership();
            updateOwnership = UpdateOwnership;

            void UpdateOwnership()
            {
                saleFields.IsEnabled = sold.IsChecked == true && libraryLoan.IsChecked != true;
                if (owner.copyEditors.Count > 0)
                {
                    owner.physical.IsChecked = owner.copyEditors.Any(editor => editor.sold?.IsChecked != true && editor.libraryLoan.IsChecked != true);
                }
                else if (sold.IsChecked == true || libraryLoan.IsChecked == true)
                {
                    if (!restricted) ownedBeforeSale = owner.physical.IsChecked == true;
                    restricted = true;
                    owner.physical.IsChecked = false;
                    owner.physical.IsEnabled = false;
                }
                else if (restricted)
                {
                    owner.physical.IsChecked = ownedBeforeSale;
                    owner.physical.IsEnabled = true;
                    restricted = false;
                }
            }

        }

        public PhysicalCopy Read()
        {
            var purchaseYear = OptionalInt(year?.Text ?? "", "Purchase year");
            if (purchaseYear is < 1 or > 9999) throw new InvalidOperationException("Enter a purchase year from 1 to 9999, or leave it blank.");
            var isSold = sold?.IsChecked == true;
            return new PhysicalCopy
            {
                PurchaseYear = purchaseYear,
                PurchasePrice = ReadAmount(price.Text, "Price purchased"),
                PaidCurrency = (PurchaseCurrency)currency.SelectedItem,
                PurchasedFrom = string.IsNullOrWhiteSpace(vendor.Text) ? null : vendor.Text.Trim(),
                IsGift = gift.IsChecked == true,
                IsLibraryLoan = libraryLoan.IsChecked == true,
                PurchaseCondition = condition.SelectedIndex switch { 1 => PurchaseCondition.New, 2 => PurchaseCondition.Used, _ => null },
                Sold = isSold,
                SoldFor = isSold ? ReadAmount(sale!.Text, "Sold for") : null,
                SaleCurrency = saleCurrency?.SelectedItem is PurchaseCurrency enteredCurrency ? enteredCurrency : PurchaseCurrency.CAD
            };
        }
    }

    private static decimal? ReadAmount(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.CurrentCulture, out var amount))
            throw new InvalidOperationException($"{label} must be an amount or blank.");
        PurchaseStatistics.ValidateAmount(amount, label);
        return amount;
    }

    private void CaptureMetadata()
    {
        CaptureEdition(); book.Title = title.Text; book.Author = author.Text; book.OriginalYear = OptionalInt(year.Text, "Original year");
        book.Shelves = shelves.Text.Split(',').ToList(); book.Rating = OptionalInt(rating.Text, "Rating");
        book.ToReadGroups = groups.Text.Split(',').ToList();
        book.Review = review.Text; book.Notes = notes.Text; book.Excluded = excluded.IsChecked == true;
        foreach (var (id, start, finish) in readingDates)
        {
            var reading = book.Readings.Single(r => r.Id == id);
            ApplyReadingDates(reading, start.Text, finish.Text);
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
        store.Save(book); RenderReading(); changed = false;
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
            foreach (var category in new[] { ReadingStatus.MusicBook, ReadingStatus.PhotographyArtBook })
                if (book.Status != category)
                    actions.Children.Add(Ui.Button(ReadingRules.Label(category), () => ReadingAction(b => ReadingRules.Classify(b, category))));
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
            var finishLabel = reading.Outcome == ReadingOutcome.Completed ? "Finished (YYYY-MM-DD, YYYY, or blank)" : "Finished (YYYY-MM-DD or blank)";
            var finish = Ui.Input(entry, finishLabel, reading.Finished?.ToString("yyyy-MM-dd") ??
                reading.ApproximateFinishYear?.ToString("D4", CultureInfo.InvariantCulture) ?? "");
            readingDates.Add((reading.Id, start, finish));
            entry.Children.Add(Ui.Button("Save dates", () => ReadingAction(b =>
            {
                var item = b.Readings.Single(r => r.Id == reading.Id);
                ApplyReadingDates(item, start.Text, finish.Text);
            })));
            foreach (var progress in reading.Progress.AsEnumerable().Reverse().Take(20))
                entry.Children.Add(Ui.Text($"{progress.RecordedAt:g} · {progress.Value:0.##} {progress.Unit}", 12, "MutedBrush"));
            if (reading.Progress.Count > 20) entry.Children.Add(Ui.Text($"Showing latest 20 of {reading.Progress.Count} saved updates.", 12, "MutedBrush"));
            var card = Ui.Card(entry);
            card.Tag = reading.Id;
            readingPanel.Children.Add(card);
        }
    }
}
