using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class GoodreadsImportWindow : Window
{
    public GoodreadsImportWindow(GoodreadsImportPreview preview)
    {
        Title = "Preview Goodreads import";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 940; Height = 680; MinWidth = 670; MinHeight = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading("Preview Goodreads import"));
        var summary = Ui.Text("", 14, "MutedBrush");
        summary.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(summary);
        var explanation = Ui.Text("Goodreads takes priority for reading status, rating, review, and its own shelves. Calibre links, ownership, and local notes are kept. Pre-Goodreads completions are mapped to undated history.", 13);
        explanation.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(explanation);
        var filter = new ComboBox
        {
            Width = 195, HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { "Changes and skipped", "Needs review", "All rows" }, SelectedIndex = 0
        };
        System.Windows.Automation.AutomationProperties.SetName(filter, "Preview rows");
        header.Children.Add(filter);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var apply = Ui.Button("Back up and import", () =>
        {
            if (preview.Skipped > 0 && MessageBox.Show(this,
                $"{preview.Skipped} rows will be skipped. Import the reviewed additions and updates?",
                "Skipped Goodreads rows", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            DialogResult = true;
        }, true);
        buttons.Children.Add(apply);
        buttons.Children.Add(Ui.Button("Cancel", Close));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        root.Children.Add(grid);
        var list = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(GoodreadsImportRow.Display)) };
        grid.Children.Add(list);
        var details = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        var scroll = Ui.Scroll(details);
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);

        void Populate()
        {
            var selected = (list.SelectedItem as GoodreadsImportRow)?.Number;
            var rows = preview.Rows.Where(row => filter.SelectedIndex switch
            {
                0 => row.Action != "Unchanged", 1 => row.Action == "Review", _ => true
            }).ToArray();
            list.ItemsSource = rows;
            list.SelectedItem = rows.FirstOrDefault(row => row.Number == selected) ?? rows.FirstOrDefault();
            summary.Text = $"{preview.FileName} · {preview.Added} new · {preview.Updated} updated · {preview.Unchanged} unchanged · {preview.Skipped} skipped" +
                $"\n{preview.Excluded} manga/comics skipped · {preview.LegacyUndated} pre-Goodreads completions";
            apply.IsEnabled = preview.Added + preview.Updated > 0;
        }

        list.SelectionChanged += (_, _) =>
        {
            details.Children.Clear();
            if (list.SelectedItem is not GoodreadsImportRow row) return;
            var heading = Ui.Heading(row.Label, 20);
            heading.TextWrapping = TextWrapping.Wrap;
            details.Children.Add(heading);
            details.Children.Add(Ui.Text($"CSV line {row.Number} · Goodreads ID {row.SourceId}", 12, "MutedBrush"));
            var description = Ui.Text(row.Details);
            description.TextWrapping = TextWrapping.Wrap;
            details.Children.Add(description);
            if (row.Action == "Review")
            {
                foreach (var candidate in row.Candidates)
                {
                    var label = Ui.Text(candidate.Label);
                    label.TextWrapping = TextWrapping.Wrap;
                    var button = Ui.Button("Match to this book", () => { preview.Resolve(row.Number, candidate.Id); Populate(); });
                    details.Children.Add(label);
                    details.Children.Add(button);
                }
                details.Children.Add(Ui.Button("Add as a separate book", () => { preview.Resolve(row.Number, Guid.Empty); Populate(); }));
            }
            if (preview.HasDecision(row.Number))
                details.Children.Add(Ui.Button("Undo match decision", () => { preview.ClearDecision(row.Number); Populate(); }));
            scroll.ScrollToTop();
        };
        filter.SelectionChanged += (_, _) => Populate();
        Populate();
    }
}
