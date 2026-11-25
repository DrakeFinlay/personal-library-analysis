using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Text.Json;
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
    private UIElement? libraryPage;
    private List<Book> books = [];
    private ScrollViewer? homeReadingScroll;
    private ScrollViewer? recommendationsScroll;
    private ScrollViewer? bibliographiesScroll;
    private StatisticsPage? statisticsPage;
    private int goalsYear = DateTime.Today.Year;
    private int busyCount;
    private LibraryDisplay libraryDisplay = new();
    private readonly TextBlock message = Ui.Text("");
    public MainWindow(LibraryStore store, bool isolated)
    {
        this.store = store; this.isolated = isolated;
        try { libraryDisplay = (JsonSerializer.Deserialize<LibraryDisplay>(store.Setting("library-display", "{}")) ?? new()).Validated(); }
        catch (JsonException) { }
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
        foreach (var name in new[] { "Home", "My Books", "Recommendations", "Bibliographies", "Statistics", "Goals", "Settings" })
            nav.Children.Add(Ui.Button(name, () => { activePage = name; RefreshPage(); }));
        DockPanel.SetDock(nav, Dock.Top); root.Children.Add(nav);
        message.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        message.FontSize = 12; DockPanel.SetDock(message, Dock.Bottom); root.Children.Add(message);
        root.Children.Add(page); Content = root; RefreshPage();
        Closing += (_, args) => { if (busyCount > 0) args.Cancel = true; };
    }

    private void RefreshPage()
    {
        books = activePage == "My Books" ? [] : store.Books();
        if (activePage != "Home") homeReadingScroll = null;
        if (activePage != "Recommendations") recommendationsScroll = null;
        if (activePage != "Statistics") statisticsPage = null;
        message.Text = store.LastBackupError ?? (activePage == "My Books" ? "Saved on this computer  ·  Offline core" :
            $"{books.Count(b => !b.Excluded)} books  ·  Saved on this computer  ·  Offline core");
        page.Content = activePage switch
        {
            "My Books" => libraryPage ??= Library(), "Recommendations" => RecommendationsPage(),
            "Bibliographies" => Bibliographies(), "Statistics" => Statistics(),
            "Goals" => Goals(), "Settings" => SettingsPage(), _ => Home()
        };
    }

    private StatisticsPage Statistics()
    {
        var offset = statisticsPage?.VerticalOffset ?? 0;
        statisticsPage = new StatisticsPage(store, this, books, RefreshPage);
        statisticsPage.ScrollToVerticalOffset(offset);
        return statisticsPage;
    }

    private void Edit(Book book)
    {
        if (activePage == "My Books")
            book = store.FindBook(book.Id)
                ?? throw new InvalidOperationException("This book was removed. Refresh the library to update the results.");
        var editor = new BookEditor(store, book) { Owner = this };
        editor.ShowDialog();
        if (activePage != "My Books") RefreshPage();
    }

    private void AddBook() => AddBook("", "");

    private void AddBook(string title, string author)
    {
        var lookup = new MetadataLookupWindow(title, author, editBook: true, store: store, addBook: true) { Owner = this };
        if (lookup.ShowDialog() != true) return;
        if (lookup.ExistingBook is { } existing) { Edit(existing); return; }
        var editor = new BookEditor(store, lookup.ManualBook ?? new Book()) { Owner = this };
        if (lookup.SelectedMatch is { } match) editor.ApplyMetadata(match, lookup.SelectedEdition, lookup.SelectedCover);
        editor.ShowDialog();
        if (activePage != "My Books") RefreshPage();
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
        var sidebar = new StackPanel();
        AddShortList(sidebar, "Recently changed books", ReadingGoals.RecentlyChanged(books));
        var scroll = Ui.Scroll(sidebar); Grid.SetColumn(scroll, 1); columns.Children.Add(scroll); return columns;
    }

    private void AddReadingSection(Panel reading, string? title, List<Book> items, IReadOnlyList<ReadingSection> sections)
    {
        if (title != null) reading.Children.Add(Ui.Heading(title, 22));
        if (items.Count == 0) reading.Children.Add(Ui.Text("No books in this section yet.", 13, "MutedBrush"));
        foreach (var book in items)
        {
            var row = new DockPanel();
            var cover = Ui.Cover(book, store, 125, 185);
            cover.Margin = new Thickness(0, 0, 20, 0);
            DockPanel.SetDock(cover, Dock.Left);
            row.Children.Add(cover);
            var details = new StackPanel(); details.Children.Add(Ui.Heading(book.Title, 23));
            details.Children.Add(Ui.Text(book.Author, 15, "MutedBrush")); details.Children.Add(Ui.Text(ReadingRules.ProgressText(book), 15));
            var actions = new WrapPanel(); actions.Children.Add(Ui.Button("Update progress / open", () => Edit(book), true));
            actions.Children.Add(Ui.Button("Move up", () => Move(book, items, -1)));
            actions.Children.Add(Ui.Button("Move down", () => Move(book, items, 1)));
            if (sections.Count > 0) actions.Children.Add(ReadingSectionMenu(book, sections));
            details.Children.Add(actions);
            row.Children.Add(details);
            var card = Ui.Card(row);
            card.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount != 2) return;
                args.Handled = true;
                Ui.Guard(() => Edit(book));
            };
            reading.Children.Add(card);
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

    private void AddShortList(Panel parent, string title, IEnumerable<Book> source)
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Heading(title, 21)); var items = source.ToList();
        if (items.Count == 0) panel.Children.Add(Ui.Text("No books here yet.", 13, "MutedBrush"));
        foreach (var book in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
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
        var showCovers = false;
        var sortMode = LibrarySort.ToReadPosition;
        var sortDescending = false;
        var updatingSort = false;
        var root = new DockPanel(); var top = new StackPanel();
        var showAll = false;
        var resultSummary = Ui.Text("", 13, "MutedBrush");
        var showAllButton = Ui.Button("Show all books", () => { });
        var heading = new DockPanel();
        var libraryActions = new WrapPanel();
        var reload = Ui.Button("Refresh library", () => { });
        DockPanel.SetDock(libraryActions, Dock.Right);
        heading.Children.Add(libraryActions);
        heading.Children.Add(Ui.Heading("My Books"));
        top.Children.Add(heading);
        var findPanel = new StackPanel();
        var search = Ui.Input(findPanel, "Find title, author, or ISBN within these filters");
        top.Children.Add(new Expander { Header = "Find a specific book", Content = findPanel, Margin = new Thickness(0, 0, 0, 8) });
        var statusFilters = new StackPanel { Width = 170, Margin = new Thickness(0, 0, 18, 0) };
        statusFilters.Children.Add(Ui.Text("READING STATUS", 12, "MutedBrush"));
        ReadingStatus? statusFilter = ReadingStatus.ToRead;
        (string Label, ReadingStatus? Status)[] statusOptions =
        [
            ("All", null), ("Unclassified", ReadingStatus.Unclassified), ("To read", ReadingStatus.ToRead),
            ("Reading", ReadingStatus.CurrentlyReading), ("Read", ReadingStatus.Read), ("Partially read", ReadingStatus.PartiallyRead),
            ("Did not finish", ReadingStatus.DidNotFinish), ("Reference only", ReadingStatus.ReferenceOnly),
            ("Music book", ReadingStatus.MusicBook), ("Photography / art book", ReadingStatus.PhotographyArtBook)
        ];
        var statusButtons = new List<(RadioButton Button, ReadingStatus? Status)>();
        foreach (var (label, value) in statusOptions)
        {
            var button = new RadioButton { Content = label, IsChecked = value == ReadingStatus.ToRead, Style = (Style)FindResource("StatusFilter") };
            AutomationProperties.SetName(button, value == null ? "All reading statuses" : ReadingRules.Label(value.Value));
            statusFilters.Children.Add(button);
            statusButtons.Add((button, value));
        }
        DockPanel.SetDock(statusFilters, Dock.Left); root.Children.Add(statusFilters);
        var filters = new WrapPanel();
        var shelf = new ComboBox { Width = 195, Margin = new Thickness(0, 0, 12, 10) };
        var restoringShelf = false;
        var groups = new ComboBox { Width = 205, Margin = new Thickness(0, 0, 12, 10) };
        AutomationProperties.SetName(shelf, "Shelf filter");
        AutomationProperties.SetName(groups, "To-read group filter");
        LoadShelves();
        var excluded = new CheckBox { Content = "Show excluded records" };
        var missingCover = new CheckBox { Content = "Missing local cover", Margin = new Thickness(10, 4, 0, 10) };
        var bulkEdit = new CheckBox { Content = "Bulk edit", Margin = new Thickness(10, 4, 0, 10) };
        var views = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 0, 10) };
        views.Children.Add(Ui.Text("View", 13, "MutedBrush"));
        var listView = new RadioButton { Content = "Table", IsChecked = !showCovers, Margin = new Thickness(12, 0, 12, 0) };
        var coverView = new RadioButton { Content = "Covers", IsChecked = showCovers };
        foreach (var option in new[] { listView, coverView })
        {
            option.SetResourceReference(ForegroundProperty, "TextBrush");
            views.Children.Add(option);
        }
        filters.Children.Add(shelf); filters.Children.Add(groups); filters.Children.Add(excluded); filters.Children.Add(missingCover); filters.Children.Add(views);
        filters.Children.Add(bulkEdit);
        var display = Ui.Button("Display…", () => { });
        display.Margin = new Thickness(12, 0, 0, 8);
        filters.Children.Add(display);
        libraryActions.Children.Add(Ui.Button("Review duplicates…", () =>
        {
            new LibraryCleanupWindow(store) { Owner = this }.ShowDialog();
        }));
        libraryActions.Children.Add(Ui.Button("Merge two books…", () =>
        {
            var picker = new DuplicatePickerWindow(store.Books(), search.Text) { Owner = this };
            if (picker.ShowDialog() == true)
                new LibraryCleanupWindow(store, picker.Pair) { Owner = this }.ShowDialog();
        }));
        libraryActions.Children.Add(Ui.Button("Find covers and MSRP…", () =>
        {
            new LibraryEnrichmentWindow(store) { Owner = this }.ShowDialog();
        }));
        libraryActions.Children.Add(reload);
        top.Children.Add(filters);
        var sort = new WrapPanel();
        var sortLabel = Ui.Text("Sort by", 13, "MutedBrush");
        sortLabel.Margin = new Thickness(0, 8, 8, 0);
        sort.Children.Add(sortLabel);
        var sortField = new ComboBox { ItemsSource = new[] { "To-read position", "Date added", "Title", "Author" }, SelectedIndex = (int)sortMode, Width = 165 };
        AutomationProperties.SetName(sortField, "Sort by");
        sort.Children.Add(sortField);
        var ascending = new RadioButton { Content = "Ascending ↑", IsChecked = !sortDescending, Style = (Style)FindResource("StatusFilter"), Margin = new Thickness(8, 3, 6, 12) };
        var descending = new RadioButton { Content = "Descending ↓", IsChecked = sortDescending, Style = (Style)FindResource("StatusFilter"), Margin = new Thickness(0, 3, 0, 12) };
        AutomationProperties.SetName(ascending, "Ascending sort order");
        AutomationProperties.SetName(descending, "Descending sort order");
        sort.Children.Add(ascending);
        sort.Children.Add(descending);
        top.Children.Add(sort);
        var selected = new HashSet<Guid>();
        Guid? selectionAnchor = null;
        var selectionBoxes = new Dictionary<Guid, CheckBox>();
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
        ShelfSuggestions.Attach(bulkShelf, () => store.LibraryNames(), commaSeparated: false);
        bulkShelf.ToolTip = "Choose an existing shelf or type a new shelf name.";
        var bulkGroup = new TextBox { Width = 180, ToolTip = "Add selected books to a group. Existing memberships are kept." };
        AutomationProperties.SetName(bulkGroup, "Add to to-read group");
        ShelfSuggestions.Attach(bulkGroup, () => store.LibraryNames(groups: true), commaSeparated: false);
        var bulkAcquisition = new ComboBox
        {
            Width = 155, ItemsSource = new[] { "Keep gift/library", "Gift", "Library" },
            SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "Applies to the selected edition's most recent unsold copy. When all copies are sold, Gift marks the latest copy and Library adds a loan."
        };
        AutomationProperties.SetName(bulkAcquisition, "Gift or library");
        var apply = Ui.Button("Apply to selected", () => { }, true);
        var configure = Ui.Button("Configure selected…", () => { });
        var remove = Ui.Button("Remove selected…", () => { });
        var merge = Ui.Button("Review selected pair…", () =>
        {
            var pair = store.Books().Where(book => selected.Contains(book.Id)).ToArray();
            if (pair.Length != 2) return;
            new LibraryCleanupWindow(store, new(pair[0], pair[1], "Selected pair")) { Owner = this }.ShowDialog();
        });
        var results = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        configure.Click += (_, _) => Ui.Guard(() =>
        {
            if (new BulkBookEditor(store, selected) { Owner = this }.ShowDialog() == true)
                ReloadLibrary();
        });
        display.Click += (_, _) => Ui.Guard(() =>
        {
            var dialog = new LibraryDisplayWindow(libraryDisplay) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            libraryDisplay = dialog.Value;
            store.SetSetting("library-display", JsonSerializer.Serialize(libraryDisplay));
            ReloadLibrary();
        });
        reload.Click += (_, _) => Ui.Guard(ReloadLibrary);
        apply.Click += (_, _) => Ui.Guard(() =>
        {
            var shelfName = bulkShelf.Text.Trim();
            if (bulkStatus.SelectedIndex == 0 && shelfName.Length == 0 && bulkGroup.Text.Trim().Length == 0 && bulkAcquisition.SelectedIndex == 0)
                throw new InvalidOperationException("Choose a status, Gift or Library, or enter a shelf or group name.");
            var changes = store.Books().Where(book => selected.Contains(book.Id)).ToList();
            foreach (var book in changes)
            {
                if (bulkStatus.SelectedIndex > 0) ReadingRules.Classify(book, statuses[bulkStatus.SelectedIndex - 1]);
                if (shelfName.Length > 0) book.Shelves.Add(shelfName);
                if (bulkGroup.Text.Trim().Length > 0) book.ToReadGroups.Add(bulkGroup.Text.Trim());
                if (bulkAcquisition.SelectedIndex > 0)
                    AcquisitionRules.Mark(book, asGift: bulkAcquisition.SelectedIndex == 1);
            }
            store.SaveBatch(changes);
            selected.Clear();
            ReloadLibrary();
            message.Text = store.LastBackupError ?? $"Updated {changes.Count} books.";
        });
        bulk.Children.Add(selectedCount);
        bulk.Children.Add(Ui.Button("Select all shown", () => { selected.UnionWith(matches.Select(book => book.Id)); Populate(); }));
        bulk.Children.Add(Ui.Button("Clear selection", () => { selected.Clear(); selectionAnchor = null; Populate(); }));
        bulk.Children.Add(bulkStatus);
        bulk.Children.Add(bulkAcquisition);
        var shelfLabel = Ui.Text("Add to shelf", 13, "MutedBrush");
        shelfLabel.Margin = new Thickness(4, 8, 6, 8);
        var shelfEntry = new StackPanel { Orientation = Orientation.Horizontal };
        shelfEntry.Children.Add(shelfLabel);
        AutomationProperties.SetName(bulkShelf, "Add to shelf");
        shelfEntry.Children.Add(bulkShelf); bulk.Children.Add(shelfEntry);
        var groupEntry = new StackPanel { Orientation = Orientation.Horizontal };
        var groupLabel = Ui.Text("Add to group", 13, "MutedBrush"); groupLabel.Margin = new Thickness(4, 8, 6, 8);
        groupEntry.Children.Add(groupLabel); groupEntry.Children.Add(bulkGroup); bulk.Children.Add(groupEntry);
        bulk.Children.Add(apply);
        bulk.Children.Add(configure);
        remove.Click += async (_, _) =>
        {
            var booksToRemove = store.Books().Where(book => selected.Contains(book.Id)).ToList();
            if (booksToRemove.Count == 0) { ReloadLibrary(); return; }
            var confirmation = new BookRemovalWindow(booksToRemove) { Owner = this };
            if (confirmation.ShowDialog() != true) return;
            BookRemovalResult? outcome = null;
            await Busy(() => store.RemoveBooks(booksToRemove, confirmation.CalibrePlan), result =>
            {
                outcome = result;
                selected.Clear();
                ReloadLibrary();
                if (result.Warning != null)
                    MessageBox.Show(this, result.Warning, "Book removal", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            if (outcome != null) message.Text = store.LastBackupError ?? $"Removed {outcome.Removed} books.";
        };
        bulk.Children.Add(remove);
        bulk.Children.Add(merge);
        top.Children.Add(bulk);
        showAllButton.Click += (_, _) => { showAll = !showAll; Populate(); };
        var paging = new WrapPanel(); paging.Children.Add(resultSummary); paging.Children.Add(showAllButton);
        top.Children.Add(paging);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        root.Children.Add(results);
        void LoadShelves()
        {
            var previous = shelf.SelectedIndex > 0 ? shelf.SelectedItem as string : null;
            var names = store.LibraryNames().ToList();
            if (previous != null) names.Insert(0, previous);
            restoringShelf = true;
            shelf.ItemsSource = new[] { "All shelves" }.Concat(names.Distinct(StringComparer.OrdinalIgnoreCase).Order()).ToArray();
            if (previous == null) shelf.SelectedIndex = 0;
            else shelf.SelectedItem = previous;
            var previousGroup = groups.SelectedIndex > 0 ? groups.SelectedItem as string : null;
            groups.ItemsSource = new[] { "All to-read groups" }.Concat(store.LibraryNames(groups: true)).ToArray();
            if (previousGroup == null) groups.SelectedIndex = 0; else groups.SelectedItem = previousGroup;
            restoringShelf = false;
        }

        void ReloadLibrary()
        {
            var viewport = FindScrollViewer(results.Content as DependencyObject);
            var offset = viewport?.VerticalOffset ?? 0;
            var selectedBook = (results.Content as DataGrid)?.SelectedItem as LibraryTableRow;
            var anchor = viewport?.CanContentScroll == true && (int)offset < matches.Count ? matches[(int)offset].Id : (Guid?)null;
            LoadShelves();
            Populate();
            if (results.Content is DataGrid table && selectedBook != null && bulkEdit.IsChecked != true)
                table.SelectedItem = table.Items.OfType<LibraryTableRow>().FirstOrDefault(item => item.Book.Id == selectedBook.Book.Id);
            var anchorIndex = anchor.HasValue ? matches.FindIndex(book => book.Id == anchor.Value) : -1;
            if (anchorIndex >= 0) offset = anchorIndex;
            results.UpdateLayout();
            FindScrollViewer(results.Content as DependencyObject)?.ScrollToVerticalOffset(offset);
            message.Text = store.LastBackupError ?? "Library refreshed.";
        }
        void UpdateSelection()
        {
            selectedCount.Text = $"{selected.Count} selected";
            apply.IsEnabled = selected.Count > 0;
            configure.IsEnabled = selected.Count > 0;
            remove.IsEnabled = selected.Count > 0;
            merge.IsEnabled = selected.Count == 2;
        }
        CheckBox SelectBook(Book book)
        {
            var check = new CheckBox { Content = "Select", IsChecked = selected.Contains(book.Id), Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(check, $"Select {book.Title}");
            void ChangeSelection(object sender, RoutedEventArgs args)
            {
                var range = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                LibrarySelection.Change(selected, matches.Select(b => b.Id).ToArray(), selectionAnchor,
                    book.Id, check.IsChecked == true, range);
                if (!range || selectionAnchor == null) selectionAnchor = book.Id;
                foreach (var (id, box) in selectionBoxes) box.IsChecked = selected.Contains(id);
                UpdateSelection();
            }
            check.Click += ChangeSelection;
            selectionBoxes[book.Id] = check;
            return check;
        }
        void Populate()
        {
            var pageResult = store.Browse(new LibraryQuery(statusFilter,
                shelf.SelectedIndex > 0 ? shelf.SelectedItem as string : null,
                groups.SelectedIndex > 0 ? groups.SelectedItem as string : null,
                search.Text, false, sortDescending, excluded.IsChecked == true,
                missingCover.IsChecked == true, showAll ? null : 50, sortMode));
            matches = pageResult.Books;
            resultSummary.Text = $"{matches.Count} of {pageResult.Total} matching books  ·  ";
            showAllButton.Content = showAll ? "Show first 50" : "Show all books";
            showAllButton.Visibility = showAll || pageResult.Total > 50 ? Visibility.Visible : Visibility.Collapsed;
            selected.IntersectWith(matches.Select(book => book.Id));
            if (selectionAnchor.HasValue && matches.All(book => book.Id != selectionAnchor.Value)) selectionAnchor = null;
            UpdateSelection();
            if (matches.Count == 0)
            {
                results.Content = Ui.Card(Ui.Text("No matching books. Add one or change the filters.", 15, "MutedBrush"));
                return;
            }
            Func<Book, CheckBox>? selector = bulkEdit.IsChecked == true ? SelectBook : null;
            selectionBoxes.Clear();
            results.Content = showCovers ? LibraryCovers(matches, selector) : LibraryTable.Create(matches, bulkEdit.IsChecked == true,
                selected, UpdateSelection, Edit, value =>
                {
                    if (sortMode == value) sortDescending = !sortDescending;
                    else { sortMode = value; sortDescending = false; }
                    updatingSort = true;
                    sortField.SelectedIndex = (int)sortMode;
                    ascending.IsChecked = !sortDescending; descending.IsChecked = sortDescending;
                    updatingSort = false;
                    Populate();
                }, sortMode, sortDescending);
        }
        foreach (var (button, value) in statusButtons)
            button.Checked += (_, _) => { statusFilter = value; showAll = false; Populate(); };
        var searchDelay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); showAll = false; Populate(); };
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        shelf.SelectionChanged += (_, _) => { if (!restoringShelf) { showAll = false; Populate(); } };
        groups.SelectionChanged += (_, _) => { if (!restoringShelf) { showAll = false; Populate(); } };
        sortField.SelectionChanged += (_, _) => { if (!updatingSort) { sortMode = (LibrarySort)sortField.SelectedIndex; Populate(); } };
        ascending.Checked += (_, _) => { if (!updatingSort) { sortDescending = false; Populate(); } };
        descending.Checked += (_, _) => { if (!updatingSort) { sortDescending = true; Populate(); } };
        excluded.Click += (_, _) => Populate();
        missingCover.Click += (_, _) => Populate();
        bulkEdit.Click += (_, _) =>
        {
            bulk.Visibility = bulkEdit.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            selected.Clear();
            selectionAnchor = null;
            Populate();
        };
        listView.Checked += (_, _) => { showCovers = false; Populate(); };
        coverView.Checked += (_, _) => { showCovers = true; Populate(); };
        Populate(); return root;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? view)
    {
        if (view is ScrollViewer scroll) return scroll;
        if (view == null) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(view); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(view, index)) is { } child) return child;
        return null;
    }

    private UIElement RecommendationsPage()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(Ui.Heading("Recommendations"));
        var picks = Recommendations.Overview(books);
        if (!picks.HasRatedBooks)
            panel.Children.Add(Ui.Text("Rate a finished book to start getting personalized picks.", 14, "MutedBrush"));
        AddRecommendationSection(panel, "Top picks from To read", picks.ToRead);
        AddRecommendationSection(panel, "Owned and unread", picks.OwnedUnread);
        AddCitedRecommendationSection(panel, picks);
        var offset = recommendationsScroll?.VerticalOffset ?? 0;
        recommendationsScroll = Ui.Scroll(panel);
        recommendationsScroll.ScrollToVerticalOffset(offset);
        return recommendationsScroll;
    }

    private void AddCitedRecommendationSection(Panel panel, RecommendationOverview overview)
    {
        var section = new StackPanel { Margin = new Thickness(0, 10, 0, 22) };
        section.Children.Add(Ui.Heading("Cited by books you enjoyed", 22));
        panel.Children.Add(section);
        var now = DateTimeOffset.Now;
        var shown = overview.ToRead.Concat(overview.OwnedUnread).Select(pick => pick.Book.Id).ToHashSet();
        var authors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picks = CitedRecommendations.Rank(books, ReadCitations, now)
            .OrderBy(pick => pick.LocalBook is { } book && shown.Contains(book.Id))
            .Where(pick =>
            {
                var preference = store.Setting(CitationPreferenceKey(pick), "");
                return preference != "dismissed" &&
                    !(long.TryParse(preference, out var until) && until > now.ToUnixTimeSeconds()) &&
                    authors.Add(pick.Author);
            })
            .Take(Recommendations.PicksPerSection).ToArray();
        if (picks.Length == 0)
        {
            section.Children.Add(Ui.Text("No cited books to suggest yet. Picks come from finished books rated four or five stars.", 14, "MutedBrush"));
            return;
        }

        var grid = new UniformGrid { Columns = 2 };
        section.SizeChanged += (_, args) => grid.Columns = args.NewSize.Width >= 1450 ? 3 : 2;
        section.Children.Add(grid);
        foreach (var pick in picks)
        {
            var card = new StackPanel();
            grid.Children.Add(Ui.Card(card));
            var title = Ui.Heading(pick.Title, 19);
            title.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(title);
            card.Children.Add(Ui.Text(pick.Author, 13, "MutedBrush"));
            var evidence = pick.Sources[0];
            var otherSources = pick.Sources.Count == 1 ? "" : $" + {pick.Sources.Count - 1} more";
            var source = Ui.Button($"Cited in {evidence.Source.Title}{otherSources}", () =>
                OpenBibliography(evidence.Source, evidence.Citation));
            source.Content = new TextBlock { Text = (string)source.Content, TextWrapping = TextWrapping.Wrap };
            source.HorizontalAlignment = HorizontalAlignment.Left;
            source.ToolTip = string.Join("\n", pick.Sources.Select(item => $"{item.Source.Title} · {item.Source.Rating}/5\n{item.Citation.Text}"));
            card.Children.Add(source);
            var actions = new WrapPanel();
            card.Children.Add(actions);
            if (pick.LocalBook is { } book)
                actions.Children.Add(Ui.Button("Open in My Books", () => Edit(book)));
            else
                actions.Children.Add(Ui.Button("Find or add book…", () => AddBook(pick.Title, pick.Author)));
            actions.Children.Add(Ui.Button("Not now", () => SaveCitationPreference(pick, dismiss: false)));
            actions.Children.Add(Ui.Button("Not interested", () => SaveCitationPreference(pick, dismiss: true)));
        }

        IEnumerable<EpubCitation> ReadCitations(Guid id) =>
            (store.ReadEpubBibliography(id)?.Entries ?? []).Concat(store.ReadOnlineBibliography(id)?.Entries ?? [])
                .Concat(CitedRecommendations.ReadText(store.Bibliography(id).Text));
    }

    private static string CitationPreferenceKey(CitedRecommendation pick) =>
        "citation-recommendation:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pick.Key)));

    private void SaveCitationPreference(CitedRecommendation pick, bool dismiss)
    {
        var now = DateTimeOffset.Now;
        if (pick.LocalBook is { } book && store.Books().Find(saved => saved.Id == book.Id) is { } saved)
        {
            if (dismiss) Recommendations.Dismiss(saved);
            else Recommendations.Snooze(saved, now);
            store.Save(saved);
        }
        else
        {
            store.SetSetting(CitationPreferenceKey(pick), dismiss ? "dismissed" : now.AddYears(1).ToUnixTimeSeconds().ToString());
        }
        RefreshPage();
    }

    private void AddRecommendationSection(Panel panel, string title, IReadOnlyList<Recommendation> picks)
    {
        var section = new StackPanel { Margin = new Thickness(0, 10, 0, 22) };
        section.Children.Add(Ui.Heading(title, 22));
        panel.Children.Add(section);
        if (picks.Count == 0)
        {
            section.Children.Add(Ui.Text("No matching books yet.", 14, "MutedBrush"));
            return;
        }

        var grid = new UniformGrid { Columns = 5, HorizontalAlignment = HorizontalAlignment.Left };
        section.SizeChanged += (_, args) =>
        {
            var wide = args.NewSize.Width >= 1800;
            grid.Columns = wide ? 10 : 5;
            grid.Width = Math.Min(args.NewSize.Width, wide ? 2120 : 1060);
        };
        foreach (var pick in picks)
        {
            var book = pick.Book;
            var tile = Ui.Button(book.Title, () => Edit(book));
            tile.Padding = new Thickness(8, 12, 8, 10);
            tile.Margin = new Thickness(0, 0, 0, 6);
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(tile, $"Open {book.Title}");

            var content = new StackPanel { MaxWidth = 178 };
            var cover = Ui.Cover(book, store, 140, 208);
            cover.HorizontalAlignment = HorizontalAlignment.Center;
            cover.Margin = new Thickness(0, 0, 0, 9);
            content.Children.Add(cover);
            var name = Ui.Heading(book.Title, 16);
            name.Height = 42;
            name.Margin = new Thickness(0, 0, 0, 2);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(name);
            var author = Ui.Text(book.Author, 12, "MutedBrush");
            author.Margin = new Thickness(0);
            author.TextWrapping = TextWrapping.NoWrap;
            author.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(author);
            tile.Content = content;

            var why = string.Join("\n", pick.Reasons.Select(reason =>
                $"{reason.Label} · {reason.AverageRating:0.#}/5 from {reason.MatchedBooks} rated {(reason.MatchedBooks == 1 ? "book" : "books")}"));
            AutomationProperties.SetHelpText(tile, why);
            var tooltip = new ToolTip { Content = Ui.Text($"{book.Title}\n{why}", 13), MaxWidth = 380 };
            tooltip.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            tooltip.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
            tile.ToolTip = tooltip;
            var card = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            card.Children.Add(tile);
            var actions = new WrapPanel();
            AddAction("Not now", "Hide this book from recommendations for one year.", book => Recommendations.Snooze(book, DateTimeOffset.Now));
            AddAction("Not interested", "Remove this book from To read and future recommendations. Keep its library record and holdings.", Recommendations.Dismiss);
            card.Children.Add(actions);
            grid.Children.Add(card);

            void AddAction(string label, string description, Action<Book> change)
            {
                var action = Ui.Button(label, () =>
                {
                    var saved = store.Books().Find(item => item.Id == book.Id);
                    if (saved != null)
                    {
                        change(saved);
                        store.Save(saved);
                    }
                    RefreshPage();
                });
                action.FontSize = 12;
                action.Padding = new Thickness(7, 5, 7, 5);
                action.Margin = new Thickness(0, 0, 6, 4);
                action.ToolTip = description;
                AutomationProperties.SetName(action, $"{label}: {book.Title}");
                actions.Children.Add(action);
            }
        }
        section.Children.Add(grid);
    }

    private ScrollViewer LibraryCovers(IEnumerable<Book> matches, Func<Book, CheckBox>? selector)
    {
        var grid = new WrapPanel { ItemWidth = 176, ItemHeight = selector == null ? 290 : 318 };
        var cards = new List<(FrameworkElement Labels, FrameworkElement Cover, Button Tile)>();
        foreach (var book in matches)
        {
            var check = selector?.Invoke(book);
            var tile = Ui.Button(book.Title, () =>
            {
                if (check == null) Edit(book);
                else
                {
                    check.IsChecked = check.IsChecked != true;
                    check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            });
            tile.Padding = new Thickness(10);
            tile.ToolTip = $"{book.Title}\n{book.Author}\n{BookStatus(book)}";
            AutomationProperties.SetName(tile, check == null ? $"Open {book.Title}" : $"Toggle selection for {book.Title}");
            var labels = new StackPanel { Width = 142 };
            var cover = Ui.Cover(book, store, 110, 162);
            cover.HorizontalAlignment = HorizontalAlignment.Center;
            cover.Margin = new Thickness(0, 0, 0, 8);
            labels.Children.Add(cover);
            var name = Ui.Heading(book.Title, libraryDisplay.TitleSize);
            name.Height = libraryDisplay.TitleSize * 2.6;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(0, 0, 0, 3);
            labels.Children.Add(name);
            var author = Ui.Text(book.Author, libraryDisplay.DetailSize, "MutedBrush");
            author.TextWrapping = TextWrapping.NoWrap;
            author.TextTrimming = TextTrimming.CharacterEllipsis;
            author.Margin = new Thickness(0, 0, 0, 2);
            labels.Children.Add(author);
            var status = Ui.Text(BookStatus(book), libraryDisplay.DetailSize, "MutedBrush");
            status.Margin = new Thickness(0);
            status.Height = libraryDisplay.DetailSize * 2.6;
            labels.Children.Add(status);
            tile.Content = labels;
            cards.Add((labels, cover, tile));
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
        void ResizeCards()
        {
            if (grid.ActualWidth <= 0) return;
            var columns = Math.Min(libraryDisplay.Columns, Math.Max(1, (int)(grid.ActualWidth / 150)));
            var width = Math.Floor(grid.ActualWidth / columns);
            var coverWidth = Math.Clamp(width - 44, 80, 190);
            var coverHeight = coverWidth * 1.48;
            grid.ItemWidth = width;
            var height = coverHeight + libraryDisplay.TitleSize * 2.6 + libraryDisplay.DetailSize * 4 + 44;
            grid.ItemHeight = height + (selector == null ? 0 : 30);
            foreach (var card in cards)
            {
                card.Labels.Width = Math.Max(80, width - 34);
                card.Cover.Width = coverWidth; card.Cover.Height = coverHeight;
                card.Tile.Height = height - 8;
            }
        }
        grid.SizeChanged += (_, _) => ResizeCards();
        grid.Loaded += (_, _) => ResizeCards();
        return Ui.Scroll(grid);
    }

    private static string BookStatus(Book book) =>
        ReadingRules.Label(book.Status) + (book.Rating is { } stars ? $"  ·  {stars}/5 stars" : "") +
        (book.Excluded ? "  ·  EXCLUDED" : "");

    private UIElement Bibliographies()
    {
        var root = new DockPanel();
        var header = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
        header.Children.Add(Ui.Heading("Bibliographies"));
        var actions = new WrapPanel(); header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var add = Ui.Button("Add Bibliography", () =>
        {
            if (ChooseBibliographyBook() is not { } book) return;
            if (new BibliographyTextWindow(store, book, startEditing: true) { Owner = this }.ShowDialog() == true)
                RefreshPage();
        }, true);
        DockPanel.SetDock(add, Dock.Right);
        actions.Children.Add(add);
        var photos = Ui.Button("Import bibliography photos…", () =>
        {
            if (ChooseBibliographyBook("Import bibliography photos") is not { } book) return;
            if (new BibliographyTextWindow(store, book, startEditing: true, scanOnOpen: true) { Owner = this }.ShowDialog() == true)
                RefreshPage();
        });
        actions.Children.Add(photos);
        var cited = Ui.Button("Cited books in My Books", () =>
            new LocalCitedBooksWindow(store, (book, citation) => OpenBibliography(book, citation)) { Owner = this }.ShowDialog());
        actions.Children.Add(cited);
        var findOnline = Ui.Button("Find Bibliography Online", () =>
        {
            if (ChooseBibliographyBook("Find Bibliography Online") is { } book &&
                new PublicBibliographyWindow(store, book) { Owner = this }.ShowDialog() == true)
                RefreshPage();
        });
        DockPanel.SetDock(findOnline, Dock.Right);
        actions.Children.Add(findOnline);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        var found = books.Where(book => File.Exists(store.BibliographyPath(book.Id)) || store.HasEpubBibliography(book.Id) ||
                store.HasOnlineBibliography(book.Id))
            .OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(book => book.Author, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var book in found)
        {
            var button = Ui.Button(book.Title, () => OpenBibliography(book));
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(14, 10, 14, 10);
            button.ToolTip = $"{book.Title} · {book.Author}";
            AutomationProperties.SetName(button, $"{book.Title} · {book.Author}");
            var label = new StackPanel();
            var title = Ui.Heading(book.Title, 19);
            title.TextWrapping = TextWrapping.Wrap;
            title.LineHeight = 23;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.MaxHeight = 46;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(0, 0, 0, 2);
            label.Children.Add(title);
            var author = Ui.Text(book.Author, 13, "MutedBrush");
            author.TextTrimming = TextTrimming.CharacterEllipsis;
            author.Margin = new Thickness(0);
            label.Children.Add(author);
            button.Content = label;
            grid.Children.Add(button);
        }
        var previousOffset = bibliographiesScroll?.VerticalOffset ?? 0;
        var scroll = Ui.Scroll(found.Length > 0 ? grid : Ui.Text("No saved bibliographies yet."));
        scroll.SizeChanged += (_, args) => grid.Columns = args.NewSize.Width >= 1450 ? 3 : 2;
        scroll.ScrollToVerticalOffset(previousOffset);
        bibliographiesScroll = scroll;
        root.Children.Add(scroll);
        return root;
    }

    private void OpenBibliography(Book book, EpubCitation? citation = null)
    {
        var local = store.ReadEpubBibliography(book.Id);
        var online = store.ReadOnlineBibliography(book.Id);
        var document = citation == null ? local ?? online : local?.Entries.Contains(citation) == true ? local
            : online?.Entries.Contains(citation) == true ? online : null;
        Window bibliography = document != null
            ? new EpubBibliographyWindow(store, book, document, citation)
            : new BibliographyTextWindow(store, book);
        bibliography.Owner = this;
        bibliography.ShowDialog();
    }

    private Book? ChooseBibliographyBook(string title = "Add Bibliography")
    {
        var picker = new Window
        {
            Title = title, Owner = this, Width = 630, Height = 520, MinWidth = 430, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Style = (Style)Application.Current.FindResource(typeof(Window))
        };
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel();
        var search = Ui.Input(header, "Search My Books for the source book");
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var list = new ListBox
        {
            ItemTemplate = Ui.WrappedLabel(nameof(BibliographySource.Label))
        };
        AutomationProperties.SetName(list, "Source books");
        var open = Ui.Button("Continue", () => { if (list.SelectedItem != null) picker.DialogResult = true; }, true);
        open.IsDefault = true;
        DockPanel.SetDock(open, Dock.Bottom);
        root.Children.Add(open);
        root.Children.Add(list);
        picker.Content = root;
        var sources = store.Books().OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(book => new BibliographySource(book)).ToArray();
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => open.IsEnabled = list.SelectedItem != null;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) picker.DialogResult = true; };
        picker.Loaded += (_, _) => search.Focus();
        Filter();
        return picker.ShowDialog() == true && list.SelectedItem is BibliographySource selected ? selected.Book : null;

        void Filter()
        {
            list.ItemsSource = sources.Where(source => source.Label.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            list.SelectedIndex = 0;
            open.IsEnabled = list.SelectedItem != null;
        }
    }

    private sealed record BibliographySource(Book Book)
    {
        public string Label => $"{Book.Title} · {Book.Author}";
    }

    private UIElement Goals() => new GoalsPage(store, books, Edit, goalsYear, year => goalsYear = year);

    private UIElement SettingsPage()
    {
        var panel = new StackPanel { MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Ui.Heading("Make yourself at home"));
        var themes = new WrapPanel(); themes.Children.Add(Ui.Button("Light mode", () => { store.SetSetting("theme", "light"); App.ApplyTheme(false); }));
        themes.Children.Add(Ui.Button("Dark mode", () => { store.SetSetting("theme", "dark"); App.ApplyTheme(true); })); panel.Children.Add(themes);
        panel.Children.Add(CalibreSettings());
        panel.Children.Add(GoodreadsSettings());
        panel.Children.Add(Ui.Button("Recover saved Goodreads order and dates…", () =>
        {
            var preview = GoodreadsOrder.Preview(store.Books());
            if (preview.Changes.Length == 0)
            { MessageBox.Show(this, "No missing positions or dates can be recovered from the saved Goodreads fields.", "Goodreads order"); return; }
            if (MessageBox.Show(this, $"Recover {preview.Positions} To-read positions and {preview.Dates} dates across {preview.Changes.Length} books?\n\nOnly missing fields are filled. A verified backup is created first. {preview.MissingToReadPositions} To-read books have no saved canonical position.",
                "Recover saved Goodreads order", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var backup = store.ApplyGoodreadsOrder(preview); libraryPage = null;
            RefreshPage(); message.Text = $"Recovered Goodreads order and dates. Backup: {backup}";
        }));
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
        var recoverRatings = Ui.Button("Recover Goodreads ratings…", () => { });
        recoverRatings.Click += async (_, _) =>
        {
            var csv = new OpenFileDialog { Title = "Choose your Goodreads library export", Filter = "CSV files (*.csv)|*.csv" };
            if (csv.ShowDialog(this) != true) return;
            var zip = new OpenFileDialog { Title = "Choose the Goodreads account-data ZIP with your ratings", Filter = "ZIP archives (*.zip)|*.zip" };
            if (zip.ShowDialog(this) != true) return;
            GoodreadsRatingsPreview? preview = null;
            await Busy(() => GoodreadsRatings.Preview(store, csv.FileName, zip.FileName), result => preview = result);
            if (preview == null || new GoodreadsRatingsWindow(preview) { Owner = this }.ShowDialog() != true) return;
            await Busy(() => { store.ApplyGoodreadsRatings(preview); return true; }, _ =>
            {
                books = store.Books();
                summary.Text = $"Added {preview.Changes.Count} Goodreads ratings.";
            });
        };
        section.Children.Add(recoverRatings);
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
                if (result.ComicsSkipped > 0) summary.Text += $"\n{result.ComicsSkipped} comics or manga were skipped.";
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
