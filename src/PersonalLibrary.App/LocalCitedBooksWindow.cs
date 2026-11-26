using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class LocalCitedBooksWindow : Window
{
    private sealed record Row(LocalCitedBook Item)
    {
        public string Label => $"{Item.Book.Title} · {Item.Book.Author} · {Item.Evidence.Select(e => e.Source.Id).Distinct().Count()} source(s)";
    }

    public LocalCitedBooksWindow(LibraryStore store, Action<Book, EpubCitation?> openSource)
    {
        Title = "Cited books in My Books"; Width = 1100; Height = 760; MinWidth = 700; MinHeight = 450;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel(); header.Children.Add(Ui.Heading(Title));
        var note = Ui.Text("Possible local matches across all saved bibliographies, regardless of reading status, rating, or ownership. Review the original reference to confirm identity.", 13, "MutedBrush");
        note.TextWrapping = TextWrapping.Wrap; header.Children.Add(note);
        var search = Ui.Input(header, "Filter cited titles or authors");
        var status = Ui.Text("Loading saved bibliographies…", 13, "MutedBrush"); header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); root.Children.Add(grid);
        var list = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(Row.Label)) }; grid.Children.Add(list);
        var details = new StackPanel { Margin = new Thickness(18, 0, 0, 0) }; var scroll = Ui.Scroll(details);
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); Content = root;
        Row[] rows = [];
        void Filter()
        {
            var shown = rows.Where(row => row.Label.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            list.ItemsSource = shown; list.SelectedIndex = shown.Length > 0 ? 0 : -1;
            status.Text = $"{shown.Length} of {rows.Length} cited library titles";
        }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            details.Children.Clear();
            if (list.SelectedItem is not Row row) return;
            details.Children.Add(Ui.Heading(row.Item.Book.Title, 23));
            details.Children.Add(Ui.Button("Open book", () => new BookEditor(store, row.Item.Book) { Owner = this }.ShowDialog()));
            foreach (var evidence in row.Item.Evidence)
            {
                details.Children.Add(Ui.Button($"Cited by: {evidence.Source.Title}", () => openSource(evidence.Source, evidence.Citation)));
                var text = Ui.Text(evidence.Citation.Text, 14); text.TextWrapping = TextWrapping.Wrap; details.Children.Add(text);
            }
            scroll.ScrollToTop();
        };
        Loaded += async (_, _) =>
        {
            try
            {
                rows = await Task.Run(() =>
                {
                    var library = store.Books();
                    return BibliographyMatches.All(library, id =>
                        store.ReadCitationEvidence(id, library)).Select(item => new Row(item)).ToArray();
                });
                Filter();
            }
            catch (Exception ex) { status.Text = $"Could not load bibliography matches: {ex.Message}"; }
        };
    }
}
