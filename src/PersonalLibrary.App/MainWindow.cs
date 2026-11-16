using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

public sealed class MainWindow : Window
{
    private readonly LibraryStore store;
    private readonly ContentControl page = new();
    private readonly bool isolated;
    private string activePage = "Home";
    private string librarySearch = "";
    private bool showCovers;
    private bool sortByAuthor;
    private bool sortDescending;
    private List<Book> books = [];
    private ScrollViewer? homeReadingScroll;
    private int busyCount;
    private readonly TextBlock message = Ui.Text("");
    public MainWindow(LibraryStore store, bool isolated)
    {
        this.store = store; this.isolated = isolated;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = isolated ? "Personal Library — TEST PROFILE" : "Personal Library";
        Width = 1320; Height = 880; MinWidth = 950; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(30, 20, 22, 12) };
        if (isolated)
        {
            var banner = Ui.Text("TEST PROFILE  ·  Synthetic / isolated data  ·  Your personal library is not being used", 13, "AccentBrush");
            DockPanel.SetDock(banner, Dock.Top); root.Children.Add(banner);
        }
        var header = new DockPanel { Margin = new Thickness(0, 10, 0, 16) };
        var add = Ui.Button("+ Add a book", AddBook, true); DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        header.Children.Add(Ui.Heading("personal library", 31)); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        foreach (var name in new[] { "Home", "My Books", "Bibliographies", "Settings" })
            nav.Children.Add(Ui.Button(name, () => { activePage = name; RefreshPage(); }));
        DockPanel.SetDock(nav, Dock.Top); root.Children.Add(nav);
        message.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        message.FontSize = 12; DockPanel.SetDock(message, Dock.Bottom); root.Children.Add(message);
        root.Children.Add(page); Content = root; RefreshPage();
        Closing += (_, args) => { if (busyCount > 0) args.Cancel = true; };
    }

    private void RefreshPage()
    {
        books = store.Books();
        if (activePage != "Home") homeReadingScroll = null;
        message.Text = store.LastBackupError ?? $"{books.Count(b => !b.Excluded)} books  ·  Saved on this computer  ·  Offline core";
        page.Content = activePage switch { "My Books" => Library(), "Bibliographies" => Bibliographies(), "Settings" => SettingsPage(), _ => Home() };
    }

    private void Edit(Book book)
    {
        var editor = new BookEditor(store, book) { Owner = this };
        editor.ShowDialog(); RefreshPage();
    }

    private void AddBook()
    {
        var lookup = new MetadataLookupWindow("", "", editBook: true, store: store, addBook: true) { Owner = this };
        if (lookup.ShowDialog() != true) return;
        if (lookup.ExistingBook is { } existing) { Edit(existing); return; }
        var editor = new BookEditor(store, lookup.ManualBook ?? new Book()) { Owner = this };
        if (lookup.SelectedMatch is { } match) editor.ApplyMetadata(match, lookup.SelectedEdition, lookup.SelectedCover);
        editor.ShowDialog();
        RefreshPage();
    }

    private UIElement Home()
    {
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        var reading = new StackPanel(); reading.Children.Add(Ui.Heading("A little time for a good book"));
        var readingHeader = new DockPanel();
        var manage = Ui.Button("Manage sections", () =>
        {
            var dialog = new ReadingSectionsWindow(store) { Owner = this };
            if (dialog.ShowDialog() == true) RefreshPage();
        });
        DockPanel.SetDock(manage, Dock.Right);
        readingHeader.Children.Add(manage);
        var title = Ui.Text("CURRENTLY READING", 12, "MutedBrush");
        title.VerticalAlignment = VerticalAlignment.Center;
        readingHeader.Children.Add(title);
        reading.Children.Add(readingHeader);
        var sections = store.ReadingSections();
        var current = books.Where(b => !b.Excluded && b.Status == ReadingStatus.CurrentlyReading).ToList();
        if (current.Count == 0)
        {
            var empty = new StackPanel(); empty.Children.Add(Ui.Heading("Your next chapter starts here", 22));
            empty.Children.Add(Ui.Text("Add a book or open one from My Books, then start a reading."));
            empty.Children.Add(Ui.Button("Browse My Books", () => { activePage = "My Books"; RefreshPage(); }));
            reading.Children.Add(Ui.Card(empty));
        }
        var sectionIds = sections.Select(section => section.Id).ToHashSet();
        foreach (var section in sections)
            AddReadingSection(reading, section.Name, current.Where(book => book.ReadingSectionId == section.Id).ToList(), sections);
        var other = current.Where(book => book.ReadingSectionId is not { } id || !sectionIds.Contains(id)).ToList();
        if (other.Count > 0)
            AddReadingSection(reading, sections.Count > 0 ? ReadingSection.UnassignedName : null, other, sections);

        var offset = homeReadingScroll?.VerticalOffset ?? 0;
        homeReadingScroll = Ui.Scroll(reading);
        homeReadingScroll.ScrollToVerticalOffset(offset);
        columns.Children.Add(homeReadingScroll);
        var sidebar = new StackPanel(); var challenge = new StackPanel();
        var year = DateTime.Today.Year; var goal = GetGoal(year); var count = ReadingRules.CompletedInYear(books, year);
        challenge.Children.Add(Ui.Text($"{year} READING CHALLENGE", 12, "MutedBrush"));
        challenge.Children.Add(Ui.Heading($"{count} / {goal} books", 29));
        challenge.Children.Add(new ProgressBar { Minimum = 0, Maximum = goal, Value = count, Height = 8, Margin = new Thickness(0, 8, 0, 12) });
        challenge.Children.Add(Ui.Text("Completed readings this year. Rereads count; undated history stays outside the calendar.", 13, "MutedBrush"));
        challenge.Children.Add(Ui.Button("Change goal", () => { activePage = "Settings"; RefreshPage(); }));
        sidebar.Children.Add(Ui.Card(challenge));
        AddShortList(sidebar, "Read next", books.Where(b => !b.Excluded && b.ReadNext && b.Status == ReadingStatus.ToRead), true);
        AddShortList(sidebar, "Recently added to to-read", books.Where(b => !b.Excluded && b.Status == ReadingStatus.ToRead).OrderByDescending(b => b.ToReadAddedAt).Take(10), false);
        var scroll = Ui.Scroll(sidebar); Grid.SetColumn(scroll, 1); columns.Children.Add(scroll); return columns;
    }

    private void AddReadingSection(Panel reading, string? title, List<Book> items, IReadOnlyList<ReadingSection> sections)
    {
        if (title != null) reading.Children.Add(Ui.Heading(title, 22));
        if (items.Count == 0) reading.Children.Add(Ui.Text("No books in this section yet.", 13, "MutedBrush"));
        foreach (var book in items)
        {
            var row = new DockPanel();
            var cover = Ui.Cover(book, store, 100, 148);
            cover.Margin = new Thickness(0, 0, 20, 0);
            DockPanel.SetDock(cover, Dock.Left);
            row.Children.Add(cover);
            var details = new StackPanel(); details.Children.Add(Ui.Heading(book.Title, 23));
            details.Children.Add(Ui.Text(book.Author, 15, "MutedBrush")); details.Children.Add(Ui.Text(ReadingRules.ProgressText(book), 15));
            var actions = new WrapPanel(); actions.Children.Add(Ui.Button("Update progress / open", () => Edit(book), true));
            actions.Children.Add(Ui.Button("Move up", () => Move(book, items, -1)));
            actions.Children.Add(Ui.Button("Move down", () => Move(book, items, 1)));
            if (sections.Count > 0) actions.Children.Add(ReadingSectionMenu(book, sections));
            details.Children.Add(actions); row.Children.Add(details); reading.Children.Add(Ui.Card(row));
        }
    }

    private Button ReadingSectionMenu(Book book, IReadOnlyList<ReadingSection> sections)
    {
        var currentSection = sections.FirstOrDefault(section => section.Id == book.ReadingSectionId)?.Id;
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.SetResourceReference(ContextMenu.BackgroundProperty, "SurfaceBrush");
        menu.SetResourceReference(ContextMenu.BorderBrushProperty, "LineBrush");
        foreach (var (id, name) in sections.Select(section => ((Guid?)section.Id, section.Name)).Append((null, ReadingSection.UnassignedName)))
        {
            if (id == currentSection) continue;
            var item = new MenuItem { Header = new TextBlock { Text = name } };
            item.SetResourceReference(MenuItem.ForegroundProperty, "TextBrush");
            AutomationProperties.SetName(item, name);
            item.Click += (_, _) => Ui.Guard(() => { store.MoveToReadingSection(book.Id, id); RefreshPage(); });
            menu.Items.Add(item);
        }
        var button = Ui.Button("Move to section", () => menu.IsOpen = true);
        AutomationProperties.SetName(button, $"Move {book.Title} to section");
        menu.PlacementTarget = button;
        button.ContextMenu = menu;
        return button;
    }

    private void AddShortList(Panel parent, string title, IEnumerable<Book> source, bool reorder)
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Heading(title, 21)); var items = source.ToList();
        if (items.Count == 0) panel.Children.Add(Ui.Text("No books here yet.", 13, "MutedBrush"));
        foreach (var book in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (reorder)
            {
                var moves = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                foreach (var (symbol, direction, offset) in new[] { ("↑", "up", -1), ("↓", "down", 1) })
                {
                    var move = Ui.Button(symbol, () => Move(book, items, offset));
                    move.Padding = new Thickness(7, 2, 7, 2);
                    move.Margin = new Thickness(0, 2, 0, 2);
                    AutomationProperties.SetName(move, $"Move {book.Title} {direction}");
                    moves.Children.Add(move);
                }
                DockPanel.SetDock(moves, Dock.Right);
                row.Children.Add(moves);
            }

            var button = Ui.Button(book.Title, () => Edit(book));
            button.Padding = new Thickness(7);
            button.Margin = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(button, $"Open {book.Title}");
            var summary = new DockPanel();
            var cover = Ui.Cover(book, store, 36, 54);
            cover.Margin = new Thickness(0, 0, 9, 0);
            DockPanel.SetDock(cover, Dock.Left);
            summary.Children.Add(cover);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(book.Title, 13);
            name.Margin = new Thickness(0, 0, 0, 3);
            labels.Children.Add(name);
            var author = Ui.Text(book.Author, 12, "MutedBrush");
            author.Margin = new Thickness(0);
            labels.Children.Add(author);
            summary.Children.Add(labels);
            button.Content = summary;
            row.Children.Add(button);
            panel.Children.Add(row);
        }
        parent.Children.Add(Ui.Card(panel));
    }

    private void Move(Book book, List<Book> items, int offset)
    {
        var index = items.FindIndex(b => b.Id == book.Id); var other = index + offset;
        if (other < 0 || other >= items.Count) return;
        (items[index], items[other]) = (items[other], items[index]);
        for (var i = 0; i < items.Count; i++) items[i].SortOrder = i;
        store.SaveBatch(items);
        RefreshPage();
    }

    private UIElement Library()
    {
        var root = new DockPanel(); var top = new StackPanel(); top.Children.Add(Ui.Heading("My Books"));
        var search = Ui.Input(top, "Search saved books by title, author, ISBN, or shelf", librarySearch);
        var statusFilters = new WrapPanel();
        ReadingStatus? statusFilter = null;
        (string Label, ReadingStatus? Status)[] statusOptions =
        [
            ("All", null), ("Unclassified", ReadingStatus.Unclassified), ("To read", ReadingStatus.ToRead),
            ("Reading", ReadingStatus.CurrentlyReading), ("Read", ReadingStatus.Read), ("Partially read", ReadingStatus.PartiallyRead),
            ("Did not finish", ReadingStatus.DidNotFinish), ("Reference only", ReadingStatus.ReferenceOnly)
        ];
        var statusButtons = new List<(RadioButton Button, ReadingStatus? Status)>();
        foreach (var (label, value) in statusOptions)
        {
            var button = new RadioButton { Content = label, IsChecked = value == null, Style = (Style)FindResource("StatusFilter") };
            AutomationProperties.SetName(button, value == null ? "All reading statuses" : ReadingRules.Label(value.Value));
            statusFilters.Children.Add(button);
            statusButtons.Add((button, value));
        }
        top.Children.Add(statusFilters);
        var filters = new WrapPanel();
        var shelf = new ComboBox { Width = 195, ItemsSource = new[] { "All shelves" }.Concat(books.SelectMany(b => b.Shelves).Distinct(StringComparer.OrdinalIgnoreCase).Order()), SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 10) };
        var excluded = new CheckBox { Content = "Show excluded records" };
        var missingCover = new CheckBox { Content = "Missing local cover", Margin = new Thickness(10, 4, 0, 10) };
        var bulkEdit = new CheckBox { Content = "Bulk edit", Margin = new Thickness(10, 4, 0, 10) };
        var views = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 0, 10) };
        views.Children.Add(Ui.Text("View", 13, "MutedBrush"));
        var listView = new RadioButton { Content = "List", IsChecked = !showCovers, Margin = new Thickness(12, 0, 12, 0) };
        var coverView = new RadioButton { Content = "Covers", IsChecked = showCovers };
        foreach (var option in new[] { listView, coverView })
        {
            option.SetResourceReference(ForegroundProperty, "TextBrush");
            views.Children.Add(option);
        }
        filters.Children.Add(shelf); filters.Children.Add(excluded); filters.Children.Add(missingCover); filters.Children.Add(views);
        filters.Children.Add(bulkEdit);
        filters.Children.Add(Ui.Button("Review duplicates…", () =>
        {
            new LibraryCleanupWindow(store) { Owner = this }.ShowDialog();
            RefreshPage();
        }));
        filters.Children.Add(Ui.Button("Find Goodreads covers…", () =>
        {
            new GoodreadsCoversWindow(store) { Owner = this }.ShowDialog();
            RefreshPage();
        }));
        top.Children.Add(filters);
        var sort = new WrapPanel();
        var sortLabel = Ui.Text("Sort by", 13, "MutedBrush");
        sortLabel.Margin = new Thickness(0, 8, 8, 0);
        sort.Children.Add(sortLabel);
        var sortField = new ComboBox { ItemsSource = new[] { "Title", "Author" }, SelectedIndex = sortByAuthor ? 1 : 0, Width = 120 };
        AutomationProperties.SetName(sortField, "Sort by");
        sort.Children.Add(sortField);
        var ascending = new RadioButton { Content = "A → Z", IsChecked = !sortDescending, Style = (Style)FindResource("StatusFilter"), Margin = new Thickness(8, 3, 6, 12) };
        var descending = new RadioButton { Content = "Z → A", IsChecked = sortDescending, Style = (Style)FindResource("StatusFilter"), Margin = new Thickness(0, 3, 0, 12) };
        AutomationProperties.SetName(ascending, "Ascending sort order");
        AutomationProperties.SetName(descending, "Descending sort order");
        sort.Children.Add(ascending);
        sort.Children.Add(descending);
        top.Children.Add(sort);
        var selected = new HashSet<Guid>();
        List<Book> matches = [];
        var bulk = new WrapPanel { Visibility = Visibility.Collapsed };
        var selectedCount = Ui.Text("0 selected", 13, "MutedBrush");
        selectedCount.Margin = new Thickness(0, 8, 12, 8);
        var statuses = Enum.GetValues<ReadingStatus>();
        var bulkStatus = new ComboBox
        {
            Width = 160, ItemsSource = new[] { "Keep status" }.Concat(statuses.Select(ReadingRules.Label)),
            SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 8)
        };
        var bulkShelf = new TextBox { Width = 150, Margin = new Thickness(0, 0, 8, 8) };
        var apply = Ui.Button("Apply to selected", () => { }, true);
        var remove = Ui.Button("Remove selected…", () => { });
        var merge = Ui.Button("Review selected pair…", () =>
        {
            var pair = matches.Where(book => selected.Contains(book.Id)).ToArray();
            if (pair.Length != 2) return;
            new LibraryCleanupWindow(store, new(pair[0], pair[1], "Selected pair")) { Owner = this }.ShowDialog();
            RefreshPage();
        });
        var results = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        apply.Click += (_, _) => Ui.Guard(() =>
        {
            var shelfName = bulkShelf.Text.Trim();
            if (bulkStatus.SelectedIndex == 0 && shelfName.Length == 0)
                throw new InvalidOperationException("Choose a status or enter a shelf name.");
            var changes = matches.Where(book => selected.Contains(book.Id)).Select(LibraryStore.Clone).ToList();
            foreach (var book in changes)
            {
                if (bulkStatus.SelectedIndex > 0) ReadingRules.Classify(book, statuses[bulkStatus.SelectedIndex - 1]);
                if (shelfName.Length > 0) book.Shelves.Add(shelfName);
            }
            store.SaveBatch(changes);
            selected.Clear();
            books = store.Books();
            var previousShelf = shelf.SelectedItem;
            shelf.ItemsSource = new[] { "All shelves" }.Concat(books.SelectMany(book => book.Shelves).Distinct(StringComparer.OrdinalIgnoreCase).Order());
            shelf.SelectedItem = previousShelf;
            if (shelf.SelectedIndex < 0) shelf.SelectedIndex = 0;
            Populate();
            message.Text = store.LastBackupError ?? $"Updated {changes.Count} books.";
        });
        bulk.Children.Add(selectedCount);
        bulk.Children.Add(Ui.Button("Select all results", () => { selected.UnionWith(matches.Select(book => book.Id)); Populate(); }));
        bulk.Children.Add(Ui.Button("Clear selection", () => { selected.Clear(); Populate(); }));
        bulk.Children.Add(bulkStatus);
        var shelfLabel = Ui.Text("Add to shelf", 13, "MutedBrush");
        shelfLabel.Margin = new Thickness(4, 8, 6, 8);
        bulk.Children.Add(shelfLabel);
        AutomationProperties.SetName(bulkShelf, "Add to shelf");
        bulk.Children.Add(bulkShelf);
        bulk.Children.Add(apply);
        remove.Click += async (_, _) =>
        {
            var booksToRemove = matches.Where(book => selected.Contains(book.Id)).ToList();
            var confirmation = new BookRemovalWindow(booksToRemove) { Owner = this };
            if (confirmation.ShowDialog() != true) return;
            BookRemovalResult? outcome = null;
            await Busy(() => store.RemoveBooks(booksToRemove, confirmation.CalibrePlan), result =>
            {
                outcome = result;
                books = store.Books();
                selected.Clear();
                Populate();
                if (result.Warning != null)
                    MessageBox.Show(this, result.Warning, "Book removal", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (outcome != null) message.Text = store.LastBackupError ?? $"Removed {outcome.Removed} books.";
        };
        bulk.Children.Add(remove);
        bulk.Children.Add(merge);
        top.Children.Add(bulk);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        root.Children.Add(results);
        void UpdateSelection()
        {
            selectedCount.Text = $"{selected.Count} selected";
            apply.IsEnabled = selected.Count > 0;
            remove.IsEnabled = selected.Count > 0;
            merge.IsEnabled = selected.Count == 2;
        }
        CheckBox SelectBook(Book book)
        {
            var check = new CheckBox { Content = "Select", IsChecked = selected.Contains(book.Id), Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(check, $"Select {book.Title}");
            void ChangeSelection(object sender, RoutedEventArgs args)
            {
                if (check.IsChecked == true) selected.Add(book.Id);
                else selected.Remove(book.Id);
                UpdateSelection();
            }
            check.Checked += ChangeSelection;
            check.Unchecked += ChangeSelection;
            return check;
        }
        void Populate()
        {
            var filtered = books.Where(b => (excluded.IsChecked == true || !b.Excluded) &&
                (missingCover.IsChecked != true || b.Editions.All(edition => store.CoverPath(edition) == null)) &&
                (statusFilter == null || b.Status == statusFilter) &&
                (shelf.SelectedIndex == 0 || b.Shelves.Contains(shelf.SelectedItem?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)) &&
                ($"{b.Title} {b.Author} {string.Join(' ', b.Shelves)} {string.Join(' ', b.Editions.Select(e => e.Isbn))}").Contains(search.Text, StringComparison.OrdinalIgnoreCase));
            var comparer = StringComparer.CurrentCultureIgnoreCase;
            Func<Book, string> key = sortByAuthor ? book => book.Author : book => book.Title;
            var ordered = sortDescending ? filtered.OrderByDescending(key, comparer) : filtered.OrderBy(key, comparer);
            matches = ordered.ThenBy(book => book.Title, comparer).ThenBy(book => book.Author, comparer).ToList();
            selected.IntersectWith(matches.Select(book => book.Id));
            UpdateSelection();
            if (matches.Count == 0)
            {
                results.Content = Ui.Card(Ui.Text("No matching books. Add one or change the filters.", 15, "MutedBrush"));
                return;
            }
            Func<Book, CheckBox>? selector = bulkEdit.IsChecked == true ? SelectBook : null;
            results.Content = showCovers ? LibraryCovers(matches, selector) : LibraryList(matches, selector);
        }
        foreach (var (button, value) in statusButtons)
            button.Checked += (_, _) => { statusFilter = value; Populate(); };
        search.TextChanged += (_, _) =>
        {
            librarySearch = search.Text;
            Populate();
        };
        shelf.SelectionChanged += (_, _) => Populate();
        sortField.SelectionChanged += (_, _) => { sortByAuthor = sortField.SelectedIndex == 1; Populate(); };
        ascending.Checked += (_, _) => { sortDescending = false; Populate(); };
        descending.Checked += (_, _) => { sortDescending = true; Populate(); };
        excluded.Click += (_, _) => Populate();
        missingCover.Click += (_, _) => Populate();
        bulkEdit.Click += (_, _) =>
        {
            bulk.Visibility = bulkEdit.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            selected.Clear();
            Populate();
        };
        listView.Checked += (_, _) => { showCovers = false; Populate(); };
        coverView.Checked += (_, _) => { showCovers = true; Populate(); };
        Populate(); return root;
    }

    private ListBox LibraryList(IEnumerable<Book> matches, Func<Book, CheckBox>? selector)
    {
        var list = new ListBox();
        foreach (var book in matches)
        {
            var row = new DockPanel();
            var open = Ui.Button("Open", () => Edit(book));
            open.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(open, $"Open {book.Title}");
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            if (selector != null)
            {
                var check = selector(book);
                check.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(check, Dock.Left);
                row.Children.Add(check);
            }
            var cover = Ui.Cover(book, store, 48, 72);
            cover.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(cover, Dock.Left);
            row.Children.Add(cover);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Ui.Heading(book.Title, 20));
            labels.Children.Add(Ui.Text($"{book.Author}  ·  {BookStatus(book)}", 13, "MutedBrush"));
            if (book.Shelves.Count > 0)
                labels.Children.Add(Ui.Text(string.Join("  /  ", book.Shelves), 12, "MutedBrush"));
            row.Children.Add(labels);
            list.Items.Add(new ListBoxItem { Content = row, Tag = book });
        }
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: Book book }) Edit(book); };
        return list;
    }

    private ScrollViewer LibraryCovers(IEnumerable<Book> matches, Func<Book, CheckBox>? selector)
    {
        var grid = new WrapPanel { ItemWidth = 176, ItemHeight = selector == null ? 290 : 318 };
        foreach (var book in matches)
        {
            var check = selector?.Invoke(book);
            var tile = Ui.Button(book.Title, () =>
            {
                if (check == null) Edit(book);
                else check.IsChecked = check.IsChecked != true;
            });
            tile.Padding = new Thickness(10);
            tile.ToolTip = $"{book.Title}\n{book.Author}\n{BookStatus(book)}";
            AutomationProperties.SetName(tile, check == null ? $"Open {book.Title}" : $"Toggle selection for {book.Title}");
            var labels = new StackPanel { Width = 142 };
            var cover = Ui.Cover(book, store, 110, 162);
            cover.HorizontalAlignment = HorizontalAlignment.Center;
            cover.Margin = new Thickness(0, 0, 0, 8);
            labels.Children.Add(cover);
            var name = Ui.Heading(book.Title, 15);
            name.Height = 36;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(0, 0, 0, 3);
            labels.Children.Add(name);
            var author = Ui.Text(book.Author, 12, "MutedBrush");
            author.TextWrapping = TextWrapping.NoWrap;
            author.TextTrimming = TextTrimming.CharacterEllipsis;
            author.Margin = new Thickness(0, 0, 0, 2);
            labels.Children.Add(author);
            var status = Ui.Text(BookStatus(book), 11, "MutedBrush");
            status.Margin = new Thickness(0);
            status.Height = 30;
            labels.Children.Add(status);
            tile.Content = labels;
            if (check == null) grid.Children.Add(tile);
            else
            {
                var selectableTile = new DockPanel();
                check.Margin = new Thickness(8, 2, 0, 6);
                DockPanel.SetDock(check, Dock.Top);
                selectableTile.Children.Add(check);
                selectableTile.Children.Add(tile);
                grid.Children.Add(selectableTile);
            }
        }
        return Ui.Scroll(grid);
    }

    private static string BookStatus(Book book) =>
        ReadingRules.Label(book.Status) + (book.Excluded ? "  ·  EXCLUDED" : "");

    private UIElement Bibliographies()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Heading("Bibliographies"));
        panel.Children.Add(Ui.Text("Paste references in a book's Bibliography tab. Each document stays in your profile as editable Markdown.", 14, "MutedBrush"));
        var found = books.Where(b => File.Exists(store.BibliographyPath(b.Id)) || store.HasEpubBibliography(b.Id)).ToList();
        foreach (var book in found) panel.Children.Add(Ui.Button(book.Title + " — edit bibliography", () => Edit(book)));
        if (found.Count == 0) panel.Children.Add(Ui.Text("No saved bibliographies yet. Open a book to add the first one."));
        return Ui.Scroll(panel);
    }

    private int GetGoal(int year) => int.TryParse(store.Setting("goal-" + year, "24"), out var value) && value > 0 ? value : 24;

    private UIElement SettingsPage()
    {
        var panel = new StackPanel { MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Ui.Heading("Make yourself at home"));
        var themes = new WrapPanel(); themes.Children.Add(Ui.Button("Light mode", () => { store.SetSetting("theme", "light"); App.ApplyTheme(false); }));
        themes.Children.Add(Ui.Button("Dark mode", () => { store.SetSetting("theme", "dark"); App.ApplyTheme(true); })); panel.Children.Add(themes);
        var year = DateTime.Today.Year;
        var goal = Ui.Input(panel, $"{year} annual book goal", GetGoal(year).ToString());
        panel.Children.Add(Ui.Button("Save goal", () =>
        {
            if (!int.TryParse(goal.Text, out var target) || target < 1 || target > 100000) throw new InvalidOperationException("Enter a goal from 1 to 100,000.");
            store.SetSetting("goal-" + year, target.ToString()); message.Text = "Reading goal saved.";
        }, true));
        panel.Children.Add(CalibreSettings());
        panel.Children.Add(GoodreadsSettings());
        panel.Children.Add(CatalogSettings());
        panel.Children.Add(Ui.Heading("Your local profile", 23));
        panel.Children.Add(Ui.Text(store.Profile, 13, "MutedBrush"));
        panel.Children.Add(Ui.Button("Open profile folder", () => Process.Start(new ProcessStartInfo(store.Profile) { UseShellExecute = true })));
        panel.Children.Add(Ui.Heading("Backup & restore", 23));
        panel.Children.Add(Ui.Text("A checksummed snapshot includes your books, notes, covers, and Markdown files. A daily backup is created after a change. Restore first checks the archive and saves a pre-restore backup.", 14, "MutedBrush"));
        var backup = Ui.Button("Create backup now", () => { });
        backup.Click += async (_, _) => await Busy(() => store.Backup(), path => MessageBox.Show("Backup created:\n" + path, "Backup complete")); panel.Children.Add(backup);
        panel.Children.Add(Ui.Button("Open backups folder", () => { Directory.CreateDirectory(store.BackupDirectory); Process.Start(new ProcessStartInfo(store.BackupDirectory) { UseShellExecute = true }); }));
        var restore = Ui.Button("Preview and restore backup…", () => { });
        restore.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Personal Library backup (*.zip)|*.zip", InitialDirectory = store.BackupDirectory };
            if (dialog.ShowDialog(this) != true) return;
            await Busy(() => store.PreviewBackup(dialog.FileName), preview =>
            {
                if (MessageBox.Show(this, $"Created: {preview.Created:g}\nBooks: {preview.Books}\nFiles: {preview.Files}\n\nReplace this profile with the validated backup? A pre-restore backup will be kept.", "Restore preview", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                // The UI is disabled until the background restore finishes.
                _ = Busy(() => { store.Restore(dialog.FileName); return true; }, _ => { App.ApplyTheme(store.Setting("theme", "light") == "dark"); RefreshPage(); message.Text = "Profile restored."; });
            });
        };
        panel.Children.Add(restore);
        panel.Children.Add(Ui.Heading("Build status", 23));
        panel.Children.Add(Ui.Text("Offline reading tracker with Calibre and Goodreads imports and optional online metadata lookup.", 14, "MutedBrush"));
        return Ui.Scroll(panel);
    }

    private UIElement CatalogSettings()
    {
        var section = new StackPanel();
        var catalog = new OfflineCatalog(store.Root);
        section.Children.Add(Ui.Heading("Offline Open Library catalog", 23));
        var status = Ui.Text(catalog.Installed ? "Installed · searchable by title, author, and ISBN" : "Available to install", 14, "MutedBrush");
        section.Children.Add(status);
        var description = Ui.Text("Download the Authors and Editions data dumps, then select the files here. The index uses additional disk space. Covers are fetched separately when you choose one.", 14, "MutedBrush");
        description.TextWrapping = TextWrapping.Wrap;
        section.Children.Add(description);
        var actions = new WrapPanel();
        var reconcile = Ui.Button("Preview ISBN matches…", () => { });
        reconcile.IsEnabled = catalog.Installed;
        reconcile.Click += async (_, _) => await Busy(
            () => CatalogReconciliation.Preview(store, catalog), preview =>
            {
                var dialog = new Window
                {
                    Title = "Offline catalog match preview", Owner = this, Width = 780, Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Style = (Style)Application.Current.FindResource(typeof(Window))
                };
                var body = new DockPanel { Margin = new Thickness(22) };
                dialog.Content = body;
                var summary = Ui.Text($"{preview.Editions.Count} edition updates · {preview.Ambiguous} ambiguous or conflicting matches skipped", 15);
                summary.TextWrapping = TextWrapping.Wrap;
                DockPanel.SetDock(summary, Dock.Top);
                body.Children.Add(summary);
                var buttons = new WrapPanel();
                var apply = Ui.Button("Back up and apply matches", () => dialog.DialogResult = true, true);
                apply.IsEnabled = preview.Editions.Count > 0;
                buttons.Children.Add(apply);
                buttons.Children.Add(Ui.Button("Close", dialog.Close));
                DockPanel.SetDock(buttons, Dock.Bottom);
                body.Children.Add(buttons);
                body.Children.Add(new ListBox { ItemsSource = preview.Editions, ItemTemplate = Ui.WrappedLabel(nameof(CatalogEditionChange.Label)) });
                if (dialog.ShowDialog() == true)
                    _ = Busy(() => { store.ApplyCatalogMatches(preview); return preview.Editions.Count; }, count =>
                    {
                        RefreshPage();
                        message.Text = store.LastBackupError ?? $"Updated {count} editions from the offline catalog.";
                    });
            });
        actions.Children.Add(Ui.Button("Open data dumps page", () =>
            Process.Start(new ProcessStartInfo("https://openlibrary.org/developers/dumps") { UseShellExecute = true })));
        actions.Children.Add(Ui.Button("Install from data dumps…", () =>
        {
            const string filter = "Open Library dump (*.gz;*.txt;*.tsv)|*.gz;*.txt;*.tsv|All files|*.*";
            var authors = new OpenFileDialog { Title = "Choose the Open Library authors dump", Filter = filter };
            if (authors.ShowDialog(this) != true) return;
            var editions = new OpenFileDialog
            {
                Title = "Choose the Open Library editions dump", Filter = filter, InitialDirectory = Path.GetDirectoryName(authors.FileName)
            };
            if (editions.ShowDialog(this) != true) return;
            var install = new CatalogInstallWindow(catalog, authors.FileName, editions.FileName) { Owner = this };
            if (install.ShowDialog() == true)
            {
                status.Text = $"Installed · {install.InstalledCount:N0} searchable editions";
                reconcile.IsEnabled = true;
            }
        }));
        actions.Children.Add(reconcile);
        section.Children.Add(actions);
        return section;
    }

    private UIElement GoodreadsSettings()
    {
        var section = new StackPanel();
        section.Children.Add(Ui.Heading("Goodreads import", 23));
        var description = Ui.Text("Choose your Goodreads library CSV to review matches and changes before importing.", 14, "MutedBrush");
        description.TextWrapping = TextWrapping.Wrap;
        section.Children.Add(description);
        string? archivePath = null;
        var archive = Ui.Button("Use reading dates from Goodreads ZIP…", () => { });
        var archiveLabel = Ui.Text("", 13, "MutedBrush");
        archiveLabel.TextWrapping = TextWrapping.Wrap;
        var archiveActions = new WrapPanel();
        archiveActions.Children.Add(archive);
        var clearArchive = Ui.Button("Clear ZIP selection", () =>
        {
            archivePath = null;
            archiveLabel.Text = "";
        });
        clearArchive.IsEnabled = false;
        clearArchive.Click += (_, _) => clearArchive.IsEnabled = false;
        archive.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose your Goodreads account-data ZIP", Filter = "ZIP archives (*.zip)|*.zip" };
            if (dialog.ShowDialog(this) != true) return;
            archivePath = dialog.FileName;
            archiveLabel.Text = Path.GetFileName(archivePath);
            clearArchive.IsEnabled = true;
        };
        archiveActions.Children.Add(clearArchive);
        section.Children.Add(archiveActions);
        section.Children.Add(archiveLabel);
        var import = Ui.Button("Choose Goodreads CSV…", () => { });
        section.Children.Add(import);
        var summary = Ui.Text("", 13, "MutedBrush");
        summary.TextWrapping = TextWrapping.Wrap;
        section.Children.Add(summary);
        void ShowReport()
        {
            if (store.LastGoodreadsImport() is { } report)
                summary.Text = $"Last import · {report.ImportedAt:g} · {report.Added} new · {report.Updated} updated · {report.Skipped} skipped";
        }
        ShowReport();
        import.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose your Goodreads library export", Filter = "CSV files (*.csv)|*.csv" };
            if (dialog.ShowDialog(this) != true) return;
            GoodreadsImportPreview? preview = null;
            await Busy(() => GoodreadsImporter.Preview(store, dialog.FileName, archivePath), result => preview = result);
            if (preview == null || new GoodreadsImportWindow(preview) { Owner = this }.ShowDialog() != true) return;
            await Busy(() => { store.ApplyGoodreads(preview); return true; }, _ =>
            {
                books = store.Books();
                ShowReport();
            });
        };
        return section;
    }

    private UIElement CalibreSettings()
    {
        var section = new StackPanel();
        section.Children.Add(Ui.Heading("Calibre library", 23));
        section.Children.Add(Ui.Text("Choose the folder containing metadata.db. Refresh previews new and updated books. New books start as Unclassified.", 14, "MutedBrush"));
        var path = Ui.Input(section, "Calibre library folder", store.Setting(CalibreImporter.FolderSetting, ""));
        var actions = new WrapPanel();
        var summary = Ui.Text("", 14, "MutedBrush");
        var previewList = new ListBox { MaxHeight = 275, Visibility = Visibility.Collapsed };
        CalibrePreview? preview = null;
        var apply = Ui.Button("Apply import", () => { }, true);
        apply.IsEnabled = false;
        void ClearPreview()
        {
            preview = null;
            apply.IsEnabled = false;
            previewList.ItemsSource = null;
            previewList.Visibility = Visibility.Collapsed;
            summary.Text = "";
        }
        path.TextChanged += (_, _) => ClearPreview();
        actions.Children.Add(Ui.Button("Choose folder…", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose your Calibre library folder" };
            if (Directory.Exists(path.Text)) dialog.InitialDirectory = path.Text;
            if (dialog.ShowDialog(this) == true) path.Text = dialog.FolderName;
        }));
        var refresh = Ui.Button("Refresh / preview", () => { });
        refresh.Click += async (_, _) =>
        {
            ClearPreview();
            var folder = path.Text;
            await Busy(() => CalibreImporter.Preview(store, folder), result =>
            {
                preview = result;
                summary.Text = $"{result.Added} new · {result.Updated} updated · {result.Unchanged} unchanged · {result.Covers} covers";
                if (result.MissingFromSource > 0) summary.Text += $"\n{result.MissingFromSource} previously imported books are missing from Calibre and will be kept.";
                if (result.WithoutLocalFiles > 0) summary.Text += $"\n{result.WithoutLocalFiles} records have no available ebook file.";
                if (result.PreviouslyRemoved > 0) summary.Text += $"\n{result.PreviouslyRemoved} previously removed books were skipped.";
                previewList.ItemsSource = result.Changes.Select(change =>
                    $"{change.Action}  ·  {change.Title} — {change.Author}{(change.Excluded ? "  ·  EXCLUDED" : "")}");
                previewList.Visibility = result.Changes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                apply.IsEnabled = result.Changes.Count > 0 || store.Setting(CalibreImporter.FolderSetting, "") != result.Folder;
                apply.Content = result.Changes.Count > 0 ? "Apply import" : "Save connection";
            });
        };
        apply.Click += async (_, _) =>
        {
            if (preview is not { } reviewed) return;
            await Busy(() => { store.ApplyCalibre(reviewed); return true; }, _ =>
            {
                ClearPreview();
                summary.Text = $"Connection saved. Added {reviewed.Added} and updated {reviewed.Updated} books.";
                books = store.Books();
            });
        };
        actions.Children.Add(refresh);
        actions.Children.Add(apply);
        section.Children.Add(actions);
        section.Children.Add(summary);
        section.Children.Add(previewList);
        return section;
    }

    private async Task Busy<T>(Func<T> work, Action<T> completed)
    {
        busyCount++; IsEnabled = false; message.Text = "Working…";
        try { var result = await Task.Run(work); completed(result); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Personal Library", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { busyCount--; IsEnabled = busyCount == 0; if (busyCount == 0) message.Text = store.LastBackupError ?? "Ready · changes stay on this computer"; }
    }
}
