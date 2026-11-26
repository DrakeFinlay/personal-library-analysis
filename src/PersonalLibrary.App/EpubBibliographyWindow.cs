using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class EpubBibliographyWindow : Window
{
    private sealed record Row(EpubCitation Citation, Book[] Matches)
    {
        public string Label => $"{Citation.Number}. {CitationLabel(Citation)}" + (Citation.Interpretation is { } info ? "\n" + CitationProcessing.Label(info) : "");
    }

    private static string CitationLabel(EpubCitation citation) => citation.Title.Length == 0 ? citation.Text
        : citation.Author.Length == 0 ? citation.Title : $"{citation.Title} — {citation.Author}";

    public EpubBibliographyWindow(LibraryStore store, Book book, EpubBibliography bibliography,
        EpubCitation? selectedCitation = null, bool showAlternate = true, bool draftPreview = false, bool processedView = false)
    {
        Title = book.Title + " — Bibliography";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 1060;
        Height = 740;
        MinWidth = 700;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading(book.Title));
        header.Children.Add(Ui.Text(book.Author, 14, "MutedBrush"));
        var actions = new WrapPanel();
        header.Children.Add(actions);
        var onlineSource = bibliography.OnlineSource;
        if (onlineSource != null && Uri.TryCreate(onlineSource.ItemUrl, UriKind.Absolute, out var sourcePage))
            actions.Children.Add(Ui.Button("Open Internet Archive edition", () => PublicBibliographyWindow.OpenArchivePage(sourcePage)));
        if (showAlternate)
        {
            var alternate = onlineSource == null ? store.ReadOnlineBibliography(book.Id) : store.ReadEpubBibliography(book.Id);
            if (alternate != null)
                actions.Children.Add(Ui.Button(onlineSource == null ? "Browse online bibliography" : "Browse local EPUB bibliography", () =>
                    new EpubBibliographyWindow(store, book, alternate, showAlternate: false) { Owner = this }.ShowDialog()));
        }
        var personal = Ui.Button("Open pasted or scanned text", () =>
            new BibliographyTextWindow(store, book) { Owner = this }.ShowDialog());
        personal.Visibility = store.Bibliography(book.Id).Hash == null ? Visibility.Collapsed : Visibility.Visible;
        actions.Children.Add(personal);
        actions.Children.Add(Ui.Button("Add or edit text…", () =>
        {
            if (new BibliographyTextWindow(store, book, startEditing: true) { Owner = this }.ShowDialog() == true)
                personal.Visibility = Visibility.Visible;
        }));
        if (draftPreview) actions.Visibility = Visibility.Collapsed;
        var library = store.Books().Where(candidate => candidate.Id != book.Id).ToArray();
        Row[] LoadRows() => (processedView ? store.ReadCitationEvidence(book.Id) : store.ProcessCitations(book.Id, bibliography.Entries))
            .Select(citation => new Row(citation, BibliographyMatches.Find(citation, library))).ToArray();
        var rows = LoadRows();
        var sourceLabel = onlineSource == null ? bibliography.SourceFile
            : string.Join(" · ", new[] { onlineSource.Provider, onlineSource.Title, onlineSource.Date }.Where(value => value.Length > 0));
        var summary = Ui.Text("", 14, "MutedBrush"); header.Children.Add(summary);
        void Summary() => summary.Text = $"Identified references: {rows.Count(row => row.Citation.Interpretation?.CanConnect == true)} · Need interpretation review: {rows.Count(row => row.Citation.Interpretation is { CanConnect: false, Resolution: not CitationResolution.FullNote, Excluded: false })} · Possible library matches: {rows.Count(row => row.Matches.Length > 0)} · {sourceLabel}";
        Summary();
        if (bibliography.UnreadableDocuments > 0)
            header.Children.Add(Ui.Text($"{bibliography.UnreadableDocuments} EPUB documents could not be read.", 13, "MutedBrush"));
        foreach (var note in bibliography.IntroductoryNotes) header.Children.Add(Ui.Text(note, 13, "MutedBrush"));
        var search = Ui.Input(header, "Search citations");
        var filters = new WrapPanel();
        var sections = new ComboBox
        {
            Width = 205,
            ItemsSource = new[] { "All sections" }.Concat(rows.Select(row => row.Citation.Section).Distinct()),
            SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 10)
        };
        var matchesOnly = new CheckBox { Content = "With local matches" };
        var needsReview = new CheckBox { Content = "Needs interpretation review", Margin = new Thickness(12, 0, 0, 0) };
        var fullNotes = new CheckBox { Content = "Include complete notes", Margin = new Thickness(12, 0, 0, 0) };
        var count = Ui.Text("", 13, "MutedBrush");
        count.Margin = new Thickness(10, 5, 0, 8);
        filters.Children.Add(sections);
        filters.Children.Add(matchesOnly);
        filters.Children.Add(needsReview);
        filters.Children.Add(fullNotes);
        filters.Children.Add(count);
        header.Children.Add(filters);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        root.Children.Add(columns);
        var list = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(Row.Label)) };
        columns.Children.Add(list);
        var details = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        var detailsScroll = Ui.Scroll(details);
        Grid.SetColumn(detailsScroll, 1);
        columns.Children.Add(detailsScroll);
        list.SelectionChanged += (_, _) =>
        {
            details.Children.Clear();
            if (list.SelectedItem is not Row row) return;
            var citation = row.Citation;
            var heading = Ui.Heading(citation.Title.Length > 0 ? CitationLabel(citation) : $"Citation {citation.Number}", 23);
            heading.TextWrapping = TextWrapping.Wrap;
            details.Children.Add(heading);
            details.Children.Add(Ui.Text($"{citation.Section} · Citation {citation.Number}", 13, "MutedBrush"));
            if (citation.Interpretation is { } interpretation)
            {
                var interpretationText = Ui.Text(CitationProcessing.Label(interpretation) + $"\nNote: {interpretation.NoteNumber?.ToString() ?? "not supplied"} · Locator: {interpretation.Locator} · Volume: {interpretation.Volume} · Edition year: {interpretation.EditionYear?.ToString() ?? "unknown"}\n" +
                    (interpretation.Contributor.Length > 0 ? "Translator / contributor: " + interpretation.Contributor + "\n" : "") + interpretation.Reason + "\nReference wording: " + interpretation.Reference, 13, "MutedBrush");
                interpretationText.TextWrapping = TextWrapping.Wrap; details.Children.Add(interpretationText);
                if (!draftPreview && interpretation.Resolution != CitationResolution.FullNote) details.Children.Add(Ui.Button("Review work identity…", () =>
                {
                    if (new CitationIdentityWindow(store, book, citation) { Owner = this }.ShowDialog() != true) return;
                    var fingerprint = CitationProvenance.Fingerprint(citation); rows = LoadRows(); Summary(); Populate();
                    list.SelectedItem = rows.FirstOrDefault(item => CitationProvenance.Fingerprint(item.Citation) == fingerprint);
                }));
            }
            var citationText = Ui.Text(citation.Text, 16);
            citationText.TextWrapping = TextWrapping.Wrap;
            details.Children.Add(citationText);
            var assessment = CitationProvenance.Assess(book, citation, row.Matches.Length == 1 ? row.Matches[0] : null);
            var provenance = Ui.Text(CitationProvenance.Label(assessment.Attribution) + (assessment.Warning.Length > 0 ? "\n" + assessment.Warning : ""), 13, "MutedBrush"); provenance.TextWrapping = TextWrapping.Wrap; details.Children.Add(provenance);
            if (!draftPreview) details.Children.Add(Ui.Button("Review attribution / dates…", () =>
            {
                if (new CitationReviewWindow(store, book, citation) { Owner = this }.ShowDialog() == true)
                {
                    var index = Array.IndexOf(rows, row);
                    rows[index] = row with { Citation = store.ReviewedCitations(book.Id, [citation])[0] };
                    Populate(); list.SelectedItem = rows[index];
                }
            }));
            details.Children.Add(Ui.Text(citation.ArchiveEntry + (citation.Anchor == null ? "" : "#" + citation.Anchor), 12, "MutedBrush"));
            details.Children.Add(Ui.Button("Find this citation online…", () =>
                new MetadataLookupWindow(citation.Title, citation.Author) { Owner = this }.ShowDialog()));
            details.Children.Add(Ui.Heading("Possible matches in My Books", 19));
            if (row.Matches.Length == 0) details.Children.Add(Ui.Text("No local match found for this citation.", 13, "MutedBrush"));
            foreach (var match in row.Matches)
                details.Children.Add(Ui.Button($"{match.Title} · {match.Author}", () =>
                    new BookEditor(store, match) { Owner = this }.ShowDialog()));
            detailsScroll.ScrollToTop();
        };
        void Populate()
        {
            var visible = rows.Where(row => (sections.SelectedIndex == 0 || row.Citation.Section == sections.SelectedItem as string)
                && (fullNotes.IsChecked == true || row.Citation.Interpretation?.Resolution != CitationResolution.FullNote)
                && (matchesOnly.IsChecked != true || row.Matches.Length > 0)
                && (needsReview.IsChecked != true || row.Citation.Interpretation is { CanConnect: false, Resolution: not CitationResolution.FullNote, Excluded: false })
                && (row.Citation.Text + " " + row.Citation.Title + " " + row.Citation.Author).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = visible;
            list.SelectedIndex = visible.Length > 0 ? 0 : -1;
            count.Text = $"{visible.Length} shown";
        }
        search.TextChanged += (_, _) => Populate();
        sections.SelectionChanged += (_, _) => Populate();
        matchesOnly.Click += (_, _) => Populate();
        needsReview.Click += (_, _) => Populate();
        fullNotes.Click += (_, _) => Populate();
        Content = root;
        Populate();
        if (selectedCitation != null && rows.FirstOrDefault(row => CitationProvenance.Fingerprint(row.Citation) == CitationProvenance.Fingerprint(selectedCitation)) is { } selected)
        {
            list.SelectedItem = selected;
            list.ScrollIntoView(selected);
        }
    }

    private static string Normalize(string value)
    {
        var normalized = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            normalized.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return string.Join(' ', normalized.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
