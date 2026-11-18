using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;

namespace PersonalLibrary.App;

internal sealed class LibraryDisplayWindow : Window
{
    public LibraryDisplay Value { get; private set; }
    public LibraryDisplayWindow(LibraryDisplay current)
    {
        Value = current;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "My Books display"; Width = 430; Height = 460; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Ui.Heading("My Books display", 24));
        panel.Children.Add(Ui.Text("Cover rows use this many books when space permits and fewer in a narrow window. Font sizes apply to both views.", 13, "MutedBrush"));
        var columns = Ui.Input(panel, "Books per row (1–20)", current.Columns.ToString());
        var title = Ui.Input(panel, "Title font size (12–32)", current.TitleSize.ToString());
        var detail = Ui.Input(panel, "Details font size (10–24)", current.DetailSize.ToString());
        var actions = new WrapPanel();
        actions.Children.Add(Ui.Button("Save", () =>
        {
            int Read(TextBox input, int min, int max)
            {
                if (!int.TryParse(input.Text, out var n) || n < min || n > max)
                    throw new InvalidOperationException($"Enter a whole number from {min} to {max}.");
                return n;
            }
            Value = new(Read(columns, 1, 20), Read(title, 12, 32), Read(detail, 10, 24));
            DialogResult = true;
        }, true));
        actions.Children.Add(Ui.Button("Cancel", Close));
        panel.Children.Add(actions); Content = panel;
    }
}
