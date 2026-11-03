using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal static class Ui
{
    public static TextBlock Text(string text, double size = 14, string brush = "TextBrush")
    {
        var control = new TextBlock { Text = text, FontSize = size, Margin = new Thickness(0, 0, 0, 8) };
        control.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return control;
    }
    public static TextBlock Heading(string text, double size = 26)
    {
        var control = Text(text, size); control.FontFamily = new FontFamily("Georgia"); return control;
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => Guard(action);
        if (primary)
        {
            button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#176B62"));
            button.Foreground = Brushes.White;
            button.Content = new TextBlock { Text = text, Foreground = Brushes.White };
        }
        return button;
    }
    public static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Personal Library", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public static Border Card(UIElement content)
    {
        var card = new Border { Child = content, Padding = new Thickness(22), Margin = new Thickness(0, 0, 16, 16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); return card;
    }
    public static TextBox Input(Panel panel, string label, string value = "", bool multiline = false)
    {
        panel.Children.Add(Text(label, 12, "MutedBrush"));
        var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 160 : 0, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        panel.Children.Add(input); return input;
    }
    public static FrameworkElement Cover(Book book, LibraryStore store, double width = 88, double height = 130)
    {
        var path = store.CoverPath(book.SelectedEdition);
        if (path != null)
        {
            try
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                return new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 20, 0) };
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { }
        }
        string[] colors = ["#49736B", "#6D657F", "#936347", "#546F87", "#7A7549"];
        var color = colors[(int)((uint)book.Id.GetHashCode() % colors.Length)];
        var panel = new StackPanel { Margin = new Thickness(9, 14, 9, 4) };
        panel.Children.Add(new TextBlock { Text = "PERSONAL LIBRARY", Foreground = Brushes.White, FontSize = 8, Opacity = .75 });
        panel.Children.Add(new TextBlock { Text = book.Title, Foreground = Brushes.White, FontFamily = new FontFamily("Georgia"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MaxHeight = height - 55, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border { Width = width, Height = height, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Child = panel, Margin = new Thickness(0, 0, 20, 0), CornerRadius = new CornerRadius(2) };
    }
}
