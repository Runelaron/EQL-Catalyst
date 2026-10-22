using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    private static UIElement ItemFloatingInspector(UIElement content)
    {
        var root=(DockPanel)FocusedLayout.FloatingInspector("Item",content);
        if(root.Children.OfType<Border>().FirstOrDefault() is { } card)card.Padding=new(3,6,6,6);
        return root;
    }
    private static UIElement TrackerLeftPane(UIElement content)
    {
        var pane=new DockPanel { LastChildFill=true, Margin=new(0,0,0,5) };
        var settings=UI.Button("Settings",()=>((App)Application.Current).OpenSettings());
        settings.Tag="tracker-settings-footer";settings.Height=28;settings.MinWidth=68;settings.Margin=new(0,5,0,0);settings.ToolTip="Open Settings. Wiki refresh and layout reset are in Sync & files.";
        var footer=UI.Row(settings,UpdateReadyButton());footer.HorizontalAlignment=HorizontalAlignment.Left;
        DockPanel.SetDock(footer,Dock.Bottom);pane.Children.Add(footer);pane.Children.Add(content);return pane;
    }
}
