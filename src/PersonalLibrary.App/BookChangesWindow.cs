using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class BookChangesWindow : Window
{
    private sealed record Row(BookChange Change)
    {
        public string Label => $"{Change.At.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {Change.Kind}" +
            (Change.Kind == "Book changed" ? " · " + string.Join(", ", LibraryStore.ChangeFields(Change)
                .Select(item => FieldLabel(item.Field.Split('.', '[')[0])).Distinct().Take(3)) : "");
        public override string ToString() => Label;
    }

    private static string FieldLabel(string value) => System.Text.RegularExpressions.Regex.Replace(
        value.Replace("ToRead", "To-read").Replace(".", " · "), "(?<=[a-z])(?=[A-Z])", " ");

    public BookChangesWindow(LibraryStore store, Book book)
    {
        Title = $"{book.Title} — Change history"; Width = 1060; Height = 740; MinWidth = 700; MinHeight = 440;
        Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var header = new StackPanel(); header.Children.Add(Ui.Heading("Change history"));
        header.Children.Add(Ui.Text(book.Title, 17));
        var note = Ui.Text("Recorded changes include saved edits, status changes, readings and rereads, imports, and bibliographies. Tracking started is a baseline; it does not reconstruct earlier edits.", 13, "MutedBrush");
        note.TextWrapping = TextWrapping.Wrap; header.Children.Add(note);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(320) }); columns.ColumnDefinitions.Add(new());
        root.Children.Add(columns);
        var rows = new List<Row>(); var list = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(Row.Label)) };
        var left = new DockPanel(); var older = Ui.Button("Load older changes", () => { });
        DockPanel.SetDock(older, Dock.Bottom); left.Children.Add(older); left.Children.Add(list); columns.Children.Add(left);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(details, "Change details");
        Grid.SetColumn(details, 1); columns.Children.Add(details);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Row row) { details.Text = "No recorded changes yet."; return; }
            var change = row.Change;
            var fields = LibraryStore.ChangeFields(change);
            var output = new System.Text.StringBuilder(row.Label + "\n\n");
            foreach (var field in fields) output.AppendLine(FieldLabel(field.Field)).AppendLine("Before: " + field.Before).AppendLine("After: " + field.After).AppendLine();
            if (change.Details is { } text)
            {
                var attachment = JsonSerializer.Deserialize<AttachmentChange>(text, LibraryStore.Json);
                if (attachment != null) output.AppendLine(attachment.File).AppendLine("Before:\n" + (attachment.Before ?? "(not recorded)"))
                    .AppendLine("\nAfter:\n" + attachment.After);
                if (attachment?.Images is { Length: > 0 } images) output.AppendLine("\nImages added:\n" + string.Join("\n", images));
            }
            details.Text = output.ToString(); details.ScrollToHome();
        };
        void Load()
        {
            var batch = store.Changes(book.Id, before: rows.LastOrDefault()?.Change.Sequence);
            var selected = list.SelectedItem;
            rows.AddRange(batch.Select(change => new Row(change))); list.ItemsSource = rows.ToArray();
            list.SelectedItem = selected ?? rows.FirstOrDefault(); older.IsEnabled = batch.Count == 100;
        }
        older.Click += (_, _) => Ui.Guard(Load); Load();
    }
}
