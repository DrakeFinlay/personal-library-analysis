using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class BibliographyConnectionsWindow : Window
{
    private sealed record WorkRow(BibliographyNode Work, int Incoming, int Outgoing)
    {
        public string Label => $"{Work.Title} · {Work.Author}\n{Incoming} citing books · {Outgoing} references";
    }

    public BibliographyConnectionsWindow(LibraryStore store, Action<Book, EpubCitation?> openSource, string? initialFocus = null, bool originalTextOnly = false)
    {
        Title = "Bibliography connections & statistics"; Width = 1480; Height = 900; MinWidth = 1050; MinHeight = 650;
        Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Ui.Heading("Bibliography connections", 27));
        var note = Ui.Text("Arrows: source bibliography → listed work. Incoming books list the selected work; outgoing references are listed by it. Shared connections only. Work identity and original-author attribution are separate; inspect the evidence.", 13, "MutedBrush");
        note.TextWrapping = TextWrapping.Wrap; header.Children.Add(note);
        var legend = new WrapPanel(); header.Children.Add(legend);
        foreach (var (label, color) in new[] { ("In My Books", "#DFF1E3"), ("Not in My Books", "#C1DAFF"), ("Library match unresolved", "#FFE8B6") })
            legend.Children.Add(new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 10, 8), Child = new TextBlock { Text = label, Foreground = Brushes.Black } });
        var originalOnly = new CheckBox { Content = "Reviewed original-text citations only (exclude editorial / further reading / chronology warnings)", IsChecked = originalTextOnly, Margin = new Thickness(0, 0, 0, 8) }; header.Children.Add(originalOnly);
        var summary = Ui.Text("Loading saved bibliographies…", 13, "MutedBrush"); header.Children.Add(summary);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var graphTab = new TabItem { Header = "Explore connections" }; var statsTab = new TabItem { Header = "Repeatedly cited works" }; tabs.Items.Add(graphTab); tabs.Items.Add(statsTab);
        var statsSearch = new TextBox();
        var includeSingle = new CheckBox { Content = "Also show repeated evidence within a single source", Margin = new Thickness(0, 0, 0, 10) };
        var statsCount = Ui.Text("", 13, "MutedBrush");
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true, CanUserAddRows = false };
        var graphGrid = new Grid { Margin = new Thickness(8) }; graphGrid.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); graphGrid.ColumnDefinitions.Add(new()); graphGrid.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); graphTab.Content = graphGrid;
        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 10, 0) }; graphGrid.Children.Add(searchPanel);
        var searchHeader = new StackPanel(); DockPanel.SetDock(searchHeader, Dock.Top); searchPanel.Children.Add(searchHeader);
        var search = Ui.Input(searchHeader, "Find a shared work (title / author)");
        var searchCount = Ui.Text("", 12, "MutedBrush"); searchHeader.Children.Add(searchCount);
        var traceSelection = new CheckBox { Content = "Trace from current focus", Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "When checked, choosing a shared work highlights a path from the current focus. Otherwise, choosing a work updates the graph immediately." };
        searchHeader.Children.Add(traceSelection);
        var choices = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(WorkRow.Label)) }; searchPanel.Children.Add(choices);
        var middle = new DockPanel(); Grid.SetColumn(middle, 1); graphGrid.Children.Add(middle);
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); middle.Children.Add(toolbar);
        var depth = new ComboBox { ItemsSource = new[] { 1, 2, 3, 4, 5 }, SelectedIndex = 1, Width = 52, Margin = new Thickness(3, 0, 6, 5) };
        toolbar.Children.Add(Ui.Text("Steps:", 12)); toolbar.Children.Add(depth);
        var viewport = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White };
        var network = new BibliographyNetworkCanvas(); viewport.Content = network; middle.Children.Add(viewport);
        var graphStatus = Ui.Text("Choose a work to explore its connections.", 12, "MutedBrush"); graphStatus.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(graphStatus, Dock.Bottom); middle.Children.Insert(middle.Children.Count - 1, graphStatus);
        var detail = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; var detailScroll = Ui.Scroll(detail); Grid.SetColumn(detailScroll, 2); graphGrid.Children.Add(detailScroll);
        BibliographyGraph? graph = null; BibliographyGraph? corpus = null; string? focus = initialFocus; string? selected = null; string[] highlightedPath = []; var visible = new HashSet<string>();
        var syncingChoice = false;
        var zoom = new Slider { Minimum = .35, Maximum = 1.8, Value = 1, Width = 90, Margin = new Thickness(8, 0, 0, 5), ToolTip = "Graph zoom" };
        System.Windows.Automation.AutomationProperties.SetName(zoom, "Graph zoom");
        zoom.ValueChanged += (_, _) => { network.LayoutTransform = new ScaleTransform(zoom.Value, zoom.Value); };
        void Draw(bool center = false)
        {
            if (graph == null || focus == null) return;
            network.Draw(graph, visible, focus, selected, highlightedPath);
            if (center && network.Positions.Count > 0)
                Dispatcher.BeginInvoke(() =>
                {
                    if (network.Positions.Count == 0 || !IsVisible) return;
                    viewport.UpdateLayout();
                    var minX = network.Positions.Values.Min(p => p.X); var maxX = network.Positions.Values.Max(p => p.X);
                    var minY = network.Positions.Values.Min(p => p.Y); var maxY = network.Positions.Values.Max(p => p.Y);
                    zoom.Value = Math.Clamp(Math.Min(viewport.ViewportWidth / (maxX - minX + 240), viewport.ViewportHeight / (maxY - minY + 130)), .35, 1);
                    viewport.UpdateLayout();
                    viewport.ScrollToHorizontalOffset((minX + maxX) / 2 * zoom.Value - viewport.ViewportWidth / 2);
                    viewport.ScrollToVerticalOffset((minY + maxY) / 2 * zoom.Value - viewport.ViewportHeight / 2);
                });
        }
        void Focus(string? key)
        {
            if (graph == null || key == null || !graph.Nodes.ContainsKey(key)) return;
            focus = selected = key; highlightedPath = [];
            syncingChoice = true;
            try { choices.SelectedItem = choices.Items.OfType<WorkRow>().FirstOrDefault(row => row.Work.Key == key); }
            finally { syncingChoice = false; }
            var slice = graph.Neighborhood(key, (int)(depth.SelectedItem ?? 1), 120); visible = slice.Keys.ToHashSet();
            graphStatus.Text = $"Focus: {graph.Nodes[key].Title} · {visible.Count} works shown" + (slice.Omitted > 0 ? $" · {slice.Omitted} more within these steps omitted (120-work initial limit). Explore a narrower focus or expand a selected branch." : ". Select nodes or arrows to inspect evidence; expand several branches together.");
            Draw(true); Details(key);
        }
        depth.SelectionChanged += (_, _) => Focus(focus);
        void Expand()
        {
            if (graph == null || selected == null) return;
            if (!visible.Contains(selected) && visible.Count >= 160) { graphStatus.Text = "The view already contains 160 works. Focus the selected work to explore its branch."; return; }
            var additions = graph.Neighbors(selected).Where(key => !visible.Contains(key)).ToArray();
            visible.Add(selected); foreach (var key in additions.Take(Math.Max(0, 160 - visible.Count))) visible.Add(key);
            graphStatus.Text = $"{visible.Count} works shown · Expanded {graph.Nodes[selected].Title}. " +
                (additions.Any(key => !visible.Contains(key)) ? "Some neighbors are hidden by the 160-work view limit; focus this work to explore them." : "Other expanded branches remain visible.");
            Draw(true); Details(selected);
        }
        void Trace()
        {
            if (graph == null || selected == null || focus == null) return;
            highlightedPath = graph.Path(focus, selected);
            if (highlightedPath.Length == 0) { graphStatus.Text = "No path exists between these works in the shared citation network."; Draw(); Details(selected); return; }
            var required = highlightedPath.Where(key => !visible.Contains(key)).ToArray();
            if (visible.Count + required.Length > 160) visible = highlightedPath.Take(160).ToHashSet();
            else foreach (var key in required) visible.Add(key);
            graphStatus.Text = $"Shortest connection: {highlightedPath.Length - 1} steps (traverses either direction; arrows retain citation direction). " +
                string.Join(" ↔ ", highlightedPath.Select(key => graph.Nodes[key].Title)) + (highlightedPath.Length > 160 ? " · Path exceeds the view limit." : "");
            Draw(true); Details(selected);
        }
        void Select(string key) { selected = key; highlightedPath = []; Details(key); Draw(); }
        void Text(Panel panel, string value, double size = 13) { var text = Ui.Text(value, size); text.TextWrapping = TextWrapping.Wrap; panel.Children.Add(text); }
        void Details(string key)
        {
            if (graph == null) return;
            detail.Children.Clear(); var work = graph.Nodes[key];
            Text(detail, work.Title, 20); Text(detail, work.Author); Text(detail, work.Identity, 12);
            if (work.BookId is { } id) detail.Children.Add(Ui.Button("Open book", () => { if (store.FindBook(id) is { } book) new BookEditor(store, book) { Owner = this }.ShowDialog(); }));
            Text(detail, $"Cited by {graph.CitedBy(key).Length} source books · {graph.References(key).Length} references in this shared network", 14);
            Text(detail, "Incoming: bibliographies listing this work", 18);
            foreach (var edge in graph.CitedBy(key).OrderBy(e => graph.Nodes[e.Source].Title))
                detail.Children.Add(Ui.Button(graph.Nodes[edge.Source].Title + $" ({edge.Evidence.Length} passages)", () => Connection(edge)));
            Text(detail, "Outgoing: works listed by this bibliography", 18);
            foreach (var edge in graph.References(key).OrderBy(e => graph.Nodes[e.Target].Title))
                detail.Children.Add(Ui.Button(graph.Nodes[edge.Target].Title, () => Connection(edge)));
            if (graph.CitedBy(key).Length + graph.References(key).Length == 0) Text(detail, "No identified citation edges. Text-only notes remain in the bibliography.");
            detailScroll.ScrollToTop();
        }
        void Connection(BibliographyConnection edge)
        {
            if (graph == null) return;
            detail.Children.Clear(); Text(detail, $"{graph.Nodes[edge.Source].Title}'s bibliography lists {graph.Nodes[edge.Target].Title}.", 19);
            detail.Children.Add(Ui.Button("Select citing work", () => Select(edge.Source)));
            detail.Children.Add(Ui.Button("Select cited work", () => Select(edge.Target)));
            Text(detail, "Saved evidence · work identity inferred separately from attribution", 13);
            foreach (var evidence in edge.Evidence)
            {
                if (evidence.Citation.Interpretation is { } interpretation)
                {
                    Text(detail, CitationProcessing.Label(interpretation), 14);
                    Text(detail, $"Note: {interpretation.NoteNumber?.ToString() ?? "not supplied"} · Locator: {interpretation.Locator} · Volume: {interpretation.Volume}\n" + interpretation.Reason, 12);
                    detail.Children.Add(Ui.Button("Review work identity…", async () =>
                    {
                        if (new CitationIdentityWindow(store, evidence.Source, evidence.Citation) { Owner = this }.ShowDialog() == true) await LoadGraph();
                    }));
                }
                var target = graph.Nodes[edge.Target].BookId is { } targetId ? store.FindBook(targetId) : null;
                var assessment = CitationProvenance.Assess(evidence.Source, evidence.Citation, target);
                Text(detail, CitationProvenance.Label(assessment.Attribution), 14);
                if (assessment.Attribution.Contributor.Length > 0) Text(detail, "Responsible contributor: " + assessment.Attribution.Contributor);
                Text(detail, $"Source original year: {assessment.SourceOriginalYear?.ToString() ?? "unknown"} · Source edition: {assessment.Attribution.SourceEditionYear?.ToString() ?? "unknown"}\nListed work original year: {assessment.CitedOriginalYear?.ToString() ?? "unknown"} · Reference/edition year: {assessment.ReferenceYear?.ToString() ?? "unknown"}", 12);
                if (assessment.Warning.Length > 0) Text(detail, assessment.Warning, 13);
                Text(detail, evidence.Citation.Section + " · " + evidence.Citation.ArchiveEntry, 12); Text(detail, evidence.Citation.Text);
                detail.Children.Add(Ui.Button("Open source bibliography", () => openSource(evidence.Source, evidence.Citation)));
                detail.Children.Add(Ui.Button("Review attribution / dates…", async () =>
                {
                    if (new CitationReviewWindow(store, evidence.Source, evidence.Citation) { Owner = this }.ShowDialog() == true) await LoadGraph();
                }));
            }
            detailScroll.ScrollToTop();
        }
        network.WorkSelected += Select; network.ConnectionSelected += Connection;
        void Filter()
        {
            if (graph == null) return;
            var found = graph.Nodes.Values.Where(n => graph.CitedBy(n.Key).Length > 1 && n.Label.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(n => graph.CitedBy(n.Key).Length).ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            choices.ItemsSource = found.Take(100).Select(n => new WorkRow(n, graph.CitedBy(n.Key).Length, graph.References(n.Key).Length)).ToArray();
            searchCount.Text = found.Length > 100 ? $"First 100 of {found.Length} shared works · refine search" : $"{found.Length} shared works";
        }
        search.TextChanged += (_, _) => Filter();
        choices.SelectionChanged += (_, _) =>
        {
            if (syncingChoice || choices.SelectedItem is not WorkRow row) return;
            if (traceSelection.IsChecked == true && focus != null) { selected = row.Work.Key; Trace(); }
            else Focus(row.Work.Key);
        };
        var statistics = new DockPanel { Margin = new Thickness(12) }; statsTab.Content = statistics;
        var statsHeader = new StackPanel(); DockPanel.SetDock(statsHeader, Dock.Top); statistics.Children.Add(statsHeader);
        Text(statsHeader, "Counts use the chosen attribution scope. All-reference counts include unreviewed/editorial/further-reading material and are not proof of original-author influence. Single-source evidence is statistical only; graph exploration requires a shared work.");
        statsHeader.Children.Add(Ui.Text("Filter repeated works (title / author)", 12, "MutedBrush")); statsHeader.Children.Add(statsSearch);
        System.Windows.Automation.AutomationProperties.SetName(statsSearch, "Filter repeated works (title / author)");
        statsHeader.Children.Add(includeSingle); statsHeader.Children.Add(statsCount);
        table.Columns.Add(new DataGridTextColumn { Header = "Work", Binding = new Binding(nameof(CitedWorkStatistics.Title)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Author", Binding = new Binding(nameof(CitedWorkStatistics.Author)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Citing books", Binding = new Binding(nameof(CitedWorkStatistics.CitingBooks)) });
        table.Columns.Add(new DataGridTextColumn { Header = "Evidence passages", Binding = new Binding(nameof(CitedWorkStatistics.EvidencePassages)) });
        table.Columns.Add(new DataGridTextColumn { Header = "Identity", Binding = new Binding(nameof(CitedWorkStatistics.Identity)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var explore = Ui.Button("Explore selected shared work", () => { if (table.SelectedItem is CitedWorkStatistics row && row.CitingBooks > 1) { tabs.SelectedIndex = 0; Focus(row.Work.Key); } });
        explore.IsEnabled = false;
        table.SelectionChanged += (_, _) => explore.IsEnabled = table.SelectedItem is CitedWorkStatistics row && row.CitingBooks > 1;
        DockPanel.SetDock(explore, Dock.Bottom); statistics.Children.Add(explore); statistics.Children.Add(table);
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is CitedWorkStatistics row && row.CitingBooks > 1) { tabs.SelectedIndex = 0; Focus(row.Work.Key); } };
        void Stats()
        {
            if (corpus == null) return;
            var all = corpus.Statistics(); var shown = all.Where(row => (row.CitingBooks > 1 || includeSingle.IsChecked == true && row.EvidencePassages > 1) &&
                row.Work.Label.Contains(statsSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            table.ItemsSource = shown; statsCount.Text = $"{shown.Length} repeated works shown · {all.Count(row => row.CitingBooks > 1)} cited by multiple books across the entire set";
        }
        statsSearch.TextChanged += (_, _) => Stats(); includeSingle.Click += (_, _) => Stats();
        toolbar.Children.Add(Ui.Button("Focus selected", () => Focus(selected)));
        toolbar.Children.Add(Ui.Button("Expand selected", Expand));
        toolbar.Children.Add(Ui.Button("Trace from focus", Trace)); toolbar.Children.Add(Ui.Button("Reset", () => Focus(focus)));
        toolbar.Children.Add(Ui.Button("Fit graph", () => Draw(true))); toolbar.Children.Add(zoom);
        async Task LoadGraph()
        {
            var onlyOriginal = originalOnly.IsChecked == true; originalOnly.IsEnabled = choices.IsEnabled = toolbar.IsEnabled = false;
            try
            {
                corpus = await Task.Run(() => store.ReadBibliographyGraph(onlyOriginal));
                graph = corpus.SharedNetwork();
                summary.Text = $"Scope: {(onlyOriginal ? "reviewed original text" : "all reference material")} · {corpus.SourceBooks} bibliography sources · {graph.Statistics().Count(row => row.CitingBooks > 1)} shared cited works · {graph.Nodes.Count} works including connecting sources · {graph.Connections.Length} connections.";
                Filter(); Stats();
                var initial = focus != null && graph.Nodes.ContainsKey(focus) ? focus : graph.Statistics().FirstOrDefault()?.Work.Key ?? graph.Nodes.Keys.FirstOrDefault();
                if (initial != null) Focus(initial);
                else { focus = selected = null; visible.Clear(); detail.Children.Clear(); network.Draw(graph, visible, "", null, []); graphStatus.Text = "No shared works in this attribution scope. Review evidence or choose All reference material. Single-source references remain in statistics."; }
            }
            catch (Exception ex) { summary.Text = "Could not load bibliography connections: " + ex.Message; }
            finally { originalOnly.IsEnabled = choices.IsEnabled = toolbar.IsEnabled = true; }
        }
        Loaded += async (_, _) => await LoadGraph();
        originalOnly.Click += async (_, _) => await LoadGraph();
    }
}
