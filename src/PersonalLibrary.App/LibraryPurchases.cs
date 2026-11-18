using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class LibraryPurchases : StackPanel
{
    private static readonly Color[] Colors =
    [
        Color.FromRgb(30, 120, 107), Color.FromRgb(172, 108, 45), Color.FromRgb(78, 105, 148),
        Color.FromRgb(153, 77, 106), Color.FromRgb(110, 123, 48), Color.FromRgb(105, 85, 145),
        Color.FromRgb(166, 73, 55), Color.FromRgb(60, 122, 146), Color.FromRgb(134, 103, 78),
        Color.FromRgb(96, 116, 102), Color.FromRgb(139, 125, 159), Color.FromRgb(185, 149, 69)
    ];

    public LibraryPurchases(LibraryStore store, IReadOnlyList<Book> books)
    {
        var rates = BankOfCanadaLookup.Cached(store);
        var statistics = new PurchaseStatistics(books, rates);
        Children.Add(Ui.Heading("Library purchases", 24));
        var lifetime = new TabItem { Header = "Lifetime" };
        var current = new TabItem { Header = "Current library" };
        var scopes = new TabControl();
        scopes.Items.Add(lifetime);
        scopes.Items.Add(current);
        RenderScopes();

        if (statistics.HasForeignAmounts)
        {
            var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var refresh = new Button { Content = "Refresh exchange rates" };
            var status = Ui.Text(RateCaption(), 13, "MutedBrush");
            status.Margin = new Thickness(6, 9, 10, 8);
            status.TextWrapping = TextWrapping.Wrap;
            tools.Children.Add(refresh);
            tools.Children.Add(status);
            Children.Add(tools);
            CancellationTokenSource? pending = null;
            refresh.Click += async (_, _) => await Fetch();
            Loaded += async (_, _) => { if (rates == null) await Fetch(); };
            Unloaded += (_, _) => pending?.Cancel();

            string RateCaption() => rates == null
                ? "Exchange rates are needed to include foreign amounts."
                : $"Converted at Bank of Canada rates for {rates.Date:yyyy-MM-dd}. Foreign amounts are estimated in CAD.";

            async Task Fetch()
            {
                if (pending != null) return;
                using var request = new CancellationTokenSource();
                pending = request;
                refresh.IsEnabled = false;
                status.Text = "Checking Bank of Canada exchange rates…";
                try
                {
                    var fetched = await new BankOfCanadaLookup().FetchAsync(request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    BankOfCanadaLookup.Save(store, fetched);
                    rates = fetched;
                    statistics = new PurchaseStatistics(books, rates);
                    RenderScopes();
                    status.Text = RateCaption();
                }
                catch (OperationCanceledException) when (request.IsCancellationRequested) { }
                catch (Exception)
                {
                    status.Text = "Could not refresh exchange rates. " + RateCaption();
                }
                finally
                {
                    pending = null;
                    refresh.IsEnabled = true;
                }
            }
        }
        Children.Add(scopes);

        void RenderScopes()
        {
            lifetime.Content = Scope(statistics, current: false);
            current.Content = Scope(new PurchaseStatistics(books, rates, currentOnly: true), current: true);
        }
    }

    private static StackPanel Scope(PurchaseStatistics statistics, bool current)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 10, 4, 0) };
        panel.Children.Add(Ui.Text(current ? "Copies and editions you currently own." : "Includes sold copies and other recorded purchases.", 13, "MutedBrush"));
        var totals = new Grid();
        totals.ColumnDefinitions.Add(new ColumnDefinition());
        totals.ColumnDefinitions.Add(new ColumnDefinition());
        var retail = new StackPanel();
        var spending = new StackPanel();
        RenderTotal(retail, "Library MSRP", "Average MSRP per book", statistics.RetailValue, statistics.EstimatedRetailItems);
        RenderTotal(spending, current ? "Spent on current library" : "Total spent", "Average spent per book", statistics.Spending);
        totals.Children.Add(Ui.Card(retail));
        var spendingCard = Ui.Card(spending);
        Grid.SetColumn(spendingCard, 1);
        totals.Children.Add(spendingCard);
        panel.Children.Add(totals);
        if (!current && statistics.SaleProceeds is { } sales && sales.Included + sales.Missing + sales.AwaitingConversion > 0)
        {
            var proceeds = new StackPanel();
            RenderTotal(proceeds, "Sale proceeds", null, sales);
            panel.Children.Add(Ui.Card(proceeds));
        }
        var charts = new Grid();
        charts.ColumnDefinitions.Add(new ColumnDefinition());
        charts.ColumnDefinitions.Add(new ColumnDefinition());
        charts.Children.Add(PieCard("Bought new vs. used", statistics.Conditions, statistics.UnknownCondition, "condition"));
        var vendors = PieCard("Purchased from", statistics.Vendors, statistics.UnknownVendor, "vendor");
        Grid.SetColumn(vendors, 1);
        charts.Children.Add(vendors);
        panel.Children.Add(charts);
        return panel;
    }

    private static void RenderTotal(Panel panel, string title, string? averageLabel, PurchaseTotal total, int estimated = 0)
    {
        panel.Children.Clear();
        panel.Children.Add(Ui.Heading(title, 20));
        var value = total.Included == 0 ? "No total yet" : $"CAD ${total.Amount:N2}";
        panel.Children.Add(Ui.Heading(value, 28));
        var average = total.Included == 0 ? "—" : $"CAD ${total.Amount / total.Included:N2}";
        if (averageLabel != null) panel.Children.Add(Ui.Text($"{averageLabel} · {average}", 15));
        var coverage = $"{total.Included:N0} included · {total.Missing:N0} missing amount";
        if (total.AwaitingConversion > 0) coverage += $" · {total.AwaitingConversion:N0} awaiting conversion";
        if (estimated > 0) coverage += $" · {estimated:N0} estimated MSRP";
        var detail = Ui.Text(coverage, 13, "MutedBrush");
        detail.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(detail);
    }

    private static Border PieCard(string title, IReadOnlyList<PurchaseCount> counts, int missing, string field)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Heading(title, 20));
        var total = counts.Sum(item => item.Count);
        var coverage = Ui.Text($"By copy or edition count · {total:N0} recorded · {missing:N0} without {field}", 13, "MutedBrush");
        coverage.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(coverage);
        if (total == 0)
        {
            panel.Children.Add(Ui.Text($"No purchase {field} recorded yet.", 14, "MutedBrush"));
            return Ui.Card(panel);
        }

        const double size = 148;
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pie = new Canvas { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 15, 0) };
        AutomationProperties.SetName(pie, title);
        DockPanel.SetDock(pie, Dock.Left);
        row.Children.Add(pie);
        var legend = new StackPanel();
        var start = -Math.PI / 2;
        for (var index = 0; index < counts.Count; index++)
        {
            var item = counts[index];
            var share = (double)item.Count / total;
            var brush = new SolidColorBrush(Colors[index % Colors.Length]);
            var detail = $"{item.Label} · {item.Count:N0} · {share:P1}";
            Shape slice;
            if (counts.Count == 1) slice = new Ellipse { Width = size, Height = size };
            else
            {
                var end = start + Math.Tau * share;
                var center = new Point(size / 2, size / 2);
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(center, isFilled: true, isClosed: true);
                    context.LineTo(PointAt(start), isStroked: false, isSmoothJoin: false);
                    context.ArcTo(PointAt(end), new Size(center.X, center.Y), 0, share > .5, SweepDirection.Clockwise, true, false);
                }
                geometry.Freeze();
                slice = new System.Windows.Shapes.Path { Data = geometry };
                start = end;

                Point PointAt(double angle) => new(center.X * (1 + Math.Cos(angle)), center.Y * (1 + Math.Sin(angle)));
            }
            slice.Fill = brush;
            slice.ToolTip = detail;
            pie.Children.Add(slice);
            var legendRow = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var swatch = new Border { Background = brush, Width = 11, Height = 11,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 7, 0) };
            DockPanel.SetDock(swatch, Dock.Left);
            legendRow.Children.Add(swatch);
            var label = Ui.Text(detail, 13);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0);
            legendRow.Children.Add(label);
            legend.Children.Add(legendRow);
        }
        var scroll = Ui.Scroll(legend);
        scroll.MaxHeight = 175;
        row.Children.Add(scroll);
        panel.Children.Add(row);
        return Ui.Card(panel);
    }
}
