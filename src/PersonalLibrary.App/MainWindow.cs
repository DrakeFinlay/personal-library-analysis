using System.Diagnostics;
using System.IO;
using System.Windows;
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
    private List<Book> books = [];
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
        var add = Ui.Button("+ Add a book", () => Edit(new Book()), true); DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
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
        message.Text = store.LastBackupError ?? $"{books.Count(b => !b.Excluded)} books  ·  Saved on this computer  ·  Offline core";
        page.Content = activePage switch { "My Books" => Library(), "Bibliographies" => Bibliographies(), "Settings" => SettingsPage(), _ => Home() };
    }

    private void Edit(Book book)
    {
        var editor = new BookEditor(store, book) { Owner = this };
        editor.ShowDialog(); RefreshPage();
    }

    private UIElement Home()
    {
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        var reading = new StackPanel(); reading.Children.Add(Ui.Heading("A little time for a good book"));
        reading.Children.Add(Ui.Text("CURRENTLY READING", 12, "MutedBrush"));
        var current = books.Where(b => !b.Excluded && b.Status == ReadingStatus.CurrentlyReading).ToList();
        if (current.Count == 0)
        {
            var empty = new StackPanel(); empty.Children.Add(Ui.Heading("Your next chapter starts here", 22));
            empty.Children.Add(Ui.Text("Add a book or open one from My Books, then start a reading."));
            empty.Children.Add(Ui.Button("Browse My Books", () => { activePage = "My Books"; RefreshPage(); }));
            reading.Children.Add(Ui.Card(empty));
        }
        foreach (var book in current)
        {
            var row = new DockPanel(); var cover = Ui.Cover(book, store, 100, 148); DockPanel.SetDock(cover, Dock.Left); row.Children.Add(cover);
            var details = new StackPanel(); details.Children.Add(Ui.Heading(book.Title, 23));
            details.Children.Add(Ui.Text(book.Author, 15, "MutedBrush")); details.Children.Add(Ui.Text(ReadingRules.ProgressText(book), 15));
            var actions = new WrapPanel(); actions.Children.Add(Ui.Button("Update progress / open", () => Edit(book), true));
            actions.Children.Add(Ui.Button("Move up", () => Move(book, current, -1)));
            actions.Children.Add(Ui.Button("Move down", () => Move(book, current, 1)));
            details.Children.Add(actions); row.Children.Add(details); reading.Children.Add(Ui.Card(row));
        }
        columns.Children.Add(Ui.Scroll(reading));
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

    private void AddShortList(Panel parent, string title, IEnumerable<Book> source, bool reorder)
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Heading(title, 21)); var items = source.ToList();
        if (items.Count == 0) panel.Children.Add(Ui.Text("No books here yet.", 13, "MutedBrush"));
        foreach (var book in items)
        {
            var button = Ui.Button(book.Title + "\n" + book.Author, () => Edit(book));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Content = new TextBlock { Text = book.Title + "\n" + book.Author, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };
            panel.Children.Add(button);
            if (reorder)
            {
                var row = new WrapPanel(); row.Children.Add(Ui.Button("↑", () => Move(book, items, -1)));
                row.Children.Add(Ui.Button("↓", () => Move(book, items, 1))); panel.Children.Add(row);
            }
        }
        parent.Children.Add(Ui.Card(panel));
    }

    private void Move(Book book, List<Book> items, int offset)
    {
        var index = items.FindIndex(b => b.Id == book.Id); var other = index + offset;
        if (other < 0 || other >= items.Count) return;
        (items[index], items[other]) = (items[other], items[index]);
        for (var i = 0; i < items.Count; i++) { items[i].SortOrder = i; store.Save(items[i]); }
        RefreshPage();
    }

    private UIElement Library()
    {
        var root = new DockPanel(); var top = new StackPanel(); top.Children.Add(Ui.Heading("My Books"));
        var search = Ui.Input(top, "Search titles, authors, ISBNs, or shelves");
        var filters = new WrapPanel();
        var status = new ComboBox { Width = 195, ItemsSource = new[] { "All statuses" }.Concat(Enum.GetValues<ReadingStatus>().Select(ReadingRules.Label)), SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 10) };
        var shelf = new ComboBox { Width = 195, ItemsSource = new[] { "All shelves" }.Concat(books.SelectMany(b => b.Shelves).Distinct(StringComparer.OrdinalIgnoreCase).Order()), SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 10) };
        var excluded = new CheckBox { Content = "Show excluded records" };
        filters.Children.Add(status); filters.Children.Add(shelf); filters.Children.Add(excluded); top.Children.Add(filters);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var list = new ListBox(); root.Children.Add(list);
        void Populate()
        {
            list.Items.Clear();
            foreach (var book in books.Where(b => (excluded.IsChecked == true || !b.Excluded) &&
                (status.SelectedIndex == 0 || ReadingRules.Label(b.Status) == status.SelectedItem?.ToString()) &&
                (shelf.SelectedIndex == 0 || b.Shelves.Contains(shelf.SelectedItem?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)) &&
                ($"{b.Title} {b.Author} {string.Join(' ', b.Shelves)} {string.Join(' ', b.Editions.Select(e => e.Isbn))}").Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new DockPanel(); var open = Ui.Button("Open", () => Edit(book)); DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
                var labels = new StackPanel(); labels.Children.Add(Ui.Heading(book.Title, 20));
                labels.Children.Add(Ui.Text($"{book.Author}  ·  {ReadingRules.Label(book.Status)}{(book.Excluded ? "  ·  EXCLUDED" : "")}", 13, "MutedBrush"));
                if (book.Shelves.Count > 0) labels.Children.Add(Ui.Text(string.Join("  /  ", book.Shelves), 12, "MutedBrush"));
                row.Children.Add(labels); list.Items.Add(new ListBoxItem { Content = row, Tag = book });
            }
            if (list.Items.Count == 0) list.Items.Add(Ui.Text("No matching books. Add one or change the filters.", 15, "MutedBrush"));
        }
        search.TextChanged += (_, _) => Populate(); status.SelectionChanged += (_, _) => Populate(); shelf.SelectionChanged += (_, _) => Populate();
        excluded.Click += (_, _) => Populate();
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: Book book }) Edit(book); };
        Populate(); return root;
    }

    private UIElement Bibliographies()
    {
        var panel = new StackPanel(); panel.Children.Add(Ui.Heading("Bibliographies"));
        panel.Children.Add(Ui.Text("Paste references in a book's Bibliography tab. Each document stays in your profile as editable Markdown.", 14, "MutedBrush"));
        var found = books.Where(b => File.Exists(store.BibliographyPath(b.Id))).ToList();
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
        panel.Children.Add(Ui.Text("Stage A · Offline reading tracker. Online lookup, importers, advanced statistics, recommendations, and visual browsing are scheduled in later stages.", 14, "MutedBrush"));
        return Ui.Scroll(panel);
    }

    private async Task Busy<T>(Func<T> work, Action<T> completed)
    {
        busyCount++; IsEnabled = false; message.Text = "Working…";
        try { var result = await Task.Run(work); completed(result); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Personal Library", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { busyCount--; IsEnabled = busyCount == 0; if (busyCount == 0) message.Text = store.LastBackupError ?? "Ready · changes stay on this computer"; }
    }
}
