using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal sealed class BibliographyNetworkCanvas : Canvas
{
    public event Action<string>? WorkSelected;
    public event Action<BibliographyConnection>? ConnectionSelected;
    public Dictionary<string, Point> Positions { get; } = new();

    public void Draw(BibliographyGraph graph, HashSet<string> visible, string focus, string? selected, string[] path)
    {
        Children.Clear(); Positions.Clear();
        var nodes = visible.Where(graph.Nodes.ContainsKey).OrderBy(key => graph.Nodes[key].Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Width = Math.Max(1050, Math.Sqrt(nodes.Length) * 290); Height = Math.Max(700, Math.Sqrt(nodes.Length) * 200);
        if (nodes.Length == 0) return;
        var points = new Point[nodes.Length]; var movement = new Vector[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            var angle = i * Math.PI * 2 / nodes.Length;
            points[i] = nodes[i] == focus ? new(Width / 2, Height / 2) : new(Width / 2 + Math.Cos(angle) * Width * .35, Height / 2 + Math.Sin(angle) * Height * .35);
        }
        var indexes = nodes.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index);
        var edges = graph.Connections.Where(edge => visible.Contains(edge.Source) && visible.Contains(edge.Target)).ToArray();
        for (var iteration = 0; iteration < 180; iteration++)
        {
            Array.Clear(movement);
            for (var i = 0; i < points.Length; i++)
            for (var j = i + 1; j < points.Length; j++)
            {
                var delta = points[i] - points[j]; if (delta.Length < .1) delta = new Vector(1, 1);
                var distance = Math.Max(30, delta.Length); delta.Normalize();
                var force = delta * (38000 / (distance * distance)); movement[i] += force; movement[j] -= force;
                if (Math.Abs(points[i].X - points[j].X) < 205 && Math.Abs(points[i].Y - points[j].Y) < 95)
                { movement[i] += delta * 12; movement[j] -= delta * 12; }
            }
            foreach (var edge in edges)
            {
                var i = indexes[edge.Source]; var j = indexes[edge.Target]; var delta = points[j] - points[i];
                var distance = delta.Length; if (distance < .1) continue; delta.Normalize();
                var force = delta * ((distance - 285) * .018); movement[i] += force; movement[j] -= force;
            }
            for (var i = 0; i < points.Length; i++)
            {
                var center = new Point(Width / 2, Height / 2) - points[i];
                movement[i] += center * (nodes[i] == focus ? .045 : .001);
                var step = movement[i]; if (step.Length > 18) { step.Normalize(); step *= 18; }
                points[i] = new(Math.Clamp(points[i].X + step.X, 110, Width - 110), Math.Clamp(points[i].Y + step.Y, 60, Height - 60));
            }
        }
        for (var i = 0; i < nodes.Length; i++) Positions[nodes[i]] = points[i];
        var pathEdges = path.Zip(path.Skip(1), (a, b) => (a, b)).ToArray();
        foreach (var edge in edges)
        {
            var highlighted = pathEdges.Any(pair => pair.a == edge.Source && pair.b == edge.Target || pair.a == edge.Target && pair.b == edge.Source);
            var incident = edge.Source == selected || edge.Target == selected;
            var brush = highlighted ? Brushes.DarkOrange : incident ? Brushes.Teal : Brushes.SlateGray;
            var from = Positions[edge.Source]; var to = Positions[edge.Target]; var vector = to - from; if (vector.Length < 1) continue;
            var route = Route(edge.Source, edge.Target);
            var start = route.Start; var end = route.End; vector = end - route.Control; vector.Normalize(); var side = new Vector(-vector.Y, vector.X);
            var geometry = new PathGeometry([new PathFigure(start, [new QuadraticBezierSegment(route.Control, end, true)], false)]);
            var line = new System.Windows.Shapes.Path { Data = geometry, Stroke = brush, StrokeThickness = highlighted ? 4 : incident ? 2.7 : 1.5,
                ToolTip = $"{graph.Nodes[edge.Source].Title} cites {graph.Nodes[edge.Target].Title}\n{edge.Evidence.Length} evidence passage(s) · click to inspect" };
            line.MouseLeftButtonDown += (_, args) => { ConnectionSelected?.Invoke(edge); args.Handled = true; }; Children.Add(line);
            var arrow = new Polygon { Fill = brush, Points = new PointCollection([end, end - vector * 13 + side * 5, end - vector * 13 - side * 5]), IsHitTestVisible = false };
            Children.Add(arrow);
        }
        foreach (var key in nodes)
        {
            var work = graph.Nodes[key];
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = work.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, FontSize = 12, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock { Text = $"{graph.CitedBy(key).Length} citing books · {graph.References(key).Length} references", FontSize = 10, Margin = new Thickness(0, 3, 0, 0) });
            var button = new Button { Content = label, Width = 190, Height = 76, Padding = new Thickness(8), Margin = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = work.BookId == null ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(228, 243, 240)), Foreground = Brushes.Black,
                BorderBrush = key == selected || path.Contains(key) ? Brushes.DarkOrange : key == focus ? Brushes.Teal : Brushes.SlateGray,
                BorderThickness = new Thickness(key == selected || key == focus || path.Contains(key) ? 3 : 1), ToolTip = work.Label + "\n" + work.Identity };
            System.Windows.Automation.AutomationProperties.SetName(button, work.Label);
            button.Click += (_, _) => WorkSelected?.Invoke(key);
            SetLeft(button, Positions[key].X - 95); SetTop(button, Positions[key].Y - 38); Children.Add(button);
        }
    }

    private (Point Start, Point Control, Point End) Route(string source, string target)
    {
        var from = Positions[source]; var to = Positions[target]; var direction = to - from; direction.Normalize();
        var side = new Vector(-direction.Y, direction.X); var middle = from + (to - from) * .5;
        (Point Start, Point Control, Point End) best = (Boundary(from, to - from), middle, Boundary(to, from - to));
        var fewest = int.MaxValue;
        // Keep arrow strokes away from unrelated node cards so a hidden section
        // cannot look like a citation originating from the intervening work.
        foreach (var offset in new[] { 0, 90, -90, 180, -180, 300, -300, 450, -450 })
        {
            var control = middle + side * offset;
            var start = Boundary(from, control - from); var end = Boundary(to, control - to);
            var collisions = 0;
            foreach (var other in Positions.Where(pair => pair.Key != source && pair.Key != target))
            {
                var bounds = new Rect(other.Value.X - 105, other.Value.Y - 48, 210, 96);
                for (var i = 1; i < 40; i++)
                {
                    var t = i / 40d; var u = 1 - t;
                    var point = new Point(u * u * start.X + 2 * u * t * control.X + t * t * end.X,
                        u * u * start.Y + 2 * u * t * control.Y + t * t * end.Y);
                    if (bounds.Contains(point)) { collisions++; break; }
                }
            }
            if (collisions < fewest) { best = (start, control, end); fewest = collisions; }
            if (collisions == 0) break;
        }
        return best;
    }

    private static Point Boundary(Point center, Vector direction)
    {
        var factor = Math.Min(Math.Abs(direction.X) < .01 ? double.PositiveInfinity : 101 / Math.Abs(direction.X),
            Math.Abs(direction.Y) < .01 ? double.PositiveInfinity : 44 / Math.Abs(direction.Y));
        return center + direction * factor;
    }
}
