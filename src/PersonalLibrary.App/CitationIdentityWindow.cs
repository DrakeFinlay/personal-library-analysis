using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class CitationIdentityWindow : Window
{
    public CitationIdentityWindow(LibraryStore store, Book source, EpubCitation citation)
    {
        Title = "Review citation work identity"; Width = 760; Height = 740; MinWidth = 600; MinHeight = 500;
        Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(20) }; Content = Ui.Scroll(root);
        void Text(string value) { var text = Ui.Text(value); text.TextWrapping = TextWrapping.Wrap; root.Children.Add(text); }
        root.Children.Add(Ui.Heading("Identify this cited work", 25)); Text("Source: " + source.Title);
        Text(citation.Interpretation?.Reference ?? citation.Text); Text(citation.Interpretation?.Reason ?? "Inferred source title.");
        var snapshot = store.CitationIdentities();
        var identities = snapshot.Entries.Values.Where(value => !value.Excluded).DistinctBy(value => value.Id).OrderBy(value => value.Title).ToArray();
        Text("Correcting this identity updates its aliases across saved bibliographies. To combine two identities, choose an existing reviewed work below. Original photos, OCR text and notes are preserved. Attribution review is separate.");
        var merge = new ComboBox { ItemsSource = new[] { "Edit this identity / identify this reference" }.Concat(identities.Select(value => value.Title + " · " + value.Author)).ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetName(merge, "Merge into reviewed work"); root.Children.Add(merge);
        var title = Ui.Input(root, "Canonical work title", citation.Title); var author = Ui.Input(root, "Canonical author / contributor", citation.Author);
        var matches = BibliographyMatches.Find(citation, store.Books());
        if (matches.Length > 0)
        {
            var local = new ComboBox { ItemsSource = new[] { "Use title / author from a possible My Books match…" }.Concat(matches.Select(book => book.Title + " · " + book.Author)).ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(local, "Use library title and author"); root.Children.Add(local);
            local.SelectionChanged += (_, _) => { if (local.SelectedIndex > 0) { title.Text = matches[local.SelectedIndex - 1].Title; author.Text = matches[local.SelectedIndex - 1].Author; } };
        }
        var exclude = new CheckBox { Content = "Exclude this interpretation from work links (keep the evidence)", IsChecked = citation.Interpretation?.Excluded == true, Margin = new Thickness(0, 10, 0, 10) }; root.Children.Add(exclude);
        merge.SelectionChanged += (_, _) =>
        {
            var identity = merge.SelectedIndex > 0 ? identities[merge.SelectedIndex - 1] : null;
            title.Text = identity?.Title ?? citation.Title; author.Text = identity?.Author ?? citation.Author;
        };
        Text("Identify only what the evidence supports. Leave uncertain references unresolved by cancelling. A work identity can exist without adding a book to My Books. Use profile backups to restore an earlier identity map if a merge was mistaken.");
        root.Children.Add(Ui.Button("Save work identity", () =>
        {
            store.SaveCitationIdentity(source.Id, citation, title.Text, author.Text, merge.SelectedIndex > 0 ? identities[merge.SelectedIndex - 1].Id : null, snapshot.Hash, exclude.IsChecked == true);
            DialogResult = true;
        }, true));
        root.Children.Add(Ui.Button("Cancel", Close));
    }
}
