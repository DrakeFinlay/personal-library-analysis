using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class DuplicatePickerWindow : Window
{
    public DuplicateCandidate? Pair { get; private set; }

    public DuplicatePickerWindow(IReadOnlyList<Book> books, string startingQuery)
    {
        Title = "Choose two books to merge";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 960;
        Height = 640;
        MinWidth = 600;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var heading = Ui.Heading(Title);
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(columns);
        var ordered = books.OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var left = AddPicker(0, "First book");
        var right = AddPicker(1, "Second book");
        var compare = Ui.Button("Compare selected books", () =>
        {
            if (left.SelectedItem is ListBoxItem { Tag: Book first } && right.SelectedItem is ListBoxItem { Tag: Book second } && first.Id != second.Id)
            {
                Pair = new(first, second, "Manually selected pair");
                DialogResult = true;
            }
        }, true);
        compare.IsEnabled = false;
        buttons.Children.Add(compare);
        buttons.Children.Add(Ui.Button("Cancel", Close));
        left.SelectionChanged += (_, _) => UpdateSelection();
        right.SelectionChanged += (_, _) => UpdateSelection();

        void UpdateSelection() => compare.IsEnabled = left.SelectedItem is ListBoxItem { Tag: Book first } &&
            right.SelectedItem is ListBoxItem { Tag: Book second } && first.Id != second.Id;

        ListBox AddPicker(int column, string label)
        {
            var side = new DockPanel { Margin = column == 0 ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(side, column);
            columns.Children.Add(side);
            var header = new StackPanel();
            DockPanel.SetDock(header, Dock.Top);
            side.Children.Add(header);
            var search = Ui.Input(header, $"{label} · search title, author, or ISBN", startingQuery);
            var list = new ListBox();
            AutomationProperties.SetName(list, label);
            side.Children.Add(list);
            void Populate()
            {
                var query = search.Text.Trim();
                list.Items.Clear();
                foreach (var book in ordered.Where(book => !book.Excluded &&
                    $"{book.Title} {book.Author} {string.Join(' ', book.Editions.Select(edition => edition.Isbn))}"
                    .Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    var row = new StackPanel();
                    row.Children.Add(Ui.Heading(book.Title, 16));
                    row.Children.Add(Ui.Text($"{book.Author} · {ReadingRules.Label(book.Status)}", 13, "MutedBrush"));
                    list.Items.Add(new ListBoxItem { Content = row, Tag = book });
                }
            }

            search.TextChanged += (_, _) => Populate();
            Populate();
            return list;
        }
    }
}
