using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace PersonalLibrary.App;

internal static class ShelfSuggestions
{
    public static void Attach(TextBox input, IEnumerable<string> shelves, bool commaSeparated = true)
        => Attach(input, () => shelves, commaSeparated, commaSeparated ? "Matching shelves" : "Matching stores");

    public static void Attach(TextBox input, Func<IEnumerable<string>> suggestions, bool commaSeparated = true, string name = "Matching shelves")
    {
        var list = new ListBox { Focusable = false, MaxHeight = 200 };
        AutomationProperties.SetName(list, name);
        list.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(input.ActualWidth)) { Source = input });
        var popup = new Popup { PlacementTarget = input, Placement = PlacementMode.Bottom, Child = list, Focusable = false };

        (int Start, int End) CurrentEntry()
        {
            if (!commaSeparated) return (0, input.Text.Length);
            var caret = input.CaretIndex;
            var start = caret == 0 ? 0 : input.Text.LastIndexOf(',', caret - 1) + 1;
            var end = input.Text.IndexOf(',', caret);
            return (start, end < 0 ? input.Text.Length : end);
        }

        void Refresh()
        {
            var (start, end) = CurrentEntry();
            var query = input.Text[start..end].Trim();
            if (!input.IsKeyboardFocused || query.Length == 0 ||
                input.SelectionStart < start || input.SelectionStart + input.SelectionLength > end)
            {
                popup.IsOpen = false;
                return;
            }

            var otherShelves = commaSeparated
                ? (input.Text[..start] + input.Text[end..]).Split(',', StringSplitOptions.TrimEntries) : [];
            var matches = suggestions().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)
                .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Except(otherShelves, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = matches;
            list.SelectedIndex = -1;
            popup.IsOpen = matches.Length > 0;
        }

        void Choose(string name)
        {
            var (start, end) = CurrentEntry();
            while (start < end && char.IsWhiteSpace(input.Text[start])) start++;
            input.Select(start, end - start);
            input.SelectedText = name;
            input.CaretIndex = start + name.Length;
            popup.IsOpen = false;
        }

        input.TextChanged += (_, _) => Refresh();
        input.SelectionChanged += (_, _) => Refresh();
        input.LostKeyboardFocus += (_, _) => popup.IsOpen = false;
        input.Unloaded += (_, _) => popup.IsOpen = false;
        input.PreviewKeyDown += (_, args) =>
        {
            if (!popup.IsOpen || Keyboard.Modifiers != ModifierKeys.None) return;
            switch (args.Key)
            {
                case Key.Down:
                    list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1);
                    break;
                case Key.Up:
                    list.SelectedIndex = list.SelectedIndex < 0 ? list.Items.Count - 1 : Math.Max(list.SelectedIndex - 1, 0);
                    break;
                case Key.Enter when list.SelectedItem is string name:
                    Choose(name);
                    break;
                case Key.Escape:
                    popup.IsOpen = false;
                    break;
                default:
                    return;
            }
            if (popup.IsOpen && list.SelectedItem is { } selected) list.ScrollIntoView(selected);
            args.Handled = true;
        };
        list.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (args.OriginalSource is not DependencyObject source ||
                ItemsControl.ContainerFromElement(list, source) is not ListBoxItem { Content: string name }) return;
            Choose(name);
            args.Handled = true;
        };
    }
}
