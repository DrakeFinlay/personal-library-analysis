using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

public sealed partial class MainWindow
{
    private UIElement Discovery()
    {
        var library = books.ToArray();
        var root = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Ui.Heading("Discovery"));
        var explanation = Ui.Text("Find reading leads where your bibliographies meet. Shared citations include unrated sources; finished books rated four or five stars boost the ranking. A listing is not proof of endorsement.", 14, "MutedBrush");
        explanation.TextWrapping = TextWrapping.Wrap; header.Children.Add(explanation);
        var scope = new WrapPanel(); header.Children.Add(scope);
        var originalOnly = new CheckBox { Content = "Reviewed original-text citations only", Margin = new Thickness(0, 10, 15, 5) }; scope.Children.Add(originalOnly);
        var refresh = new Button { Content = "Refresh discovery" }; scope.Children.Add(refresh);
        var status = Ui.Text("Loading saved bibliography connections…", 13, "MutedBrush"); status.TextWrapping = TextWrapping.Wrap; header.Children.Add(status);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var shared = new TabItem { Header = "Shared reading leads" }; var enjoyed = new TabItem { Header = "Cited by books you enjoyed" };
        tabs.Items.Add(shared); tabs.Items.Add(enjoyed);
        var sharedRoot = new DockPanel { Margin = new Thickness(10) }; shared.Content = sharedRoot;
        var filters = new StackPanel(); DockPanel.SetDock(filters, Dock.Top); sharedRoot.Children.Add(filters);
        var search = Ui.Input(filters, "Find a cited work (title / author)");
        var sourceList = new ListBox { SelectionMode = SelectionMode.Multiple, MaxHeight = 150, MinHeight = 70 };
        System.Windows.Automation.AutomationProperties.SetName(sourceList, "Required citing source books");
        var sourcePanel = new StackPanel();
        var sourceNote = Ui.Text("Click source books to require a citation from every selected source. Click a selected book again to remove it. No selection searches all sources.", 13, "MutedBrush"); sourceNote.TextWrapping = TextWrapping.Wrap;
        sourcePanel.Children.Add(sourceNote); sourcePanel.Children.Add(sourceList);
        sourcePanel.Children.Add(Ui.Button("Clear source selection", () => sourceList.UnselectAll()));
        filters.Children.Add(new Expander { Header = "Focus on specific source books (optional)", Content = sourcePanel, Margin = new Thickness(0, 0, 0, 8) });
        var range = new WrapPanel(); filters.Children.Add(range);
        var includeOther = new CheckBox { Content = "Include already-read books and other statuses", Margin = new Thickness(0, 8, 12, 5) }; range.Children.Add(includeOther);
        var count = Ui.Text("", 13, "MutedBrush"); range.Children.Add(count);
        var all = false; var allButton = new Button { Content = "Show all leads" }; range.Children.Add(allButton);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); sharedRoot.Children.Add(columns);
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 0, 14, 0) };
        System.Windows.Automation.AutomationProperties.SetName(table, "Shared reading leads"); columns.Children.Add(table);
        table.Columns.Add(new DataGridTextColumn { Header = "Work", Binding = new Binding(nameof(CitationDiscoveryLead.Title)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Author", Binding = new Binding(nameof(CitationDiscoveryLead.Author)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Citing books", Binding = new Binding(nameof(CitationDiscoveryLead.CitingBooks)) });
        table.Columns.Add(new DataGridTextColumn { Header = "Enjoyed sources", Binding = new Binding(nameof(CitationDiscoveryLead.EnjoyedSources)) });
        table.Columns.Add(new DataGridTextColumn { Header = "Library", Binding = new Binding(nameof(CitationDiscoveryLead.Library)) });
        var details = new StackPanel(); var detailsScroll = Ui.Scroll(details); Grid.SetColumn(detailsScroll, 1); columns.Children.Add(detailsScroll);
        var enjoyedPanel = new StackPanel { Margin = new Thickness(10) }; enjoyed.Content = Ui.Scroll(enjoyedPanel);
        BibliographyGraph? graph = null;
        CitationDiscoveryLead[] readingLeads = []; CitationDiscoveryLead[] otherLeads = [];
        IReadOnlyDictionary<string, string>? preferences = null; var loading = false;
        void Text(string value, double size = 13) { var text = Ui.Text(value, size); text.TextWrapping = TextWrapping.Wrap; details.Children.Add(text); }
        void Details()
        {
            details.Children.Clear();
            if (table.SelectedItem is not CitationDiscoveryLead lead) { Text("No shared reading leads match this view. Try clearing source choices or include already-read books. Unresolved citations cannot create shared connections."); return; }
            Text(lead.Title, 22); Text(lead.Author); Text(lead.Library);
            Text($"Listed by {lead.CitingBooks} distinct source books. Finished sources rated four or five stars: {lead.EnjoyedSources}. Repeated notes from one book do not increase this source count.");
            var actions = new WrapPanel(); details.Children.Add(actions);
            if (lead.Pick.LocalBook is { } book) actions.Children.Add(Ui.Button("Open in My Books", () => Edit(book)));
            else actions.Children.Add(Ui.Button("Find or add book…", () => AddBook(lead.Title, lead.Author)));
            actions.Children.Add(Ui.Button("Explore shared connections", () => new BibliographyConnectionsWindow(store, (sourceBook, citation) => OpenBibliography(sourceBook, citation), lead.GraphKey, originalOnly.IsChecked == true) { Owner = this }.ShowDialog()));
            actions.Children.Add(Ui.Button("Not now", () => SaveCitationPreference(lead.Pick, dismiss: false)));
            actions.Children.Add(Ui.Button("Not interested", () => SaveCitationPreference(lead.Pick, dismiss: true)));
            Text("Why this appears", 18);
            foreach (var evidence in lead.Pick.Sources)
            {
                var label = $"{evidence.Source.Title} · {(evidence.Source.Rating is { } rating ? rating + "/5" : "unrated")}";
                var button = Ui.Button(label, () => OpenBibliography(evidence.Source, evidence.Citation));
                button.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }; button.ToolTip = evidence.Citation.Text; details.Children.Add(button);
                Text(CitationProvenance.Label(evidence.Citation.Attribution ?? CitationProvenance.Infer(evidence.Citation.Section)), 12);
                Text(evidence.Citation.Interpretation?.Reference ?? evidence.Citation.Text);
            }
            detailsScroll.ScrollToTop();
        }
        void Filter()
        {
            if (graph == null || loading) return;
            var required = sourceList.SelectedItems.OfType<DiscoverySource>().Select(item => item.Id).ToHashSet();
            var leads = (includeOther.IsChecked == true ? otherLeads : readingLeads)
                .Where(lead => required.All(id => lead.Pick.Sources.Any(source => source.Source.Id == id)) &&
                    (lead.Title + " " + lead.Author).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) && !CitationHidden(lead.Pick, preferences)).ToArray();
            table.ItemsSource = (all ? leads : leads.Take(50)).ToArray(); table.SelectedIndex = leads.Length > 0 ? 0 : -1;
            count.Text = $"{(all ? leads.Length : Math.Min(50, leads.Length))} of {leads.Length} shared leads · Required sources: {required.Count}";
            allButton.Content = all ? "Show first 50" : "Show all leads"; allButton.Visibility = all || leads.Length > 50 ? Visibility.Visible : Visibility.Collapsed;
            Details();
        }
        async Task Load()
        {
            var onlyOriginal = originalOnly.IsChecked == true;
            refresh.IsEnabled = originalOnly.IsEnabled = sourceList.IsEnabled = false; busyCount++; loading = true;
            try
            {
                status.Text = "Reading saved bibliography connections…";
                var data = await Task.Run(() =>
                {
                    var currentLibrary = store.Books().ToArray();
                    var corpus = store.ReadBibliographyGraph(onlyOriginal);
                    return (Library: currentLibrary, Graph: corpus, Reading: CitationDiscovery.Rank(currentLibrary, corpus), Other: CitationDiscovery.Rank(currentLibrary, corpus, includeOtherStatuses: true), Preferences: store.CitationPreferences());
                });
                if (!root.IsLoaded) return;
                library = data.Library; books = library.ToList(); graph = data.Graph; readingLeads = data.Reading; otherLeads = data.Other; preferences = data.Preferences;
                var selected = sourceList.SelectedItems.OfType<DiscoverySource>().Select(item => item.Id).ToHashSet();
                sourceList.ItemsSource = graph.Nodes.Values.Where(node => node.HasBibliography && node.BookId != null).OrderBy(node => node.Title)
                    .Select(node => new DiscoverySource(node.BookId!.Value, node.Label)).ToArray();
                foreach (var item in sourceList.Items.OfType<DiscoverySource>().Where(item => selected.Contains(item.Id))) sourceList.SelectedItems.Add(item);
                enjoyedPanel.Children.Clear();
                AddCitedRecommendationSection(enjoyedPanel, Recommendations.Overview(library), id => data.Graph.Connections.SelectMany(edge => edge.Evidence).Where(item => item.Source.Id == id).Select(item => item.Citation), preferences);
                status.Text = $"{graph.SourceBooks} saved bibliography sources · {(onlyOriginal ? "Reviewed original-text scope" : "All reference material, including unreviewed/editorial/further reading")} · Ranked by enjoyed-source count, then distinct citing books. Refresh after changing source evidence.";
                loading = false; Filter();
            }
            catch (Exception ex) { status.Text = "Could not load discovery: " + ex.Message; }
            finally { refresh.IsEnabled = originalOnly.IsEnabled = sourceList.IsEnabled = true; busyCount--; loading = false; }
        }
        table.SelectionChanged += (_, _) => Details();
        refresh.Click += async (_, _) => await Load();
        allButton.Click += (_, _) => { all = !all; Filter(); };
        search.TextChanged += (_, _) => { all = false; Filter(); };
        sourceList.SelectionChanged += (_, _) => { all = false; Filter(); };
        includeOther.Click += (_, _) => { all = false; Filter(); };
        originalOnly.Click += async (_, _) => await Load();
        root.Loaded += async (_, _) => await Load();
        return root;
    }

    private bool CitationHidden(CitedRecommendation pick, IReadOnlyDictionary<string, string>? preferences = null)
    {
        string Preference(string key) => preferences != null ? preferences.GetValueOrDefault(key, "") : store.Setting(key, "");
        var preference = Preference(CitationPreferenceKey(pick));
        if (preference.Length == 0) preference = pick.Sources.Select(source => Preference(CitationPreferenceKey(CitedRecommendations.LegacyPreferenceKey(source.Citation)))).FirstOrDefault(value => value.Length > 0) ?? "";
        return preference == "dismissed" || long.TryParse(preference, out var until) && until > DateTimeOffset.Now.ToUnixTimeSeconds();
    }
    private sealed record DiscoverySource(Guid Id, string Label) { public override string ToString() => Label; }
}
