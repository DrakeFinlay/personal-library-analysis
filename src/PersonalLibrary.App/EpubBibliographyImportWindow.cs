using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class EpubBibliographyImportWindow : Window
{
    public EpubBibliographyImportWindow(LibraryStore store, Book book, string? linkedPath = null)
    {
        Title = $"Import bibliography from EPUB — {book.Title}";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 980; Height = 740; MinWidth = 640; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var header = new StackPanel(); header.Children.Add(Ui.Heading("Import bibliography from EPUB", 25));
        var title = Ui.Text($"Source book: {book.Title} · {book.Author}"); title.TextWrapping = TextWrapping.Wrap; header.Children.Add(title);
        var note = Ui.Text("Choose an EPUB, analyze its bibliography, and review the extracted text before saving. Existing pasted/scanned text and online bibliographies are preserved.", 13, "MutedBrush");
        note.TextWrapping = TextWrapping.Wrap; header.Children.Add(note);
        var path = Ui.Input(header, "EPUB file", linkedPath ?? "");
        var actions = new WrapPanel(); header.Children.Add(actions);
        var choose = Ui.Button("Choose EPUB…", () =>
        {
            var picker = new OpenFileDialog { Title = "Choose an EPUB to analyze", Filter = "EPUB books (*.epub)|*.epub", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) path.Text = picker.FileName;
        });
        var analyze = Ui.Button("Analyze bibliography", () => { }, true);
        actions.Children.Add(choose); actions.Children.Add(analyze);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = Ui.Text("No file analyzed yet.", 13, "MutedBrush"); status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var review = Ui.Button("Review citations / local matches", () => { });
        var save = Ui.Button("Back up and save bibliography", () => { }, true);
        var close = Ui.Button("Close", Close);
        buttons.Children.Add(review); buttons.Children.Add(save); buttons.Children.Add(close);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(text, "Extracted EPUB bibliography preview");
        root.Children.Add(text);
        EpubBibliographyImportPreview? candidate = null;
        var busy = false;
        review.IsEnabled = save.IsEnabled = false;
        path.TextChanged += (_, _) => { candidate = null; text.Clear(); review.IsEnabled = save.IsEnabled = false; status.Text = "Analyze this file to preview its bibliography."; };
        review.Click += (_, _) => Ui.Guard(() =>
        {
            if (candidate != null) new EpubBibliographyWindow(store, book, candidate.Bibliography, showAlternate: false, draftPreview: true) { Owner = this }.ShowDialog();
        });
        analyze.Click += async (_, _) =>
        {
            candidate = null;
            var filename = path.Text.Trim();
            await Run(() => store.PreviewEpubBibliography(book.Id, filename), result =>
            {
                candidate = result; text.Text = result.Bibliography.ToMarkdown();
                status.Text = $"Entries: {result.Bibliography.Entries.Length} · Sections: {result.Bibliography.Entries.Select(entry => entry.Section).Distinct().Count()} · Unreadable documents: {result.Bibliography.UnreadableDocuments}" +
                    (result.ExpectedBibliographyHash == null ? " · Ready to save a new EPUB bibliography." : " · Saving replaces this book's current EPUB bibliography; its earlier version remains in Change history and the backup.");
            });
        };
        save.Click += async (_, _) =>
        {
            if (candidate == null) return;
            var selected = candidate;
            await Run(() => store.ApplyEpubBibliography(selected), _ => DialogResult = true);
        };
        Closing += (_, args) => { if (busy) args.Cancel = true; };

        async Task Run<T>(Func<T> work, Action<T> completed)
        {
            busy = true; path.IsEnabled = choose.IsEnabled = analyze.IsEnabled = review.IsEnabled = save.IsEnabled = close.IsEnabled = false;
            status.Text = "Working locally…";
            try { var result = await Task.Run(work); busy = false; completed(result); }
            catch (Exception ex) { candidate = null; text.Clear(); status.Text = ex.Message; }
            finally
            {
                busy = false; path.IsEnabled = choose.IsEnabled = analyze.IsEnabled = close.IsEnabled = true;
                review.IsEnabled = save.IsEnabled = candidate != null;
            }
        }
    }

    public static string? LinkedEpub(Book book)
    {
        if (book.Calibre is not { } source) return null;
        var file = source.Metadata.Files.FirstOrDefault(file => file.Format.Equals("EPUB", StringComparison.OrdinalIgnoreCase));
        return file == null ? null : CalibreImporter.ResolvePath(source.LibraryFolder, file.RelativePath);
    }
}
