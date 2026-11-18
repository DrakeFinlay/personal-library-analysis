using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class PublicBibliographyWindow : Window
{
    public PublicBibliographyWindow(LibraryStore store, Book book)
    {
        Title = $"Find Bibliography Online — {book.Title}";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 1060;
        Height = 760;
        MinWidth = 740;
        MinHeight = 550;

        var provider = new PublicBibliographyLookup();
        var lifetime = new CancellationTokenSource();
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading("Find Bibliography Online"));
        header.Children.Add(Ui.Text($"Attach to {book.Title} · {book.Author}", 14, "MutedBrush"));
        header.Children.Add(Ui.Text("Search public editions on Internet Archive. Review the edition and bibliography before saving.", 14, "MutedBrush"));
        var fields = new UniformGrid { Columns = 3 };
        header.Children.Add(fields);
        var title = AddField("Title", book.Title.Split(':', 2)[0]);
        var author = AddField("Author", book.Author);
        var isbn = AddField("ISBN (optional)", "");
        isbn.ToolTip = "Enter an ISBN to search for that edition. Clear it to search by title and author.";
        var editions = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(PublicBookEdition.Label)), Margin = new Thickness(0, 0, 12, 0) };

        var actions = new WrapPanel();
        header.Children.Add(actions);
        var search = new Button { Content = "Search Internet Archive", IsDefault = true };
        var preview = new Button { Content = "Preview bibliography", IsEnabled = false };
        var openSource = Ui.Button("Open edition online", () =>
        {
            if (editions.SelectedItem is PublicBookEdition selected) OpenArchivePage(selected.Page);
        });
        openSource.IsEnabled = false;
        actions.Children.Add(search);
        actions.Children.Add(preview);
        actions.Children.Add(openSource);
        var status = Ui.Text("", 13, "MutedBrush");
        status.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(status);

        var footer = new WrapPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        EpubBibliography? candidate = null;
        var save = Ui.Button("Save online bibliography", () =>
        {
            if (candidate == null) return;
            if (store.HasOnlineBibliography(book.Id) && MessageBox.Show(this,
                "Replace the existing online bibliography for this book?", "Bibliography",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            store.SaveOnlineBibliography(book.Id, candidate);
            DialogResult = true;
        }, true);
        save.IsEnabled = false;
        footer.Children.Add(save);
        footer.Children.Add(Ui.Button("Close", Close));

        var columns = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        root.Children.Add(columns);
        AutomationProperties.SetName(editions, "Internet Archive editions");
        columns.Children.Add(editions);
        var text = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(text, "Online bibliography preview");
        Grid.SetColumn(text, 1);
        columns.Children.Add(text);
        editions.SelectionChanged += (_, _) =>
        {
            preview.IsEnabled = openSource.IsEnabled = editions.SelectedItem != null;
            ClearPreview();
        };
        search.Click += async (_, _) => await Run(async token =>
        {
            editions.ItemsSource = null;
            ClearPreview();
            status.Text = "Searching Internet Archive…";
            var found = await provider.SearchAsync(title.Text, author.Text, isbn.Text, token);
            token.ThrowIfCancellationRequested();
            editions.ItemsSource = found;
            status.Text = found.Count == 0 ? "No public editions found. Try the title and author without an ISBN."
                : $"Found {found.Count} editions. Select one to check for a bibliography.";
        });
        preview.Click += async (_, _) =>
        {
            if (editions.SelectedItem is not PublicBookEdition edition) return;
            await Run(async token =>
            {
                ClearPreview();
                status.Text = "Checking the selected edition for bibliography text…";
                var found = await provider.PreviewAsync(edition, token);
                token.ThrowIfCancellationRequested();
                candidate = found;
                text.Text = found.ToMarkdown();
                status.Text = $"{found.Entries.Length} entries · {edition.Label}";
            });
        };
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };

        TextBox AddField(string label, string value)
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            fields.Children.Add(field);
            var input = Ui.Input(field, label, value);
            input.MaxLength = 300;
            return input;
        }

        void ClearPreview()
        {
            candidate = null;
            text.Clear();
            save.IsEnabled = false;
        }

        async Task Run(Func<CancellationToken, Task> action)
        {
            search.IsEnabled = preview.IsEnabled = editions.IsEnabled = save.IsEnabled = false;
            try { await action(lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (OperationCanceledException) { status.Text = "Internet Archive took too long to respond. Try again."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally
            {
                if (!lifetime.IsCancellationRequested)
                {
                    search.IsEnabled = editions.IsEnabled = true;
                    preview.IsEnabled = editions.SelectedItem != null;
                    save.IsEnabled = candidate != null;
                }
            }
        }
    }

    internal static void OpenArchivePage(Uri page)
    {
        if (page.Scheme == Uri.UriSchemeHttps && page.Host == "archive.org")
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }
}
