using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class OverlayMenuDensity
{
    private static readonly List<WeakReference<ContextMenu>> menus=[];
    public static void Attach(ContextMenu menu, CharacterProfile profile)
    { menus.Add(new(menu)); Apply(menu,profile.OverlayMenus);menu.Opened+=(_,_)=>Apply(menu,profile.OverlayMenus); }
    public static void Update(CharacterProfile profile)
    { foreach(var weak in menus.ToArray())if(weak.TryGetTarget(out var menu))Apply(menu,profile.OverlayMenus);else menus.Remove(weak); }
    private static void Apply(ContextMenu menu,OverlayMenuSize size)
    {
        if(Equals(menu.Tag,size))return;menu.Tag=size;bool compact=size==OverlayMenuSize.Compact;
        double font=compact?11:13;menu.FontSize=font;menu.Padding=new(compact?2:3);
        var items=new Style(typeof(MenuItem),(Style)Application.Current.FindResource(typeof(MenuItem)));items.Setters.Add(new Setter(Control.FontSizeProperty,font));items.Setters.Add(new Setter(Control.PaddingProperty,compact?new Thickness(4,2,4,2):new Thickness(6,4,6,4)));menu.Resources[typeof(MenuItem)]=items;
        var bars=new Style(typeof(ScrollBar),(Style)Application.Current.FindResource(typeof(ScrollBar)));bars.Setters.Add(new Setter(FrameworkElement.WidthProperty,compact?5d:10d));menu.Resources[typeof(ScrollBar)]=bars;
        void Headers(ItemsControl parent){foreach(var item in parent.Items.OfType<MenuItem>()){if(item.Header is DependencyObject header)foreach(var text in Usability.Tree(header).OfType<TextBlock>())text.FontSize=font;Headers(item);}}
        Headers(menu);
    }
}
