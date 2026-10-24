using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EqlTracker.App;

internal static class PlanningScrollbars
{
    internal static void Apply(Window window)
    {
        var original = (Style)Application.Current.FindResource(typeof(ScrollBar));
        var compact = new Style(typeof(ScrollBar), original);
        compact.Setters.Add(new Setter(FrameworkElement.WidthProperty, 5d));
        var horizontal = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
        horizontal.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
        horizontal.Setters.Add(new Setter(FrameworkElement.HeightProperty, 5d));
        compact.Triggers.Add(horizontal);
        window.Resources[typeof(ScrollBar)] = compact;
        window.Resources[SystemParameters.VerticalScrollBarWidthKey] = 5d;
        window.Resources[SystemParameters.HorizontalScrollBarHeightKey] = 5d;
        // Menus keep their own density, including menus which inherit window resources.
        var menu = new Style(typeof(ContextMenu), (Style)Application.Current.FindResource(typeof(ContextMenu)));
        menu.Resources[typeof(ScrollBar)] = original;
        menu.Resources[SystemParameters.VerticalScrollBarWidthKey] = 10d;
        menu.Resources[SystemParameters.HorizontalScrollBarHeightKey] = 10d;
        window.Resources[typeof(ContextMenu)] = menu;
    }
}
