using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class LibraryCleanupWindow : Window
{
    public LibraryCleanupWindow(LibraryStore store, DuplicateCandidate? selected = null)
    {
        Title = "Review duplicate books";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 960;
        Height = 680;
        MinWidth = 690;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        header.Children.Add(Ui.Heading("Review duplicate books"));
        var status = Ui.Text("", 14, "MutedBrush");
        header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var close = Ui.Button("Done", Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        root.Children.Add(columns);
        var pairs = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(DuplicateCandidate.Label)) };
        columns.Children.Add(pairs);
        var details = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        var scroll = Ui.Scroll(details);
        Grid.SetColumn(scroll, 1);
        columns.Children.Add(scroll);
        pairs.SelectionChanged += (_, _) =>
        {
            details.Children.Clear();
            if (pairs.SelectedItem is not DuplicateCandidate pair) return;
            Describe(pair.First, pair.Second);
            Describe(pair.Second, pair.First);
        };
        Populate();

        void Populate()
        {
            var candidates = selected != null ? new[] { selected } : BookIdentity.FindDuplicates(store.Books());
            pairs.ItemsSource = candidates;
            pairs.SelectedIndex = candidates.Count > 0 ? 0 : -1;
            status.Text = candidates.Count == 0 ? "No duplicate candidates found." : $"{candidates.Count} pairs to review";
        }

        void Describe(Book keep, Book other)
        {
            var summary = new StackPanel();
            var heading = Ui.Heading(keep.Title, 20);
            heading.TextWrapping = TextWrapping.Wrap;
            summary.Children.Add(heading);
            summary.Children.Add(Ui.Text(keep.Author));
            var cover = Ui.Cover(keep, store, 64, 92);
            cover.HorizontalAlignment = HorizontalAlignment.Left;
            summary.Children.Add(cover);
            summary.Children.Add(Ui.Text(string.Join(" · ", new[]
            {
                ReadingRules.Label(keep.Status), $"{keep.Readings.Count} readings", $"{keep.Editions.Count} editions",
                keep.Calibre != null ? "Calibre" : null, keep.Goodreads != null ? "Goodreads" : null,
                store.HasBibliographyFiles(keep) ? "Saved bibliography" : null
            }.OfType<string>()), 13, "MutedBrush"));
            foreach (var edition in keep.Editions)
                summary.Children.Add(Ui.Text($"{edition.Label}  {edition.Isbn} {string.Join(", ", edition.Holdings)}", 13));
            var merge = Ui.Button("Combine into this record", () =>
            {
                var question = $"Combine these entries into one book? Editions, reading records, and both source links will be kept. A backup will be created.\n\nKeep record: {keep.Title}\nCombine from: {other.Title}";
                if (keep.Readings.Any(reading => reading.Outcome == ReadingOutcome.Completed
                    && other.Readings.Any(item => item.Outcome == ReadingOutcome.Completed && item.Finished == reading.Finished)))
                    question += "\n\nCheck the completion histories for overlap. Each saved reading will be kept.";
                if (MessageBox.Show(this, question, "Confirm book merge", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                store.MergeBooks(keep, other);
                if (selected != null) Close();
                else Populate();
            });
            summary.Children.Add(merge);
            details.Children.Add(Ui.Card(summary));
        }
    }
}
