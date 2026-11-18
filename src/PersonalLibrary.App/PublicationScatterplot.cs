using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal sealed class PublicationScatterplot : Canvas
{
    private const double DotDiameter = 12;
    private static readonly ControlTemplate DotTemplate = CreateDotTemplate();
    private readonly IReadOnlyList<PublicationPoint> points;
    private readonly Action<IReadOnlyList<Completion>> open;
    private readonly PublicationYearRange fullRange;
    private readonly DispatcherTimer singleClick = new();
    private readonly int firstYear;
    private readonly int lastYear;
    private readonly bool canExpand;
    private PublicationYearRange visibleRange;
    private Rect plotBounds = Rect.Empty;
    private Action? pendingClick;

    public PublicationScatterplot(IReadOnlyList<PublicationPoint> points, Action<IReadOnlyList<Completion>> open,
        Action? showFullScreen = null)
    {
        this.points = points;
        this.open = open;
        canExpand = showFullScreen != null;
        firstYear = points.Min(point => point.Finished.Year);
        lastYear = points.Max(point => point.Finished.Year);
        fullRange = PublicationYearRange.ForData(points.Min(point => point.OriginalYear), points.Max(point => point.OriginalYear));
        visibleRange = fullRange;
        Background = Brushes.Transparent;
        AutomationProperties.SetName(this, "Publication year versus completion date");
        SizeChanged += (_, _) => Draw();
        if (showFullScreen == null)
        {
            PreviewMouseWheel += (_, args) =>
            {
                var position = args.GetPosition(this);
                if (!plotBounds.Contains(position) || args.Delta == 0) return;
                args.Handled = true;
                var zoomed = visibleRange.Zoom(fullRange, (plotBounds.Bottom - position.Y) / plotBounds.Height, args.Delta / 120.0);
                if (zoomed == visibleRange) return;
                visibleRange = zoomed;
                Draw();
            };
            return;
        }

        Height = 420;
        singleClick.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
        singleClick.Tick += (_, _) =>
        {
            var action = pendingClick;
            CancelPendingClick();
            if (action != null) Ui.Guard(action);
        };
        Unloaded += (_, _) => CancelPendingClick();
        PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount != 2) return;
            CancelPendingClick();
            args.Handled = true;
            Dispatcher.BeginInvoke(() => Ui.Guard(showFullScreen));
        };
    }

    public void ResetZoom()
    {
        visibleRange = fullRange;
        Draw();
    }

    private void CancelPendingClick()
    {
        singleClick.Stop();
        pendingClick = null;
    }

    private void Draw()
    {
        Children.Clear();
        const double left = 92;
        const double top = 32;
        var right = ActualWidth - 30;
        var bottom = ActualHeight - 54;
        plotBounds = right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Empty;
        if (plotBounds.IsEmpty) return;

        var firstDay = new DateOnly(firstYear, 1, 1).DayNumber;
        var lastDay = new DateOnly(lastYear, 12, 31).DayNumber;
        var yearStep = PublicationYearRange.TickStep(visibleRange.Span / Math.Max(2, (bottom - top) / 27));

        double X(DateOnly date) => left + (right - left) * (date.DayNumber - firstDay) / (lastDay - firstDay);
        double Y(long year) => bottom - (bottom - top) * (year - visibleRange.First) / visibleRange.Span;

        Label("Original publication year", left, 0);
        for (var year = (long)Math.Ceiling(visibleRange.First / yearStep) * yearStep; year <= visibleRange.Last; year += yearStep)
        {
            var y = Y(year);
            Rule(left, y, right, y, year == 0 ? "MutedBrush" : "LineBrush");
            Label(year == 0 ? "BCE / CE" : YearLabel(year), left - 9, y - 8, alignRight: true);
        }

        if (firstYear == lastYear)
        {
            foreach (var month in new[] { 1, 4, 7, 10 })
            {
                var date = new DateOnly(firstYear, month, 1);
                var x = X(date);
                Rule(x, top, x, bottom);
                Label(date.ToString("MMM"), x, bottom + 8, centered: true);
            }
            Label($"Completion date · {firstYear}", (left + right) / 2, bottom + 29, centered: true);
        }
        else
        {
            var yearWidth = (right - left) / (lastYear - firstYear + 1);
            var dateStep = (int)Math.Min(lastYear - firstYear + 1, PublicationYearRange.TickStep(65 / yearWidth));
            for (var year = firstYear; year <= lastYear; year += dateStep)
            {
                var x = X(new DateOnly(year, 1, 1));
                Rule(x, top, x, bottom);
                Label(year.ToString(), x, bottom + 8, centered: true);
            }
            if (dateStep == 1 && yearWidth >= 75)
                for (var year = firstYear; year <= lastYear; year++)
                {
                    var x = X(new DateOnly(year, 7, 1));
                    Rule(x, top, x, bottom, opacity: .6);
                }
            Label("Completion date", (left + right) / 2, bottom + 29, centered: true);
        }
        Rule(left, top, left, bottom, "MutedBrush");
        Rule(left, bottom, right, bottom, "MutedBrush");

        foreach (var point in points)
        {
            if (point.OriginalYear < visibleRange.First || point.OriginalYear > visibleRange.Last) continue;
            var label = point.Readings.Count == 1 ? point.Readings[0].Book.Title : "View readings at this point";
            var description = $"{label} · completed {point.Finished:D} · originally published {YearLabel(point.OriginalYear)}";
            var marker = new DotButton(fromMouse =>
            {
                if (!fromMouse || !canExpand)
                {
                    Ui.Guard(() => open(point.Readings));
                    return;
                }
                pendingClick = () => open(point.Readings);
                singleClick.Stop();
                singleClick.Start();
            });
            marker.Template = DotTemplate;
            marker.Height = DotDiameter;
            marker.Width = DotDiameter;
            marker.Margin = new Thickness(0);
            marker.Content = null;
            marker.ToolTip = description;
            AutomationProperties.SetName(marker, description);
            SetLeft(marker, X(point.Finished) - DotDiameter / 2);
            SetTop(marker, Y(point.OriginalYear) - DotDiameter / 2);
            marker.GotKeyboardFocus += (_, _) => SetZIndex(marker, 2);
            marker.LostKeyboardFocus += (_, _) => SetZIndex(marker, 0);
            Children.Add(marker);
        }
    }

    private static ControlTemplate CreateDotTemplate()
    {
        var dot = new FrameworkElementFactory(typeof(Ellipse), "Dot");
        dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        var template = new ControlTemplate(typeof(Button)) { VisualTree = dot };
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension("TextBrush"), "Dot"));
        focused.Setters.Add(new Setter(Shape.StrokeThicknessProperty, 2.0, "Dot"));
        template.Triggers.Add(focused);
        return template;
    }

    private void Rule(double x1, double y1, double x2, double y2, string brush = "LineBrush", double opacity = 1)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1, IsHitTestVisible = false, Opacity = opacity };
        line.SetResourceReference(Shape.StrokeProperty, brush);
        Children.Add(line);
    }

    private void Label(string value, double x, double y, bool centered = false, bool alignRight = false)
    {
        var label = Ui.Text(value, 12, "MutedBrush");
        label.Margin = new Thickness(0);
        label.TextWrapping = TextWrapping.NoWrap;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (centered) x -= label.DesiredSize.Width / 2;
        else if (alignRight) x -= label.DesiredSize.Width;
        SetLeft(label, x);
        SetTop(label, y);
        Children.Add(label);
    }

    private sealed class DotButton(Action<bool> activate) : Button
    {
        private bool releasingMouse;

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
        {
            releasingMouse = true;
            try { base.OnMouseLeftButtonUp(args); }
            finally { releasingMouse = false; }
        }

        protected override void OnClick()
        {
            base.OnClick();
            activate(releasingMouse);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    internal static string YearLabel(long year) => year < 0 ? $"{-year} BCE" : $"{year} CE";
}
