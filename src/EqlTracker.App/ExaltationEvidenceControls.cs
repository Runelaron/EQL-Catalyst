using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    private static UIElement FloatingEffectInspector(UIElement content)
    {
        var root = new DockPanel { Margin = new(0, 0, 0, 5) }; var title = UI.Label("Effect"); title.Margin = new(2, 0, 0, 5); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title); root.Children.Add(UI.Scroll(content)); return root;
    }
    private bool exaltLabelChanging;
    private void RefreshExaltationCompatibilityLabel()
    {
        string label = "Current Loadout · " + string.Join(" / ", Controller.Profile.ActiveLoadout.Classes);
        if (exaltLabelChanging || exaltCompatibility.Items.Cast<string>().Contains(label)) return;
        int selected = Math.Max(0, exaltCompatibility.SelectedIndex); bool old = initializing; initializing = true; exaltLabelChanging = true;
        exaltCompatibility.ItemsSource = new[] { "All", label, "Incompatible" }; exaltCompatibility.SelectedIndex = selected;
        exaltCompatibility.ToolTip = "Restrictions for the current classes and selected Self/Pet target. Unknown restrictions remain visible under All.";
        exaltLabelChanging = false; initializing = old;
    }
}
