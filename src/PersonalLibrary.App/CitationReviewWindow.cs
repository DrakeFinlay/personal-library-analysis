using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class CitationReviewWindow : Window
{
    public CitationReviewWindow(LibraryStore store, Book source, EpubCitation citation)
    {
        source = store.FindBook(source.Id) ?? throw new InvalidOperationException("The source book is no longer saved.");
        Title = "Review citation attribution and dates"; Width = 760; Height = 800; MinWidth = 620; MinHeight = 550;
        Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var snapshot = store.CitationReviews(source.Id);
        var existing = snapshot.Entries.GetValueOrDefault(CitationProvenance.Fingerprint(citation)) ?? citation.Attribution ?? CitationProvenance.Infer(citation.Section);
        var matches = BibliographyMatches.Find(citation, store.Books()); var target = matches.Length == 1 ? matches[0] : null;
        var root = new StackPanel { Margin = new Thickness(20) }; Content = Ui.Scroll(root);
        void Text(string value) { var label = Ui.Text(value); label.TextWrapping = TextWrapping.Wrap; root.Children.Add(label); }
        root.Children.Add(Ui.Heading("Review this reference", 25));
        Text($"Source bibliography: {source.Title}\nListed work: {citation.Title} · {citation.Author}");
        Text(citation.Text); Text($"Section / location: {citation.Section} · {citation.ArchiveEntry}");
        Text($"Source original year: {source.OriginalYear?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}. Edit the source book to correct that work date. Attribution applies to this evidence, not every reference in the book.");
        var roles = Enum.GetValues<CitationRole>();
        var role = new ComboBox { ItemsSource = roles.Select(value => CitationProvenance.Label(new(value, Reviewed: value != CitationRole.Unreviewed))).ToArray(), SelectedIndex = Array.IndexOf(roles, existing.Role), Margin = new Thickness(0, 0, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetName(role, "Citation attribution"); root.Children.Add(role);
        var contributor = Ui.Input(root, "Responsible contributor (editor, translator, etc.; optional)", existing.Contributor);
        var sourceEdition = Ui.Input(root, "Source edition year used for this bibliography (optional)", existing.SourceEditionYear?.ToString(CultureInfo.InvariantCulture) ?? "");
        var original = Ui.Input(root, "Cited work original publication year (blank uses a unique My Books match, if known)", existing.CitedOriginalYear?.ToString(CultureInfo.InvariantCulture) ?? "");
        var edition = Ui.Input(root, "Cited edition / reference year (blank uses a single year found in the citation)", existing.CitedEditionYear?.ToString(CultureInfo.InvariantCulture) ?? "");
        Text("Use the original work date separately from reprint/edition dates. Unknown stays unknown. Later editorial material and suggested reading are not original-author citations.");
        var status = Ui.Text("", 13, "MutedBrush"); status.TextWrapping = TextWrapping.Wrap; root.Children.Add(status);
        CitationAttribution Value() => new(roles[role.SelectedIndex], contributor.Text.Trim(), Year(sourceEdition), Year(original), Year(edition), roles[role.SelectedIndex] != CitationRole.Unreviewed);
        root.Children.Add(Ui.Button("Check chronology", () =>
        {
            var value = Value(); CitationProvenance.Validate(value);
            var assessment = CitationProvenance.Assess(source, citation with { Attribution = value }, target);
            status.Text = assessment.Warning.Length > 0 ? assessment.Warning : "No date conflict detected with the known dates. This does not verify missing dates or attribution.";
        }));
        root.Children.Add(Ui.Button("Save attribution review", () =>
        {
            store.SaveCitationReview(source.Id, citation, Value(), snapshot.Hash); DialogResult = true;
        }, true));
        root.Children.Add(Ui.Button("Cancel", Close));
    }

    private static int? Year(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null :
        int.TryParse(input.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : throw new InvalidOperationException("Enter a whole year or leave it blank.");
}
