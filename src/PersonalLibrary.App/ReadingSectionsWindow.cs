using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PersonalLibrary.Core;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class ReadingSectionsWindow : Window
{
    private readonly LibraryStore store;
    private readonly StackPanel rows = new();

    public ReadingSectionsWindow(LibraryStore store)
    {
        this.store = store;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Currently Reading sections";
        Width = 590;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(Ui.Heading(Title, 24));
        body.Children.Add(Ui.Text("Removing a section moves its books to Other books.", 13, "MutedBrush"));
        var scroll = Ui.Scroll(rows);
        scroll.MaxHeight = 280;
        body.Children.Add(scroll);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(Ui.Button("Add section", () => AddRow(new ReadingSection(Guid.NewGuid(), ""), focus: true)));
        actions.Children.Add(Ui.Button("Save sections", Save, true));
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        body.Children.Add(actions);
        Content = body;

        foreach (var section in store.ReadingSections()) AddRow(section);
    }

    private void AddRow(ReadingSection section, bool focus = false)
    {
        var row = new DockPanel { Tag = section.Id };
        var name = new TextBox { Text = section.Name, Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetName(name, "Section name");
        foreach (var (label, action) in new (string, Action)[]
        {
            ("Move up", () => MoveRow(row, -1)),
            ("Move down", () => MoveRow(row, 1)),
            ("Remove", () => rows.Children.Remove(row))
        })
        {
            var button = Ui.Button(label, action);
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Insert(0, button);
        }
        row.Children.Add(name);
        rows.Children.Add(row);
        if (focus)
        {
            name.Focus();
            name.BringIntoView();
        }
    }

    private void MoveRow(DockPanel row, int offset)
    {
        var index = rows.Children.IndexOf(row) + offset;
        if (index < 0 || index >= rows.Children.Count) return;
        var focused = System.Windows.Input.Keyboard.FocusedElement;
        rows.Children.Remove(row);
        rows.Children.Insert(index, row);
        if (focused != null) System.Windows.Input.Keyboard.Focus(focused);
    }

    private void Save()
    {
        var sections = rows.Children.Cast<DockPanel>().Select(row =>
            new ReadingSection((Guid)row.Tag, ((TextBox)row.Children[^1]).Text));
        store.SaveReadingSections(sections);
        DialogResult = true;
    }
}
