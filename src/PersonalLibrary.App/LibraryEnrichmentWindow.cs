using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class LibraryEnrichmentWindow : Window
{
    public LibraryEnrichmentWindow(LibraryStore store)
    {
        Title = "Find covers and MSRP";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 920;
        Height = 680;
        MinWidth = 680;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading("Find covers and MSRP", 24));
        header.Children.Add(Ui.Text("Search for missing published covers and print list prices. A backup is created before saving.", 14, "MutedBrush"));
        var estimates = new CheckBox { Content = "Allow covers and estimated MSRP from other editions", IsChecked = true };
        header.Children.Add(estimates);
        var status = Ui.Text("Ready to search the library.", 13, "MutedBrush");
        status.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(status);
        var buttons = new WrapPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var rows = new ListBox();
        root.Children.Add(rows);
        var changes = new List<EnrichmentChange>();
        CancellationTokenSource? operation = null;
        var scan = new Button { Content = "Search library" };
        var cancel = new Button { Content = "Stop search", IsEnabled = false };
        var apply = new Button { Content = "Back up and save results", IsEnabled = false };
        var manual = Ui.Button("Find an individual Goodreads cover…", () =>
            new GoodreadsCoversWindow(store) { Owner = this }.ShowDialog());
        var close = Ui.Button("Close", Close);
        buttons.Children.Add(scan);
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        buttons.Children.Add(manual);
        buttons.Children.Add(close);
        cancel.Click += (_, _) => operation?.Cancel();
        Closing += (_, args) =>
        {
            if (operation == null) return;
            operation.Cancel();
            args.Cancel = true;
            status.Text = "Stopping the search. Completed results can be saved.";
        };
        scan.Click += async (_, _) =>
        {
            var books = store.Books().Where(book => !book.Excluded).ToArray();
            var allowEstimates = estimates.IsChecked == true;
            changes.Clear();
            rows.Items.Clear();
            scan.IsEnabled = apply.IsEnabled = estimates.IsEnabled = manual.IsEnabled = false;
            cancel.IsEnabled = true;
            operation = new CancellationTokenSource();
            var token = operation.Token;
            var lookup = new LibraryEnrichment();
            var completed = 0;
            var unavailable = 0;
            try
            {
                foreach (var book in books)
                {
                    token.ThrowIfCancellationRequested();
                    status.Text = $"Searching {++completed} of {books.Length} · {book.Title}";
                    try
                    {
                        var missingCover = book.Editions.All(edition => store.CoverPath(edition) == null);
                        var found = await lookup.Find(book, missingCover, allowEstimates, token);
                        foreach (var change in found)
                        {
                            var valid = change;
                            if (change.Cover is { } cover && !ValidCover(cover.Bytes)) valid = change with { Cover = null };
                            if (valid.Cover == null && valid.Msrp == null) continue;
                            changes.Add(valid);
                            var detail = valid.Msrp is { } price ? $" · {(price.Estimated ? "Estimated " : "")}{price.Currency} {price.Amount:N2}" : "";
                            rows.Items.Add($"{book.Title} · {(valid.Cover != null ? "Cover found" : "Price found")}{detail}");
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException && !token.IsCancellationRequested)
                    {
                        unavailable++;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                operation.Dispose();
                operation = null;
                scan.IsEnabled = estimates.IsEnabled = manual.IsEnabled = true;
                cancel.IsEnabled = false;
                apply.IsEnabled = changes.Count > 0;
                status.Text = $"{changes.Count(change => change.Cover != null)} covers · {changes.Count(change => change.Msrp != null)} prices · " +
                    $"{completed} books checked · {unavailable} lookups unavailable";
            }
        };
        apply.Click += (_, _) =>
        {
            try
            {
                var count = LibraryEnrichment.Apply(store, changes);
                status.Text = $"Saved results for {count} books. Refresh library to see them.";
                apply.IsEnabled = false;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save results"); }
        };
    }

    private static bool ValidCover(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return image.PixelWidth >= 65 && image.PixelHeight >= 100;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { return false; }
    }
}
