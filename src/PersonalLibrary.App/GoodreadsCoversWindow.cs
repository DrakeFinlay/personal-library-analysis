using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class GoodreadsCoversWindow : Window
{
    public GoodreadsCoversWindow(LibraryStore store)
    {
        Title = "Find Goodreads covers";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 740;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(22) };
        Content = panel;
        var summary = Ui.Heading("Goodreads books missing a cover", 24);
        DockPanel.SetDock(summary, Dock.Top);
        panel.Children.Add(summary);
        var buttons = new WrapPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var results = new ListBox();
        panel.Children.Add(results);
        var find = Ui.Button("Find cover for selected book", () =>
        {
            if (results.SelectedItem is not ListBoxItem { Tag: Book book }) return;
            var edition = book.Editions.Single(item => item.Id == book.Goodreads!.EditionId);
            var lookup = new MetadataLookupWindow(book.Title, book.Author, OpenLibraryLookup.NormalizeIsbn(edition.Isbn) ?? "",
                editBook: true, findCover: true) { Owner = this };
            if (lookup.ShowDialog() != true || lookup.SelectedCover is not { } bytes) return;
            edition.CoverFile = store.ImportJpegCover(bytes);
            store.Save(book);
            Populate();
        }, true);
        find.IsEnabled = false;
        buttons.Children.Add(find);
        buttons.Children.Add(Ui.Button("Done", Close));
        results.SelectionChanged += (_, _) => find.IsEnabled = results.SelectedItem != null;
        Populate();

        void Populate()
        {
            var previous = results.SelectedIndex;
            results.Items.Clear();
            foreach (var book in store.Books().Where(book => !book.Excluded && book.Goodreads != null
                && book.Editions.All(edition => store.CoverPath(edition) == null)).OrderBy(book => book.Title))
                results.Items.Add(new ListBoxItem { Tag = book, Content = $"{book.Title} — {book.Author}" });
            summary.Text = $"{results.Items.Count} Goodreads books missing a cover";
            results.SelectedIndex = results.Items.Count > 0 ? Math.Clamp(previous, 0, results.Items.Count - 1) : -1;
        }
    }
}
