using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class GoodreadsRatingsWindow : Window
{
    public GoodreadsRatingsWindow(GoodreadsRatingsPreview preview)
    {
        Title = "Preview Goodreads ratings";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 740;
        Height = 560;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading("Preview Goodreads ratings"));
        header.Children.Add(Ui.Text($"{preview.Changes.Count} ratings to add · {preview.AlreadyRated} existing ratings kept · " +
            $"{preview.UnmatchedArchiveRatings} archive ratings without a unique linked book", 14, "MutedBrush"));
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        var apply = Ui.Button("Back up and add ratings", () => DialogResult = true, true);
        apply.IsEnabled = preview.Changes.Count > 0;
        actions.Children.Add(apply);
        actions.Children.Add(Ui.Button("Close", Close));
        var rows = preview.Changes.Select(change => change.Display)
            .Concat(preview.UnmatchedArchiveTitles.Select(title => $"{title}  ·  Needs a unique match")).ToArray();
        if (rows.Length > 0) root.Children.Add(new ListBox { ItemsSource = rows, ItemTemplate = Ui.WrappedLabel(".") });
    }
}
