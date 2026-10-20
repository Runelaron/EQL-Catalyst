using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace EqlTracker.App;

internal static class CombatModeButtons
{
    internal static Button Create(string label, string channel, Action select)
    {
        var button = UI.Button(label, select);
        button.Width = 114; button.Height = 30; button.FontFamily = new("Georgia"); button.FontSize = 13;
        // Reuse the raised button's selected rim; the icon keeps its combat color.
        button.Resources["Accent"] = UI.Brush("Melee");
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); row.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var icon = new FrameworkElementFactory(typeof(DamageGlyph)); icon.SetValue(DamageGlyph.ChannelProperty, channel); icon.SetValue(FrameworkElement.WidthProperty, 14d); icon.SetValue(FrameworkElement.HeightProperty, 14d); icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)); icon.SetValue(UIElement.IsHitTestVisibleProperty, false);
        icon.SetValue(DamageGlyph.IsDamageModeProperty, channel == "Physical");
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding()); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(icon); row.AppendChild(text); button.ContentTemplate = new() { VisualTree = row };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
}
