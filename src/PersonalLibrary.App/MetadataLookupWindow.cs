using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class MetadataLookupWindow : Window
{
    private readonly OpenLibraryLookup lookup = new();
    private CancellationTokenSource? request;
    private sealed record Candidate(MetadataMatch? Metadata, Book? Book, string Source)
    {
        public string Label => Book is { } book ? $"{book.Title} — {book.Author} · {Source}" : $"{Metadata!.Label} · {Source}";
    }

    public MetadataMatch? SelectedMatch { get; private set; }
    public EditionMetadata? SelectedEdition { get; private set; }
    public byte[]? SelectedCover { get; private set; }
    public Book? ExistingBook { get; private set; }
    public Book? ManualBook { get; private set; }
    public bool CoverOnly { get; private set; }

    public MetadataLookupWindow(string title, string author, string isbn = "", bool editBook = false, bool hasCover = false,
        LibraryStore? store = null, bool addBook = false, bool findCover = false)
    {
        Title = findCover ? "Find a cover" : addBook ? "Add a book" : editBook ? "Find metadata" : "Find this citation";
        var catalog = store == null ? null : new OfflineCatalog(store.Root);
        var books = addBook ? store!.Books().Where(book => !book.Excluded).ToList() : [];
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 830; Height = 750; MinWidth = 640; MinHeight = 630;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading(findCover ? "Find a cover" : addBook ? "Find a book" : "Find metadata"));
        header.Children.Add(Ui.Text("Use an ISBN for a specific edition, or search by title and author.", 13, "MutedBrush"));
        var titleInput = Ui.Input(header, "Title", title);
        var authorInput = Ui.Input(header, "Author", author.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? "" : author);
        var isbnInput = Ui.Input(header, "ISBN", isbn);
        foreach (var input in new[] { titleInput, authorInput, isbnInput }) input.MaxLength = 512;
        var search = Ui.Button(store != null ? "Search locally" : "Search Open Library", () => { }, true);
        var online = Ui.Button("Search Open Library", () => { });
        search.IsDefault = true;
        var status = Ui.Text("", 13, "MutedBrush");
        status.TextWrapping = TextWrapping.Wrap;
        var searchRow = new WrapPanel();
        searchRow.Children.Add(search);
        if (store != null) searchRow.Children.Add(online);
        status.Margin = new Thickness(4, 8, 0, 8);
        searchRow.Children.Add(status);
        header.Children.Add(searchRow);

        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var includeEdition = new CheckBox { Content = "Use the shown ISBN, edition year, and page count", Visibility = Visibility.Collapsed };
        var includeCover = new CheckBox { Content = "Copy this cover into my library", Visibility = Visibility.Collapsed };
        var use = Ui.Button(addBook ? "Use this book" : "Use selected metadata", () => { }, true);
        use.IsEnabled = false;
        if (editBook && !findCover)
        {
            footer.Children.Add(includeEdition);
            footer.Children.Add(includeCover);
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(buttons);
        if (editBook && !findCover) buttons.Children.Add(use);
        var useCover = Ui.Button(findCover ? "Save this cover" : "Use cover only", () => { CoverOnly = true; DialogResult = true; });
        useCover.Visibility = Visibility.Collapsed;
        if (editBook && !addBook) buttons.Children.Add(useCover);
        if (addBook) buttons.Children.Add(Ui.Button("Enter manually…", () =>
        {
            var input = Inputs();
            ExistingBook = null;
            SelectedMatch = null;
            ManualBook = new Book { Title = input.Title, Author = input.Author };
            ManualBook.SelectedEdition.Isbn = input.Isbn;
            DialogResult = true;
        }));
        buttons.Children.Add(Ui.Button("Close", Close));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(columns);
        var results = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(Candidate.Label)) };
        columns.Children.Add(results);
        var details = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var scroll = Ui.Scroll(details);
        Grid.SetColumn(scroll, 1);
        columns.Children.Add(scroll);
        var previews = new Dictionary<MetadataMatch, (EditionMetadata? Edition, byte[]? Cover, string? Warning)>();
        var searching = false;
        Closed += (_, _) => request?.Cancel();
        Loaded += async (_, _) =>
        {
            titleInput.Focus();
            if (findCover) await Search(true);
        };

        (string Title, string Author, string Isbn) Inputs()
        {
            var query = titleInput.Text.Trim();
            var byIsbn = isbnInput.Text.Trim();
            if (byIsbn.Length == 0 && OpenLibraryLookup.NormalizeIsbn(query) is { } parsed)
                return ("", authorInput.Text.Trim(), parsed);
            return (query, authorInput.Text.Trim(), byIsbn);
        }

        void ResetPreview()
        {
            details.Children.Clear();
            includeEdition.Visibility = includeCover.Visibility = Visibility.Collapsed;
            useCover.Visibility = Visibility.Collapsed;
            use.IsEnabled = false;
            ExistingBook = null;
            SelectedMatch = null;
            SelectedEdition = null;
            SelectedCover = null;
        }

        search.Click += async (_, _) => await Search(store == null);
        online.Click += async (_, _) => await Search(true);

        async Task Search(bool useNetwork)
        {
            if (searching) return;
            var input = Inputs();
            if (input.Title.Length == 0 && input.Author.Length == 0 && input.Isbn.Length == 0)
            {
                status.Text = "Enter a title, author, or ISBN.";
                return;
            }
            searching = true;
            search.IsEnabled = online.IsEnabled = false;
            results.ItemsSource = null;
            ResetPreview();
            using var operation = BeginRequest();
            try
            {
                status.Text = "Searching…";
                if (input.Isbn.Length > 0 && OpenLibraryLookup.NormalizeIsbn(input.Isbn) == null)
                    throw new ArgumentException("Enter a valid ISBN-10 or ISBN-13.");
                var saved = books.Where(book => BookIdentity.MatchesSearch(book, input.Title, input.Author, input.Isbn))
                    .Take(25).Select(book => new Candidate(null, book, "My Books")).ToArray();
                var matches = useNetwork
                    ? await lookup.Search(input.Title, input.Author, input.Isbn, operation.Token)
                    : await Task.Run(() => catalog!.Search(input.Title, input.Author, input.Isbn, operation.Token), operation.Token);
                if (operation.IsCancellationRequested) return;
                var candidates = saved.Concat(matches.Select(match => new Candidate(match, null,
                    useNetwork ? "Open Library online" : "Offline catalog"))).ToArray();
                var sourceLabel = useNetwork ? "Open Library" : "Local search";
                status.Text = candidates.Length switch { 0 => $"{sourceLabel} found no matches.", 1 => $"{sourceLabel} · 1 result", _ => $"{sourceLabel} · {candidates.Length} results" };
                if (matches.Any(match => match.UsedMainTitle)) status.Text += $" · Retried with “{input.Title.Split(':', 2)[0].Trim()}”";
                if (findCover) status.Text += " · Choose a cover to save it.";
                if (!useNetwork && catalog?.Installed == false) status.Text += " Offline catalog is available to install in Settings.";
                searching = false;
                results.ItemsSource = candidates;
                results.SelectedIndex = candidates.Length > 0 ? 0 : -1;
            }
            catch (ArgumentException ex) when (!operation.IsCancellationRequested) { status.Text = ex.Message; }
            catch (Microsoft.Data.Sqlite.SqliteException ex) { if (!operation.IsCancellationRequested) status.Text = $"Offline catalog could not be read. {ex.Message}"; }
            catch (Exception ex) when (LookupError(ex)) { if (!operation.IsCancellationRequested) status.Text = Unavailable(ex); }
            finally
            {
                EndRequest(operation);
                search.IsEnabled = online.IsEnabled = true;
                searching = false;
            }
        }

        results.SelectionChanged += async (_, _) =>
        {
            ResetPreview();
            if (searching || results.SelectedItem is not Candidate candidate) return;
            scroll.ScrollToTop();
            using var operation = BeginRequest();
            try
            {
                use.Content = addBook ? "Use this book" : "Use selected metadata";
                if (candidate.Book is { } existing)
                {
                    ExistingBook = existing;
                    details.Children.Add(Ui.Text(existing.Title + " — " + existing.Author));
                    details.Children.Add(Ui.Cover(existing, store!, 90, 125));
                    use.Content = "Open existing book";
                    use.IsEnabled = true;
                    return;
                }
                var match = candidate.Metadata!;
                var fromCatalog = candidate.Source == "Offline catalog";
                details.Children.Add(Ui.Text("Loading match…", 13, "MutedBrush"));
                (EditionMetadata? Edition, byte[]? Cover, string? Warning) preview = (match.Edition, null, null);
                if (!fromCatalog && !previews.TryGetValue(match, out preview))
                {
                    try { preview.Edition = await lookup.ReadEdition(match, operation.Token); }
                    catch (Exception ex) when (LookupError(ex)) { preview.Warning = Unavailable(ex); }
                    if (operation.IsCancellationRequested) return;
                    if ((preview.Edition?.Cover ?? match.Cover) is { } cover)
                    {
                        try { preview.Cover = await lookup.ReadCover(cover, operation.Token); }
                        catch (Exception ex) when (LookupError(ex)) { preview.Warning ??= "Cover unavailable."; }
                    }
                    if (operation.IsCancellationRequested) return;
                    if (preview.Warning == null) previews[match] = preview;
                }
                details.Children.Clear();
                var heading = Ui.Heading(match.Title, 20);
                heading.TextWrapping = TextWrapping.Wrap;
                details.Children.Add(heading);
                if (match.Author.Length > 0)
                {
                    var authors = Ui.Text(match.Author, 13, "MutedBrush");
                    authors.TextWrapping = TextWrapping.Wrap;
                    details.Children.Add(authors);
                }
                var previewRow = new DockPanel();
                details.Children.Add(previewRow);
                if (preview.Cover is { } bytes && Decode(bytes) is { } bitmap)
                {
                    var image = new Image { Source = bitmap, Width = 90, Height = 125, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                    DockPanel.SetDock(image, Dock.Left);
                    previewRow.Children.Add(image);
                    includeCover.IsChecked = !hasCover;
                    includeCover.Visibility = Visibility.Visible;
                    useCover.Visibility = Visibility.Visible;
                }
                else
                {
                    preview.Cover = null;
                    if (findCover) details.Children.Add(Ui.Text("Open Library has no usable cover for this result.", 13, "MutedBrush"));
                }
                var facts = new StackPanel();
                previewRow.Children.Add(facts);
                void AddFact(string value)
                {
                    var fact = Ui.Text(value);
                    fact.TextWrapping = TextWrapping.Wrap;
                    facts.Children.Add(fact);
                }
                if (match.OriginalYear is { } year) AddFact($"Originally published · {year}");
                if (preview.Edition is { } edition)
                {
                    if (edition.Title != match.Title) AddFact("Edition · " + edition.Title);
                    if (edition.Publisher.Length > 0) AddFact(edition.Publisher);
                    if (edition.Isbn.Length > 0) AddFact("ISBN · " + edition.Isbn);
                    if (edition.Year is { } published) AddFact($"Edition published · {published}");
                    if (edition.Pages is { } pages) AddFact($"{pages} pages");
                    includeEdition.IsChecked = addBook || match.Edition != null;
                    includeEdition.Visibility = Visibility.Visible;
                }
                if (preview.Warning != null) details.Children.Add(Ui.Text(preview.Warning, 13, "MutedBrush"));
                SelectedMatch = match;
                SelectedEdition = preview.Edition;
                SelectedCover = preview.Cover;
                use.IsEnabled = true;
                if (fromCatalog && match.Cover is { } catalogCover)
                {
                    var fetch = Ui.Button("Fetch cover from Open Library", () => { });
                    var coverStatus = Ui.Text("", 13, "MutedBrush");
                    fetch.Click += async (_, _) =>
                    {
                        fetch.IsEnabled = false;
                        using var coverRequest = BeginRequest();
                        try
                        {
                            var bytes = await lookup.ReadCover(catalogCover, coverRequest.Token);
                            if (coverRequest.IsCancellationRequested || !ReferenceEquals(results.SelectedItem, candidate)) return;
                            if (bytes == null || Decode(bytes) is not { } image) { coverStatus.Text = "Cover unavailable."; return; }
                            SelectedCover = bytes;
                            includeCover.IsChecked = !hasCover;
                            includeCover.Visibility = Visibility.Visible;
                            useCover.Visibility = Visibility.Visible;
                            details.Children.Insert(0, new Image { Source = image, Width = 90, Height = 125 });
                        }
                        catch (Exception ex) when (LookupError(ex)) { if (!coverRequest.IsCancellationRequested) coverStatus.Text = Unavailable(ex); }
                        finally { EndRequest(coverRequest); fetch.IsEnabled = SelectedCover == null; }
                    };
                    details.Children.Add(fetch);
                    details.Children.Add(coverStatus);
                }
            }
            finally { EndRequest(operation); }
        };

        use.Click += (_, _) =>
        {
            if (addBook && SelectedMatch is { } match)
            {
                var possible = books.FirstOrDefault(book => BookIdentity.MatchesMetadata(book, match, SelectedEdition));
                if (possible != null)
                {
                    var answer = MessageBox.Show(this, $"“{possible.Title}” by {possible.Author} is already in My Books. Open it?\n\nChoose No to create a separate book.", "Possible duplicate", MessageBoxButton.YesNoCancel);
                    if (answer == MessageBoxResult.Cancel) return;
                    if (answer == MessageBoxResult.Yes) ExistingBook = possible;
                }
            }
            if (includeEdition.Visibility != Visibility.Visible || includeEdition.IsChecked != true) SelectedEdition = null;
            if (includeCover.Visibility != Visibility.Visible || includeCover.IsChecked != true) SelectedCover = null;
            DialogResult = true;
        };
    }

    private CancellationTokenSource BeginRequest()
    {
        request?.Cancel();
        return request = new CancellationTokenSource();
    }

    private void EndRequest(CancellationTokenSource completed)
    {
        if (request == completed) request = null;
    }

    private static bool LookupError(Exception ex) => ex is HttpRequestException or IOException or JsonException or OperationCanceledException;
    private static string Unavailable(Exception ex) => ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests }
        ? "Open Library is rate limiting searches. Try again in a moment."
        : "Open Library could not complete this request. Try again when connected.";

    private static BitmapImage? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = 310;
            image.StreamSource = stream;
            image.EndInit();
            return image.PixelWidth > 1 && image.PixelHeight > 1 ? image : null;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { return null; }
    }
}
