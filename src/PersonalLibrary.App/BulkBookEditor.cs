using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class BulkBookEditor : Window
{
    public BulkBookEditor(LibraryStore store, IReadOnlyCollection<Guid> ids)
    {
        var books = store.Books().Where(book => ids.Contains(book.Id)).ToList();
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = $"Configure {books.Count} selected books";
        Width = 700; Height = 780; MinWidth = 600; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var panel = new StackPanel(); root.Children.Add(Ui.Scroll(panel)); Content = root;
        panel.Children.Add(Ui.Heading($"Configure {books.Count} books", 25));
        panel.Children.Add(Ui.Text("Only checked fields are applied. Edition fields affect each book’s selected edition. Blank checked fields clear that value. Reading history and other editions are preserved.", 13, "MutedBrush"));
        (CheckBox Apply, TextBox Input) Field(string label, IEnumerable<string?> values)
        {
            var check = new CheckBox { Content = "Apply " + label, Margin = new Thickness(0, 12, 0, 4) };
            panel.Children.Add(check);
            var distinct = values.Distinct().ToArray();
            var input = new TextBox { Text = distinct.Length == 1 ? distinct[0] ?? "" : "", IsEnabled = false };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            check.Click += (_, _) => input.IsEnabled = check.IsChecked == true;
            panel.Children.Add(input); return (check, input);
        }
        var language = Field("Language", books.Select(b => b.SelectedEdition.Language));
        // Additional shelves are additive, so existing memberships are never replaced.
        var shelves = Field("Add shelves (comma separated)", new[] { "" });
        ShelfSuggestions.Attach(shelves.Input, store.Books().SelectMany(b => b.Shelves));
        var publisher = Field("Publisher", books.Select(b => b.SelectedEdition.Publisher));
        var author = Field("Author / contributors", books.Select(b => b.Author));
        var original = Field("Original publication year", books.Select(b => b.OriginalYear?.ToString()));
        var edition = Field("Edition publication year", books.Select(b => b.SelectedEdition.PublicationYear?.ToString()));
        var pages = Field("Page count", books.Select(b => b.SelectedEdition.PageCount?.ToString()));
        var rating = Field("Rating (1–5)", books.Select(b => b.Rating?.ToString()));
        var statuses = Enum.GetValues<ReadingStatus>();
        panel.Children.Add(Ui.Text("Reading status / category", 12, "MutedBrush"));
        var status = new ComboBox { ItemsSource = new[] { "Keep status" }.Concat(statuses.Select(ReadingRules.Label)), SelectedIndex = 0 };
        panel.Children.Add(status);
        panel.Children.Add(Ui.Text(string.Join(" · ", books.Take(8).Select(b => b.Title)) + (books.Count > 8 ? " …" : ""), 12, "MutedBrush"));
        actions.Children.Add(Ui.Button("Save to selected books", () =>
        {
            bool On((CheckBox Apply, TextBox Input) field) => field.Apply.IsChecked == true;
            string? Text((CheckBox Apply, TextBox Input) field) => On(field) ? field.Input.Text : null;
            int? Number((CheckBox Apply, TextBox Input) field)
            {
                if (!On(field) || string.IsNullOrWhiteSpace(field.Input.Text)) return null;
                if (!int.TryParse(field.Input.Text, out var value)) throw new InvalidOperationException($"Enter a whole number for {field.Apply.Content}.");
                return value;
            }
            if (!(new[] { language, publisher, author, original, edition, pages, rating, shelves }).Any(On) && status.SelectedIndex == 0)
                throw new InvalidOperationException("Check at least one field to apply.");
            var changes = new BulkBookChanges(Text(language), Text(publisher), Text(author), Number(original), On(original),
                Number(edition), On(edition), Number(pages), On(pages), status.SelectedIndex > 0 ? statuses[status.SelectedIndex - 1] : null,
                Number(rating), On(rating), Text(shelves));
            var pending = books.Select(LibraryStore.Clone).ToList();
            foreach (var book in pending) changes.Apply(book);
            store.SaveBatch(pending); DialogResult = true;
        }, true));
        actions.Children.Add(Ui.Button("Cancel", Close));
    }
}
