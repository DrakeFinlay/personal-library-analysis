using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal sealed record SelectedPhotoPage(string Path, PhotoArea Area, bool ImproveContrast, int AreaNumber, int QuarterTurns = 0);

internal sealed class PhotoPagesWindow : Window
{
    private sealed class Page(string path)
    {
        public string Path { get; } = path;
        public PhotoArea Area { get; set; } = PhotoArea.Whole;
        public bool Reviewed { get; set; }
        public bool Skip { get; set; }
        public bool Contrast { get; set; } = true;
        public override string ToString() => (Skip ? "Skipped · " : Reviewed ? "Ready · " : "Select area · ") + System.IO.Path.GetFileName(Path);
    }
    public SelectedPhotoPage[] Pages { get; private set; } = [];

    public PhotoPagesWindow(string[] paths)
    {
        Title = "Select bibliography / notes pages";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 1150; Height = 850; MinWidth = 780; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var pages = paths.Select(path => new Page(path)).ToList();
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Ui.Heading("Select the text to scan", 25));
        var instructions = Ui.Text("Drag the four blue corners around ONE page or text block, in clockwise order. Exclude facing-page fragments, fingers and background. The selected area will be straightened before OCR. Add another area for a second page in the same photo.");
        instructions.TextWrapping = TextWrapping.Wrap; header.Children.Add(instructions);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var status = Ui.Text("", 13, "MutedBrush"); status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        var controls = new WrapPanel(); footer.Children.Add(controls);
        var confirm = Ui.Button("Scan selected pages", () =>
        {
            if (pages.Any(p => !p.Reviewed && !p.Skip)) throw new InvalidOperationException("Accept or skip each photo before scanning.");
            Pages = pages.Where(p => !p.Skip).Select((p, i) => new SelectedPhotoPage(p.Path, p.Area, p.Contrast, i + 1)).ToArray();
            if (Pages.Length == 0) throw new InvalidOperationException("Select at least one page to scan.");
            DialogResult = true;
        }, true);
        controls.Children.Add(confirm); controls.Children.Add(Ui.Button("Cancel", Close));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(220) }); columns.ColumnDefinitions.Add(new()); root.Children.Add(columns);
        var list = new ListBox { Margin = new Thickness(0, 0, 14, 0) }; columns.Children.Add(list);
        var right = new DockPanel(); Grid.SetColumn(right, 1); columns.Children.Add(right);
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); right.Children.Add(toolbar);
        var contrast = new CheckBox { Content = "Reduce shadows", IsChecked = true, Margin = new Thickness(5, 10, 10, 5) };
        toolbar.Children.Add(contrast);
        var canvas = new Canvas { Background = Brushes.DimGray, ClipToBounds = true };
        right.Children.Add(canvas);
        var photo = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false }; canvas.Children.Add(photo);
        var polygon = new Polygon { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(25, 0, 160, 255)), IsHitTestVisible = false };
        canvas.Children.Add(polygon);
        var handles = new List<Ellipse>();
        var points = PhotoArea.Whole.Corners;
        BitmapSource? bitmap = null; var left = 0d; var top = 0d; var width = 1d; var height = 1d; int? dragging = null; var switching = false;
        Page? Current() => list.SelectedItem as Page;
        void Draw()
        {
            if (bitmap == null || canvas.ActualWidth < 1 || canvas.ActualHeight < 1) return;
            var scale = Math.Min(canvas.ActualWidth / bitmap.PixelWidth, canvas.ActualHeight / bitmap.PixelHeight);
            width = bitmap.PixelWidth * scale; height = bitmap.PixelHeight * scale; left = (canvas.ActualWidth - width) / 2; top = (canvas.ActualHeight - height) / 2;
            photo.Width = width; photo.Height = height; Canvas.SetLeft(photo, left); Canvas.SetTop(photo, top);
            var rendered = points.Select(p => new Point(left + p.X * width, top + p.Y * height)).ToArray();
            polygon.Points = new PointCollection(rendered);
            for (var i = 0; i < 4; i++) { Canvas.SetLeft(handles[i], rendered[i].X - 10); Canvas.SetTop(handles[i], rendered[i].Y - 10); }
        }
        void Changed()
        {
            if (Current() is not { } page) return;
            page.Area = new(points[0], points[1], points[2], points[3]); page.Reviewed = false; page.Skip = false;
            Draw(); Update();
        }
        void Update()
        {
            status.Text = $"{pages.Count(p => p.Reviewed && !p.Skip)} ready · {pages.Count(p => p.Skip)} skipped · {pages.Count(p => !p.Reviewed && !p.Skip)} need review. Keep the entire target text inside the corners.";
            confirm.IsEnabled = pages.All(p => p.Reviewed || p.Skip) && pages.Any(p => p.Reviewed && !p.Skip);
            list.Items.Refresh();
        }
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            var handle = new Ellipse { Width = 20, Height = 20, Fill = Brushes.DeepSkyBlue, Stroke = Brushes.White, StrokeThickness = 2, Cursor = Cursors.SizeAll,
                ToolTip = new[] { "Top left", "Top right", "Bottom right", "Bottom left" }[i] };
            System.Windows.Automation.AutomationProperties.SetName(handle, (string)handle.ToolTip);
            handle.MouseLeftButtonDown += (_, args) => { dragging = index; canvas.CaptureMouse(); args.Handled = true; };
            handles.Add(handle); canvas.Children.Add(handle);
        }
        canvas.MouseMove += (_, args) =>
        {
            if (dragging is not { } index) return;
            var position = args.GetPosition(canvas);
            points[index] = new(Math.Clamp((position.X - left) / width, 0, 1), Math.Clamp((position.Y - top) / height, 0, 1)); Changed();
        };
        canvas.MouseLeftButtonUp += (_, _) => { dragging = null; canvas.ReleaseMouseCapture(); };
        canvas.SizeChanged += (_, _) => Draw();
        toolbar.Children.Add(Ui.Button("Whole photo", () => { points = PhotoArea.Whole.Corners; Changed(); }));
        toolbar.Children.Add(Ui.Button("Left page", () => { points = new PhotoArea(new(.03, .03), new(.52, .03), new(.52, .97), new(.03, .97)).Corners; Changed(); }));
        toolbar.Children.Add(Ui.Button("Right page", () => { points = new PhotoArea(new(.48, .03), new(.97, .03), new(.97, .97), new(.48, .97)).Corners; Changed(); }));
        toolbar.Children.Add(Ui.Button("Preview straightened page", () =>
        {
            if (Current() is not { } page || bitmap == null) return;
            var flattened = PhotoPage.Rectify(PhotoPageImage.Gray(bitmap), page.Area, page.Contrast);
            var preview = BitmapSource.Create(flattened.Width, flattened.Height, 96, 96, PixelFormats.Gray8, null, flattened.Pixels, flattened.Width);
            new Window { Title = "Straightened page preview", Owner = this, Width = 750, Height = 850, Content = new Image { Source = preview, Stretch = Stretch.Uniform }, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
        }));
        toolbar.Children.Add(Ui.Button("Accept page & next", () =>
        {
            if (Current() is not { } page || bitmap == null) return;
            page.Area.Validate(); page.Reviewed = true; page.Skip = false; Next();
        }, true));
        toolbar.Children.Add(Ui.Button("Add another area", () =>
        {
            if (Current() is not { } page || bitmap == null) return;
            page.Area.Validate(); page.Reviewed = true;
            var extra = new Page(page.Path); pages.Insert(list.SelectedIndex + 1, extra);
            list.ItemsSource = null; list.ItemsSource = pages; list.SelectedItem = extra; Update();
        }));
        toolbar.Children.Add(Ui.Button("Skip photo", () => { if (Current() is { } page) { page.Skip = true; Next(); } }));
        contrast.Click += (_, _) => { if (!switching && Current() is { } page) { page.Contrast = contrast.IsChecked == true; page.Reviewed = false; Update(); } };
        void Next()
        {
            var next = pages.Skip(list.SelectedIndex + 1).Concat(pages.Take(list.SelectedIndex + 1)).FirstOrDefault(p => !p.Reviewed && !p.Skip);
            if (next != null) list.SelectedItem = next;
            Update();
        }
        list.SelectionChanged += (_, _) =>
        {
            if (Current() is not { } page) return;
            switching = true; contrast.IsChecked = page.Contrast; switching = false;
            points = page.Area.Corners; bitmap = null; photo.Source = null;
            try { bitmap = PhotoPageImage.Load(page.Path); photo.Source = bitmap; Draw(); Update(); }
            catch (Exception ex) { status.Text = $"{System.IO.Path.GetFileName(page.Path)}: {ex.Message}. Skip this photo or cancel."; }
        };
        list.ItemsSource = pages; list.SelectedIndex = 0; Update();
    }
}
