using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FgScanner.App.Views.Controls;

/// <summary>
/// A ScrollViewer that leaves the window's shortcuts working. A plain ScrollViewer takes the arrow,
/// Page Up/Down and Home/End keys even with Ctrl or Shift held, so once the sections and the Groups
/// top area scrolled, a shortcut on one of those keys (Ctrl+Shift+Left rotates the page) silently did
/// nothing whenever focus was inside. A key that an element above has bound is left to that binding;
/// every other key still scrolls.
/// </summary>
public class ShortcutScrollViewer : ScrollViewer
{
    public ShortcutScrollViewer()
    {
        // Fluent's ScrollViewer style is implicit, and implicit styles match the exact type only. Without
        // this a subclass renders the classic template: bars that take layout space, and a corner square
        // that stays light grey in the dark theme.
        SetResourceReference(StyleProperty, typeof(ScrollViewer));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsBoundAbove(e))
        {
            base.OnKeyDown(e);
        }
    }

    private bool IsBoundAbove(KeyEventArgs e)
    {
        for (var node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not UIElement element)
            {
                continue;
            }

            foreach (InputBinding binding in element.InputBindings)
            {
                if (binding.Gesture?.Matches(this, e) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
