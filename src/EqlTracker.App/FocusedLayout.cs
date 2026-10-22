using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EqlTracker.App;

/// <summary>Progressive disclosure keeps the same controls and their selection alive.</summary>
public static class FocusedLayout
{
    public static readonly DependencyProperty IsPrimaryProperty = DependencyProperty.RegisterAttached("IsPrimary", typeof(bool), typeof(FocusedLayout), new PropertyMetadata(false));
    public static bool GetIsPrimary(DependencyObject value) => (bool)value.GetValue(IsPrimaryProperty);
    public static void SetIsPrimary(DependencyObject value, bool primary) => value.SetValue(IsPrimaryProperty, primary);
    internal static Expander Disclosure(string title, UIElement content, string help, bool expanded = false)
    {
        var panel = new Expander { Header = title, Content = content, IsExpanded = expanded, Margin = new(0, 0, 0, 4), ToolTip = help };
        return panel;
    }
    internal static Button Menu(string title, params (string Label, Action Action, string Help)[] actions)
    {
        var menu = new ContextMenu();
        foreach (var action in actions)
        {
            var command = new MenuItem { Header = action.Label, ToolTip = action.Help };
            command.Click += (_, _) => action.Action(); menu.Items.Add(command);
        }
        var button = OverlayMenus.Button(menu, "Additional actions for this workspace.", title);
        button.Width = double.NaN; button.Height = double.NaN; button.Padding = new(9, 3, 9, 3); return button;
    }
    internal static Border Inspector(string title, UIElement content)
    {
        var card = UI.Card(UI.Scroll(UI.Stack(UI.Label(title), content)));
        card.SetResourceReference(Border.BackgroundProperty, "DetailSurface"); return card;
    }
    internal static UIElement FloatingInspector(string title,UIElement content)
    {
        var root=new DockPanel{Margin=new(0,0,0,5)};var label=UI.Label(title);label.Margin=new(2,0,0,5);DockPanel.SetDock(label,Dock.Top);root.Children.Add(label);
        var card=UI.Card(UI.Scroll(content));card.Margin=new(0);card.SetResourceReference(Border.BackgroundProperty,"DetailSurface");root.Children.Add(card);return root;
    }
    internal static TextBlock Purpose(string text)
    {
        var label = UI.Text(text, 11, "Muted"); label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis; label.ToolTip = text; return label;
    }
    internal static UIElement Search(TextBox input, string hint)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        var watermark = UI.Text(hint, 12, "Muted"); watermark.Margin = new(6, 0, 6, 0); watermark.IsHitTestVisible = false; watermark.VerticalAlignment = VerticalAlignment.Center; watermark.TextWrapping = TextWrapping.NoWrap;
        grid.Children.Add(input); grid.Children.Add(watermark);
        void Update() => watermark.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.TextChanged += (_, _) => Update(); System.Windows.Automation.AutomationProperties.SetName(input, hint); Update(); return grid;
    }
}
