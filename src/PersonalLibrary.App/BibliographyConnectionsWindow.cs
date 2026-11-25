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

    public BibliographyConnectionsWindow(LibraryStore store, Action<Book, EpubCitation?> openSource)
    {
        Title = "Bibliography connections & statistics"; Width = 1480; Height = 900; MinWidth = 1050; MinHeight = 650;
        Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Ui.Heading("Bibliography connections", 27));
        var note = Ui.Text("Arrows point from the citing book to the cited work. Work identities are inferred from saved citations; inspect the evidence. Light green nodes are in My Books; gray nodes are outside it or ambiguous.", 13, "MutedBrush");
        note.TextWrapping = TextWrapping.Wrap; header.Children.Add(note);
        var summary = Ui.Text("Loading saved bibliographies…", 13, "MutedBrush"); header.Children.Add(summary);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var graphTab = new TabItem { Header = "Explore connections" }; var statsTab = new TabItem { Header = "Repeatedly cited works" }; tabs.Items.Add(graphTab); tabs.Items.Add(statsTab);
        var graphGrid = new Grid { Margin = new Thickness(8) }; graphGrid.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); graphGrid.ColumnDefinitions.Add(new()); graphGrid.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); graphTab.Content = graphGrid;
        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 10, 0) }; graphGrid.Children.Add(searchPanel);
        var searchHeader = new StackPanel(); DockPanel.SetDock(searchHeader, Dock.Top); searchPanel.Children.Add(searchHeader);
        var search = Ui.Input(searchHeader, "Find a work (title / author)");
        var searchCount = Ui.Text("", 12, "MutedBrush"); searchHeader.Children.Add(searchCount);
        var choices = new ListBox { ItemTemplate = Ui.WrappedLabel(nameof(WorkRow.Label)) }; searchPanel.Children.Add(choices);
        var middle = new DockPanel(); Grid.SetColumn(middle, 1); graphGrid.Children.Add(middle);
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); middle.Children.Add(toolbar);
        var depth = new ComboBox { ItemsSource = new[] { 1, 2, 3, 4, 5 }, SelectedIndex = 0, Width = 52, Margin = new Thickness(3, 0, 6, 5) };
        toolbar.Children.Add(Ui.Text("Steps:", 12)); toolbar.Children.Add(depth);
        var viewport = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White };
        var network = new BibliographyNetworkCanvas(); viewport.Content = network; middle.Children.Add(viewport);
        var graphStatus = Ui.Text("Choose a work to explore its connections.", 12, "MutedBrush"); graphStatus.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(graphStatus, Dock.Bottom); middle.Children.Insert(middle.Children.Count - 1, graphStatus);
        var detail = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; var detailScroll = Ui.Scroll(detail); Grid.SetColumn(detailScroll, 2); graphGrid.Children.Add(detailScroll);
        BibliographyGraph? graph = null; string? focus = null; string? selected = null; string[] highlightedPath = []; var visible = new HashSet<string>();
        var zoom = new Slider { Minimum = .35, Maximum = 1.8, Value = 1, Width = 90, Margin = new Thickness(8, 0, 0, 5), ToolTip = "Graph zoom" };
        System.Windows.Automation.AutomationProperties.SetName(zoom, "Graph zoom");
        toolbar.Children.Add(Ui.Button("Focus selected", () => Focus(selected)));
        toolbar.Children.Add(Ui.Button("Expand selected", Expand));
        toolbar.Children.Add(Ui.Button("Trace from focus", Trace)); toolbar.Children.Add(Ui.Button("Reset", () => Focus(focus)));
        toolbar.Children.Add(Ui.Button("Fit graph", () => Draw(true)));
        toolbar.Children.Add(zoom);
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
            if (graph == null || key == null) return;
            focus = selected = key; highlightedPath = [];
            var slice = graph.Neighborhood(key, (int)(depth.SelectedItem ?? 1), 120); visible = slice.Keys.ToHashSet();
            graphStatus.Text = $"Focus: {graph.Nodes[key].Title} · {visible.Count} works shown" + (slice.Omitted > 0 ? $" · {slice.Omitted} more within these steps omitted (120-work initial limit). Explore a narrower focus or expand a selected branch." : ". Select nodes or arrows to inspect evidence; expand several branches together.");
            Draw(true); Details(key);
        }
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
            if (highlightedPath.Length == 0) { graphStatus.Text = "No path exists between these works in the saved citation graph."; return; }
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
            Text(detail, $"Cited by {graph.CitedBy(key).Length} source books · Cites {graph.References(key).Length} works", 14);
            Text(detail, "Cited by", 18);
            foreach (var edge in graph.CitedBy(key).OrderBy(e => graph.Nodes[e.Source].Title))
                detail.Children.Add(Ui.Button(graph.Nodes[edge.Source].Title + $" ({edge.Evidence.Length} passages)", () => Connection(edge)));
            Text(detail, "References", 18);
            foreach (var edge in graph.References(key).OrderBy(e => graph.Nodes[e.Target].Title))
                detail.Children.Add(Ui.Button(graph.Nodes[edge.Target].Title, () => Connection(edge)));
            if (graph.CitedBy(key).Length + graph.References(key).Length == 0) Text(detail, "No identified citation edges. Text-only notes remain in the bibliography.");
            detailScroll.ScrollToTop();
        }
        void Connection(BibliographyConnection edge)
        {
            if (graph == null) return;
            detail.Children.Clear(); Text(detail, graph.Nodes[edge.Source].Title + " → " + graph.Nodes[edge.Target].Title, 19);
            detail.Children.Add(Ui.Button("Select citing work", () => Select(edge.Source)));
            detail.Children.Add(Ui.Button("Select cited work", () => Select(edge.Target)));
            Text(detail, "Saved evidence · inferred work identity", 13);
            foreach (var evidence in edge.Evidence)
            {
                Text(detail, evidence.Citation.Section + " · " + evidence.Citation.ArchiveEntry, 12); Text(detail, evidence.Citation.Text);
                detail.Children.Add(Ui.Button("Open source bibliography", () => openSource(evidence.Source, evidence.Citation)));
            }
            detailScroll.ScrollToTop();
        }
        network.WorkSelected += Select; network.ConnectionSelected += Connection;
        void Filter()
        {
            if (graph == null) return;
            var found = graph.Nodes.Values.Where(n => n.Label.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(n => graph.CitedBy(n.Key).Length).ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            choices.ItemsSource = found.Take(100).Select(n => new WorkRow(n, graph.CitedBy(n.Key).Length, graph.References(n.Key).Length)).ToArray();
            searchCount.Text = found.Length > 100 ? $"First 100 of {found.Length} · refine search" : $"{found.Length} works";
        }
        search.TextChanged += (_, _) => Filter(); choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is WorkRow row) Select(row.Work.Key); };
        var statistics = new DockPanel { Margin = new Thickness(12) }; statsTab.Content = statistics;
        var statsHeader = new StackPanel(); DockPanel.SetDock(statsHeader, Dock.Top); statistics.Children.Add(statsHeader);
        Text(statsHeader, "Citing books counts distinct source books. Evidence passages counts distinct citation text within each source; duplicate copies across formats count once. These are counts within your saved bibliography set.");
        var statsSearch = Ui.Input(statsHeader, "Filter repeated works (title / author)");
        var includeSingle = new CheckBox { Content = "Also show repeated evidence within a single source", Margin = new Thickness(0, 0, 0, 10) }; statsHeader.Children.Add(includeSingle);
        var statsCount = Ui.Text("", 13, "MutedBrush"); statsHeader.Children.Add(statsCount);
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true, CanUserAddRows = false };
        table.Columns.Add(new DataGridTextColumn { Header = "Work", Binding = new Binding(nameof(CitedWorkStatistics.Title)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Author", Binding = new Binding(nameof(CitedWorkStatistics.Author)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Citing books", Binding = new Binding(nameof(CitedWorkStatistics.CitingBooks)) });
        table.Columns.Add(new DataGridTextColumn { Header = "Evidence passages", Binding = new Binding(nameof(CitedWorkStatistics.EvidencePassages)) });
        table.Columns.Add(new DataGridTextColumn { Header = "Identity", Binding = new Binding(nameof(CitedWorkStatistics.Identity)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var explore = Ui.Button("Explore selected work", () => { if (table.SelectedItem is CitedWorkStatistics row) { tabs.SelectedIndex = 0; Focus(row.Work.Key); } });
        DockPanel.SetDock(explore, Dock.Bottom); statistics.Children.Add(explore); statistics.Children.Add(table);
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is CitedWorkStatistics row) { tabs.SelectedIndex = 0; Focus(row.Work.Key); } };
        void Stats()
        {
            if (graph == null) return;
            var all = graph.Statistics(); var shown = all.Where(row => (row.CitingBooks > 1 || includeSingle.IsChecked == true && row.EvidencePassages > 1) &&
                row.Work.Label.Contains(statsSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            table.ItemsSource = shown; statsCount.Text = $"{shown.Length} repeated works shown · {all.Count(row => row.CitingBooks > 1)} cited by multiple books across the entire set";
        }
        statsSearch.TextChanged += (_, _) => Stats(); includeSingle.Click += (_, _) => Stats();
        Loaded += async (_, _) =>
        {
            try
            {
                graph = await Task.Run(store.ReadBibliographyGraph);
                summary.Text = $"{graph.SourceBooks} source books · {graph.Nodes.Count} identified works · {graph.Connections.Length} citation connections · {graph.Statistics().Count(row => row.CitingBooks > 1)} works cited by multiple books. Text-only notes do not create graph edges.";
                Filter(); Stats();
                var initial = graph.Statistics().FirstOrDefault()?.Work.Key ?? graph.Nodes.Keys.FirstOrDefault();
                if (initial != null) Focus(initial); else graphStatus.Text = "No identified citations yet. Import or add bibliographies first.";
            }
            catch (Exception ex) { summary.Text = "Could not load bibliography connections: " + ex.Message; }
        };
    }
}
