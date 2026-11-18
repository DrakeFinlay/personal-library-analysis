using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class StatisticsPage : ScrollViewer
{
    private const string PublicationTitle = "Publication year vs. completion date";
    private readonly LibraryStore store;
    private readonly Window owner;
    private readonly Action refresh;

    public StatisticsPage(LibraryStore store, Window owner, IReadOnlyList<Book> books, Action refresh)
    {
        this.store = store;
        this.owner = owner;
        this.refresh = refresh;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        var statistics = new ReadingStatistics(books);
        var body = new StackPanel { MaxWidth = 1440, HorizontalAlignment = HorizontalAlignment.Left };
        body.SetBinding(WidthProperty, new Binding(nameof(ViewportWidth)) { Source = this });
        Content = body;
        body.Children.Add(Ui.Heading("Statistics"));

        var lifetime = new StackPanel();
        lifetime.Children.Add(Ui.Heading("Lifetime pages read", 20));
        var lifetimePages = statistics.LifetimePages;
        lifetime.Children.Add(Ui.Heading(lifetimePages.WithPageCount == 0 ? "No page totals yet" : $"{lifetimePages.KnownPages:N0} pages", 32));
        lifetime.Children.Add(Ui.Text($"{lifetimePages.WithPageCount:N0} completed readings with page counts · " +
            $"{lifetimePages.WithoutPageCount:N0} missing page counts", 13, "MutedBrush"));
        lifetime.Children.Add(Ui.Text($"Includes undated readings with known page counts. {statistics.UndatedCompletions:N0} undated completions in your history.", 13, "MutedBrush"));
        body.Children.Add(Ui.Card(lifetime));

        var annual = new Grid();
        annual.ColumnDefinitions.Add(new ColumnDefinition());
        annual.ColumnDefinitions.Add(new ColumnDefinition());
        annual.Children.Add(AnnualChart(statistics.Years, showPages: false));
        var pages = AnnualChart(statistics.Years, showPages: true);
        Grid.SetColumn(pages, 1);
        annual.Children.Add(pages);
        body.Children.Add(annual);

        var publication = new StackPanel();
        var publicationHeader = new DockPanel();
        if (statistics.PublicationPoints.Count > 0)
        {
            var expand = Ui.Button("Full screen", () => ShowPublicationFullScreen(statistics.PublicationPoints));
            DockPanel.SetDock(expand, Dock.Right);
            publicationHeader.Children.Add(expand);
        }
        publicationHeader.Children.Add(Ui.Heading(PublicationTitle, 22));
        publication.Children.Add(publicationHeader);
        var yearOnly = statistics.YearOnlyCompletions == 0 ? "" : $"{statistics.YearOnlyCompletions:N0} with only a completion year · ";
        publication.Children.Add(Ui.Text($"Outside the plot · {statistics.UndatedCompletions:N0} undated · {yearOnly}" +
            $"{statistics.DatedWithoutPublicationYear:N0} dated with unknown publication year", 13, "MutedBrush"));
        if (statistics.PublicationPoints.Count == 0)
            publication.Children.Add(Ui.Text("Add an original publication year to a book with a dated completion to plot it.", 14, "MutedBrush"));
        else
            publication.Children.Add(new PublicationScatterplot(statistics.PublicationPoints,
                readings => { if (OpenPoint(readings, owner)) refresh(); },
                () => ShowPublicationFullScreen(statistics.PublicationPoints)));
        body.Children.Add(Ui.Card(publication));

        body.Children.Add(Ui.Heading("Reading highlights", 24));
        body.Children.Add(CardRow(
            BookHighlight("Oldest book read", statistics.OldestBook, "No known original publication years yet.", showYear: true),
            BookHighlight("Longest book read", statistics.LongestBook, "No page counts on completed editions yet."),
            BookHighlight("Shortest book read", statistics.ShortestBook, "No page counts on completed editions yet.")));
        body.Children.Add(CardRow(
            AuthorHighlights("Most-read authors", statistics.MostReadAuthors, "Top five by completed readings, including rereads.",
                "No completed readings with a named author yet.",
                author => $"{author.Readings.Count:N0} completed {(author.Readings.Count == 1 ? "reading" : "readings")}"),
            AuthorHighlights("Favorite authors", statistics.FavoriteAuthors, "At least two rated books and an average of 4.5 stars or higher.",
                "No authors meet both requirements yet.",
                author => $"{author.AverageRating:0.##} / 5 stars · {author.RatedBooks:N0} rated books")));
        body.Children.Add(new LibraryPurchases(store, books));
    }

    private static Grid CardRow(params Border[] cards)
    {
        var row = new Grid();
        for (var index = 0; index < cards.Length; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(cards[index], index);
            row.Children.Add(cards[index]);
        }
        return row;
    }

    private Border BookHighlight(string title, Completion? completion, string empty, bool showYear = false)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Heading(title, 20));
        if (completion == null)
        {
            panel.Children.Add(new TextBlock { Text = empty, TextWrapping = TextWrapping.Wrap });
            return Ui.Card(panel);
        }
        var detail = showYear ? PublicationScatterplot.YearLabel(completion.Book.OriginalYear!.Value)
            : $"{completion.Pages:N0} pages";
        var book = Ui.Button(completion.Book.Title, () =>
        {
            OpenReading(completion, owner);
            refresh();
        });
        var label = Ui.Text(completion.Book.Title, 16);
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(0);
        book.Content = label;
        book.HorizontalContentAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(book, $"Open {completion.Book.Title}");
        panel.Children.Add(book);
        var author = Ui.Text(completion.Book.Author, 13, "MutedBrush");
        author.TextWrapping = TextWrapping.Wrap;
        if (author.Text.Length > 0) panel.Children.Add(author);
        panel.Children.Add(Ui.Text(detail, 16));
        return Ui.Card(panel);
    }

    private static Border AuthorHighlights(string title, IReadOnlyList<AuthorReadingStatistics> authors, string description,
        string empty, Func<AuthorReadingStatistics, string> value)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Heading(title, 20));
        var descriptionText = Ui.Text(description, 13, "MutedBrush");
        descriptionText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(descriptionText);
        if (authors.Count == 0)
        {
            panel.Children.Add(Ui.Text(empty, 14, "MutedBrush"));
            return Ui.Card(panel);
        }
        var rows = new StackPanel();
        foreach (var author in authors)
        {
            var row = new DockPanel();
            var detail = Ui.Text(value(author), 14, "MutedBrush");
            detail.Margin = new Thickness(10, 0, 8, 8);
            DockPanel.SetDock(detail, Dock.Right);
            row.Children.Add(detail);
            var name = Ui.Text(author.Author, 15);
            name.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(name);
            rows.Children.Add(row);
        }
        if (authors.Count > 6)
        {
            var scroll = Ui.Scroll(rows);
            scroll.MaxHeight = 198;
            panel.Children.Add(scroll);
        }
        else panel.Children.Add(rows);
        return Ui.Card(panel);
    }

    private Border AnnualChart(IReadOnlyList<AnnualCompletions> years, bool showPages)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Heading(showPages ? "Pages per year" : "Books completed per year", 22));
        if (years.Count == 0)
        {
            body.Children.Add(Ui.Text("No dated completions yet.", 14, "MutedBrush"));
            return Ui.Card(body);
        }

        body.Children.Add(Ui.Text(showPages ? "Pages from the editions you read. Select a year to see which counts are missing."
            : "Rereads count separately. Select a year to see its reading records.", 13, "MutedBrush"));
        var rows = new StackPanel();
        body.Children.Add(years.Count > 6 ? new ScrollViewer { Content = rows, MaxHeight = 330,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : rows);
        var maximum = Math.Max(1, years.Max(Value));
        foreach (var year in years)
        {
            var value = Value(year);
            var detail = showPages
                ? (year.Pages.WithPageCount == 0 ? "No page counts" : $"{value:N0} pages") +
                    (year.Pages.WithoutPageCount > 0 ? $" · {year.Pages.WithoutPageCount:N0} missing" : "")
                : $"{value:N0} completed";
            var button = Ui.Button(year.Year.ToString(), () =>
            {
                if (ShowReadings($"Completions in {year.Year}", year.Readings, owner)) refresh();
            });
            button.Padding = new Thickness(10, 7, 10, 7);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(button, $"{year.Year}: {detail}");
            var labels = new DockPanel();
            var count = Ui.Text(detail, 13, "MutedBrush");
            DockPanel.SetDock(count, Dock.Right);
            labels.Children.Add(count);
            labels.Children.Add(Ui.Text(year.Year.ToString()));

            var bar = new Grid { Height = 5 };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(maximum - value, GridUnitType.Star) });
            bar.SetResourceReference(Panel.BackgroundProperty, "LineBrush");
            var fill = new Border { CornerRadius = new CornerRadius(2) };
            fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.Children.Add(fill);
            var content = new StackPanel();
            content.Children.Add(labels);
            content.Children.Add(bar);
            button.Content = content;
            rows.Children.Add(button);
        }
        return Ui.Card(body);

        long Value(AnnualCompletions year) => showPages ? year.Pages.KnownPages : year.Readings.Count;
    }

    private void ShowPublicationFullScreen(IReadOnlyList<PublicationPoint> points)
    {
        var window = new Window
        {
            Title = PublicationTitle,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Maximized,
            ShowInTaskbar = false,
            Style = (Style)Application.Current.FindResource(typeof(Window))
        };
        var root = new DockPanel { Margin = new Thickness(26) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var changed = false;
        var chart = new PublicationScatterplot(points, readings => changed |= OpenPoint(readings, window));
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Ui.Button("Reset zoom", chart.ResetZoom));
        var exit = Ui.Button("Exit full screen", window.Close);
        controls.Children.Add(exit);
        window.Loaded += (_, _) => exit.Focus();
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            window.Close();
        };
        DockPanel.SetDock(controls, Dock.Right);
        header.Children.Add(controls);
        var heading = new StackPanel();
        heading.Children.Add(Ui.Heading(PublicationTitle, 26));
        heading.Children.Add(Ui.Text("Scroll over the plot to zoom into publication years. Press Esc to exit.", 14, "MutedBrush"));
        header.Children.Add(heading);
        root.Children.Add(chart);
        window.Content = root;
        window.ShowDialog();
        if (changed) refresh();
    }

    private bool OpenPoint(IReadOnlyList<Completion> readings, Window parent)
    {
        if (readings.Count == 1)
        {
            OpenReading(readings[0], parent);
            return true;
        }
        return ShowReadings("Readings at this point", readings, parent, showPublicationYear: true);
    }

    private bool ShowReadings(string title, IReadOnlyList<Completion> readings, Window parent, bool showPublicationYear = false)
    {
        var window = new Window { Title = title, Owner = parent, Width = 790, Height = 610, MinWidth = 530,
            MinHeight = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Style = (Style)Application.Current.FindResource(typeof(Window)) };
        var panel = new DockPanel { Margin = new Thickness(22) };
        window.Content = panel;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        header.Children.Add(Ui.Heading(title));
        var coverage = PageCoverage.Calculate(readings);
        var total = coverage.WithPageCount == 0 ? "No page counts" : $"{coverage.KnownPages:N0} known pages";
        header.Children.Add(Ui.Text($"{readings.Count:N0} completed readings · {total} · {coverage.WithoutPageCount:N0} missing page counts", 13, "MutedBrush"));
        var close = Ui.Button("Close", window.Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        var entries = new StackPanel();
        panel.Children.Add(Ui.Scroll(entries));
        var opened = false;
        foreach (var reading in readings)
        {
            var name = reading.Book.Title;
            var finished = reading.Finished?.ToString("d") ?? (reading.Year is { } yearOnly ? $"{yearOnly} (year only)" : "No finish date");
            var details = $"{finished} · {reading.Edition.Label} · " +
                (reading.Pages is { } pages ? $"{pages:N0} pages" : "Page count missing");
            if (showPublicationYear && reading.Book.OriginalYear is { } year)
                details += $" · {PublicationScatterplot.YearLabel(year)} original publication";
            var button = Ui.Button(name, () => { OpenReading(reading, window); opened = true; });
            AutomationProperties.SetName(button, $"Open {name}, {details}");
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            var labels = new StackPanel();
            labels.Children.Add(Ui.Heading(name, 17));
            labels.Children.Add(Ui.Text(details, 13, "MutedBrush"));
            button.Content = labels;
            entries.Children.Add(button);
        }
        window.ShowDialog();
        return opened;
    }

    private void OpenReading(Completion completion, Window parent)
    {
        var book = store.Books().Find(item => item.Id == completion.Book.Id)
            ?? throw new InvalidOperationException("This book is no longer in the library.");
        new BookEditor(store, book, completion.Reading.Id) { Owner = parent }.ShowDialog();
    }
}
