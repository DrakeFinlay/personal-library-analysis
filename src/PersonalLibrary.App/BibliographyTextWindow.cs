using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class BibliographyTextWindow : Window
{
    public BibliographyTextWindow(LibraryStore store, Book book, bool startEditing = false, bool scanOnOpen = false)
    {
        Title = $"{book.Title} — Bibliography";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 850;
        Height = 680;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var saved = store.Bibliography(book.Id);
        var draftImages = new List<string>();
        DirectoryInfo? staging = null;
        var scanning = false;
        CancellationTokenSource? scanCancellation = null;
        var root = new DockPanel { Margin = new Thickness(22) };
        var heading = new StackPanel();
        heading.Children.Add(Ui.Heading(book.Title));
        heading.Children.Add(Ui.Text(book.Author, 14, "MutedBrush"));
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var status = Ui.Text("", 13, "MutedBrush");
        status.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(status);
        var actions = new WrapPanel();
        footer.Children.Add(actions);
        var text = new TextBox
        {
            Text = saved.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        System.Windows.Automation.AutomationProperties.SetName(text, "Bibliography text");
        root.Children.Add(text);
        var edit = new Button { Content = "Edit bibliography" };
        var scan = new Button { Content = "Scan images…" };
        var cancelScan = Ui.Button("Cancel scan", () => scanCancellation?.Cancel());
        cancelScan.Visibility = Visibility.Collapsed;
        var review = Ui.Button("Review citations / local matches", () =>
        {
            var library = store.Books();
            var entries = BibliographyMatches.ReadText(text.Text, library);
            if (entries.Length == 0)
                throw new InvalidOperationException("No identifiable citations yet. The complete text can still be saved. Correct OCR errors and separate references with blank lines, then review again.");
            var preview = new EpubBibliography("Editable bibliography text", "",
                ["Possible matches only. OCR can lose formatting; unrecognized text is preserved in the text editor. Review and correct before saving."], entries, 0);
            new EpubBibliographyWindow(store, book, preview, showAlternate: false, draftPreview: true) { Owner = this }.ShowDialog();
        });
        var save = Ui.Button("Save bibliography", () =>
        {
            if (string.IsNullOrWhiteSpace(text.Text))
                throw new InvalidOperationException("Paste citation text or scan images before saving.");
            store.SaveBibliography(book.Id, text.Text, saved.Hash, draftImages);
            DialogResult = true;
        }, true);
        var close = Ui.Button("Close", Close);
        foreach (var button in new[] { edit, scan, cancelScan, review, save, close }) actions.Children.Add(button);
        edit.Click += (_, _) => SetEditing(true);

        scan.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = "Select bibliography page images",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.webp;*.tif;*.tiff;*.bmp|All files|*.*",
                Multiselect = true
            };
            if (picker.ShowDialog(this) != true) return;
            var files = picker.FileNames.OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var selection = new PhotoPagesWindow(files) { Owner = this };
            if (selection.ShowDialog() != true) return;
            var pages = selection.Pages;
            scanning = true;
            scanCancellation = new CancellationTokenSource();
            cancelScan.Visibility = Visibility.Visible;
            scan.IsEnabled = review.IsEnabled = save.IsEnabled = close.IsEnabled = false;
            var failures = new List<string>();
            try
            {
                staging ??= Directory.CreateTempSubdirectory("personal-library-ocr-");
                for (var index = 0; index < pages.Length; index++)
                {
                    scanCancellation.Token.ThrowIfCancellationRequested();
                    var page = pages[index];
                    var filename = Path.GetFileName(page.Path);
                    status.Text = $"Straightening / scanning {index + 1} of {pages.Length} · {filename}";
                    try
                    {
                        var folder = staging.CreateSubdirectory(Guid.NewGuid().ToString("N"));
                        var staged = Path.Combine(folder.FullName, filename);
                        var prepared = Path.Combine(folder.FullName, $"area-{page.AreaNumber}.png");
                        await Task.Run(() =>
                        {
                            File.Copy(page.Path, staged);
                            PhotoPageImage.Prepare(staged, page.Area, page.ImproveContrast, prepared);
                        });
                        scanCancellation.Token.ThrowIfCancellationRequested();
                        var recognizedPage = await BibliographyOcr.ReadAsync(prepared, scanCancellation.Token);
                        var recognized = OcrParagraphs.Reconstruct(recognizedPage);
                        if (recognized.Length == 0) throw new InvalidOperationException("No text was recognized.");
                        var evidence = Path.Combine(folder.FullName, $"area-{page.AreaNumber}.ocr.json");
                        await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new PhotoOcrEvidence(filename, page.Area, page.ImproveContrast,
                            recognizedPage), LibraryStore.Json), scanCancellation.Token);
                        draftImages.Add(staged);
                        draftImages.Add(prepared); draftImages.Add(evidence);
                        var warnings = OcrParagraphs.ReviewWarnings(recognizedPage);
                        var warning = warnings.Length == 0 ? "" : "\n\n> Review: " + string.Join(" ", warnings);
                        text.AppendText((text.Text.Length > 0 ? "\n\n" : "") + $"## Photo: {filename} · area {page.AreaNumber}\n\n" + recognized + warning);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { failures.Add($"{filename} · {ex.Message}"); }
                }
                status.Text = failures.Count == 0
                    ? "Review the reconstructed notes and citations, then use Review citations / local matches and save. Originals, selected areas and raw OCR are retained when saved."
                    : string.Join("\n", failures.Take(3)) + (failures.Count > 3 ? $"\n{failures.Count - 3} more images failed." : "");
                text.ScrollToEnd();
            }
            catch (OperationCanceledException) { status.Text = "Scan cancelled. Successfully scanned pages remain in your draft for review."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally
            {
                scanning = false;
                scan.IsEnabled = review.IsEnabled = save.IsEnabled = close.IsEnabled = true;
                cancelScan.Visibility = Visibility.Collapsed;
                scanCancellation.Dispose(); scanCancellation = null;
            }
        };
        Closing += (_, args) =>
        {
            if (scanning) { args.Cancel = true; return; }
            if (DialogResult != true && text.Text != saved.Text)
                args.Cancel = MessageBox.Show(this, "Discard your unsaved bibliography changes?", "Bibliography",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes;
        };
        Closed += (_, _) =>
        {
            try { staging?.Delete(recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        Content = root;
        SetEditing(startEditing);
        if (scanOnOpen) Loaded += (_, _) => scan.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        void SetEditing(bool editing)
        {
            text.IsReadOnly = !editing;
            edit.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            scan.Visibility = save.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            status.Text = editing ? "Paste text or scan bibliography / notes photos. Select each page area before recognition; review the text before saving." : "";
            if (editing) text.Focus();
        }
    }
}
