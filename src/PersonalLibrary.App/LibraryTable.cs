using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal sealed record LibraryTableRow(Book Book)
{
    public string Title => Book.Title;
    public string Author => Book.Author;
    public string ToReadPosition => Book.Status == ReadingStatus.ToRead ? Book.ToReadPosition?.ToString() ?? "" : "";
    public string DateAdded => Book.DateAdded?.ToString("yyyy-MM-dd") ?? "";
    public string Status => ReadingRules.Label(Book.Status);
    public string Shelves => string.Join(", ", Book.Shelves);
    public string Groups => string.Join(", ", Book.ToReadGroups);
    public string Rating => Book.Rating is { } stars ? $"{stars}/5" : "";
    public string Pages => Book.SelectedEdition.PageCount?.ToString() ?? "";
}

internal static class LibraryTable
{
    public static DataGrid Create(IReadOnlyList<Book> books, bool bulk, HashSet<Guid> selected,
        Action selectionChanged, Action<Book> open, Action<LibrarySort> sort, LibrarySort sortMode, bool descending)
    {
        var table = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 32,
            SelectionMode = bulk ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(table, "Library table");
        table.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        table.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        table.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
        table.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
        table.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "LineBrush");
        VirtualizingPanel.SetVirtualizationMode(table, VirtualizationMode.Recycling);
        foreach (var (label, property, width) in new[] { ("Position", "ToReadPosition", .55), ("Date added", "DateAdded", .9), ("Title", "Title", 2.5), ("Author", "Author", 1.5),
            ("Status", "Status", 1.0), ("Shelves", "Shelves", 1.5), ("To-read groups", "Groups", 1.5), ("Stars", "Rating", .5), ("Pages", "Pages", .5) })
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(property)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3)));
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property),
                Width = new DataGridLength(width, DataGridLengthUnitType.Star), MinWidth = property == "Title" ? 150 : 60,
                CanUserSort = property is "Title" or "Author" or "ToReadPosition" or "DateAdded", SortMemberPath = property, ElementStyle = style,
                SortDirection = property == sortMode.ToString()
                    ? descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending : null });
        }
        table.ItemsSource = books.Select(book => new LibraryTableRow(book)).ToArray();
        if (bulk)
            foreach (var row in table.Items.OfType<LibraryTableRow>().Where(row => selected.Contains(row.Book.Id))) table.SelectedItems.Add(row);
        table.SelectionChanged += (_, _) =>
        {
            if (!bulk) return;
            selected.Clear(); selected.UnionWith(table.SelectedItems.OfType<LibraryTableRow>().Select(row => row.Book.Id)); selectionChanged();
        };
        table.Sorting += (_, args) => { args.Handled = true; if (Enum.TryParse<LibrarySort>(args.Column.SortMemberPath, out var value)) sort(value); };
        table.MouseDoubleClick += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(table, source) is DataGridRow { Item: LibraryTableRow row })
                open(row.Book);
        };
        table.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && table.SelectedItem is LibraryTableRow row) { args.Handled = true; open(row.Book); }
        };
        return table;
    }
}
