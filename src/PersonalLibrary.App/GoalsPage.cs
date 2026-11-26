using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class GoalsPage : DockPanel
{
    private readonly LibraryStore store;
    private readonly Action<Book> edit;
    private readonly StackPanel body = new();
    private int year;

    public GoalsPage(LibraryStore store, List<Book> books, Action<Book> edit, int year, Action<int> yearChanged)
    {
        this.store = store; this.edit = edit; this.year = year;
        var header = new StackPanel();
        header.Children.Add(Ui.Heading("Goals"));
        var selector = new WrapPanel();
        var input = Ui.Input(selector, "Year", year.ToString());
        input.Width = 90;
        selector.Children.Add(Ui.Button("Show year", () =>
        {
            if (!int.TryParse(input.Text, out var selected) || selected is < 1 or > 9999)
                throw new InvalidOperationException("Enter a year from 1 to 9999.");
            this.year = selected; yearChanged(selected); Render();
        }));
        header.Children.Add(selector);
        SetDock(header, Dock.Top); Children.Add(header);
        Children.Add(Ui.Scroll(body));
        Render(books);
    }

    private void Render(List<Book>? saved = null)
    {
        body.Children.Clear();
        var books = saved ?? store.Books();
        var planned = books.Where(book => !book.Excluded && book.PlannedReadingYears.Contains(year))
            .OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var list = new StackPanel();
        list.Children.Add(Ui.Heading($"Reads for {year}", 24));
        var explanation = Ui.Text("Choose specific titles from My Books for this year. A completed reading dated in this year marks a title complete, including rereads. Undated readings do not count.", 14, "MutedBrush");
        explanation.TextWrapping = TextWrapping.Wrap;
        list.Children.Add(explanation);
        list.Children.Add(Ui.Text($"{planned.Count(book => ReadingGoals.Completed(book, year))} of {planned.Count} titles completed"));
        var search = Ui.Input(list, "Find a title or author in My Books");
        var results = new StackPanel();
        list.Children.Add(results);
        search.TextChanged += (_, _) =>
        {
            results.Children.Clear();
            var query = search.Text.Trim();
            if (query.Length == 0) return;
            var matches = books.Where(book => !book.Excluded && !book.PlannedReadingYears.Contains(year) &&
                (book.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 book.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (matches.Count == 0) results.Children.Add(Ui.Text("No matching titles to add. Add missing books with + Add a book first.", 13, "MutedBrush"));
            foreach (var book in matches.Take(12))
            {
                var add = Ui.Button($"Add: {book.Title} · {book.Author}", () => { store.PlanReading(book.Id, year, true); Render(); });
                add.HorizontalContentAlignment = HorizontalAlignment.Left;
                add.Content = new TextBlock { Text = $"Add: {book.Title} · {book.Author}", TextWrapping = TextWrapping.Wrap };
                results.Children.Add(add);
            }
            if (matches.Count > 12) results.Children.Add(Ui.Text("Showing 12 matches. Type more to narrow the search.", 13, "MutedBrush"));
        };
        if (planned.Count == 0) list.Children.Add(Ui.Text("No titles planned yet. Search above to add your first read.", 14, "MutedBrush"));
        foreach (var book in planned)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var remove = Ui.Button("Remove from year", () => { store.PlanReading(book.Id, year, false); Render(); });
            AutomationProperties.SetName(remove, $"Remove {book.Title} from {year}");
            SetDock(remove, Dock.Right); row.Children.Add(remove);
            var cover = Ui.Cover(book, store, 40, 60);
            cover.Margin = new Thickness(0, 0, 12, 0);
            SetDock(cover, Dock.Left); row.Children.Add(cover);
            var open = Ui.Button(book.Title, () => edit(book));
            open.HorizontalContentAlignment = HorizontalAlignment.Left;
            var label = new StackPanel();
            var title = Ui.Text(book.Title, 17); title.TextWrapping = TextWrapping.Wrap; label.Children.Add(title);
            var author = Ui.Text(book.Author, 13, "MutedBrush"); author.TextWrapping = TextWrapping.Wrap; label.Children.Add(author);
            label.Children.Add(Ui.Text(ReadingGoals.Completed(book, year) ? $"Completed in {year}" : ReadingRules.Label(book.Status), 13, "MutedBrush"));
            open.Content = label;
            AutomationProperties.SetName(open, $"Open {book.Title}");
            row.Children.Add(open); list.Children.Add(row);
        }
        body.Children.Add(Ui.Card(list));

        var challenge = new StackPanel();
        challenge.Children.Add(Ui.Heading($"{year} reading challenge", 23));
        var goal = int.TryParse(store.Setting("goal-" + year, "24"), out var value) && value is > 0 and <= 100000 ? value : 24;
        var count = ReadingRules.CompletedInYear(books, year);
        challenge.Children.Add(Ui.Text($"{count} / {goal} books", 22));
        challenge.Children.Add(new ProgressBar { Minimum = 0, Maximum = goal, Value = count, Height = 8, Margin = new Thickness(0, 0, 0, 12) });
        var note = Ui.Text("This optional book count is separate from your title list. All completed readings in the year count, including rereads; undated history stays outside the calendar.", 13, "MutedBrush");
        note.TextWrapping = TextWrapping.Wrap; challenge.Children.Add(note);
        var target = Ui.Input(challenge, "Annual book goal", goal.ToString()); target.Width = 180; target.HorizontalAlignment = HorizontalAlignment.Left;
        challenge.Children.Add(Ui.Button("Save goal", () =>
        {
            if (!int.TryParse(target.Text, out var number) || number is < 1 or > 100000)
                throw new InvalidOperationException("Enter a goal from 1 to 100,000.");
            store.SetSetting("goal-" + year, number.ToString()); Render();
        }));
        body.Children.Add(Ui.Card(challenge));
    }
}
