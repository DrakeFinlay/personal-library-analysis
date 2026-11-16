using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class BookRemovalWindow : Window
{
    public CalibreRemovalPlan? CalibrePlan { get; private set; }

    public BookRemovalWindow(IReadOnlyList<Book> selected)
    {
        Title = "Review book removal";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 780;
        Height = 610;
        MinWidth = 560;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        header.Children.Add(Ui.Heading($"Remove {selected.Count} selected {(selected.Count == 1 ? "book" : "books")}"));
        var personal = new RadioButton { Content = "Personal Library only", IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
        var both = new RadioButton
        {
            Content = "Both Personal Library and Calibre", IsEnabled = CalibreRemoval.CanPlan(selected),
            Margin = new Thickness(0, 0, 0, 10)
        };
        personal.SetResourceReference(ForegroundProperty, "TextBrush");
        both.SetResourceReference(ForegroundProperty, "TextBrush");
        header.Children.Add(personal);
        header.Children.Add(both);
        if (!both.IsEnabled)
            header.Children.Add(Ui.Text("Select books linked to the same Calibre library to remove them from both.", 13, "MutedBrush"));
        var warning = Ui.Text("", 14);
        header.Children.Add(warning);
        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var remove = Ui.Button("Confirm removal", () => DialogResult = true);
        controls.Children.Add(remove);
        var cancel = Ui.Button("Cancel", () => DialogResult = false);
        cancel.IsCancel = true;
        controls.Children.Add(cancel);
        DockPanel.SetDock(controls, Dock.Bottom);
        root.Children.Add(controls);
        var list = new ListBox();
        root.Children.Add(list);
        void ShowPersonal()
        {
            CalibrePlan = null;
            warning.Text = "These entries will be removed from your Personal Library. Linked books stay in Calibre and are skipped on future refreshes. A Personal Library backup is taken first.";
            list.ItemsSource = selected.Select(book => $"{book.Title} · {book.Author}");
        }
        personal.Checked += (_, _) => ShowPersonal();
        both.Checked += (_, _) =>
        {
            try
            {
                var plan = CalibreRemoval.Preview(selected);
                CalibrePlan = plan;
                warning.Text = $"Calibre will move these entries and all {plan.Targets.Sum(target => target.Formats.Length)} ebook formats to its trash, along with their covers and other files. Personal Library takes a backup first. Close Calibre before confirming.";
                list.ItemsSource = plan.Targets.Select(target =>
                    $"{target.Title} · {(target.Formats.Length == 0 ? "No ebook formats" : string.Join(", ", target.Formats))}");
            }
            catch (Exception ex)
            {
                personal.IsChecked = true;
                MessageBox.Show(this, ex.Message, "Calibre removal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        ShowPersonal();
        Content = root;
    }
}
