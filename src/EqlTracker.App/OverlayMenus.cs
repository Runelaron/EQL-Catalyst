using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace EqlTracker.App;

public sealed class MenuInteractionState
{
    private DateTime? outside;
    public bool ShouldDismiss(DateTime now, bool inside, bool keyboard)
    { if (inside || keyboard) { outside = null; return false; } outside ??= now; return now - outside >= TimeSpan.FromSeconds(1); }
    public void Reset() => outside = null;
}
public static class OverlayMenus
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContextMenu, object> configured = new();
    private static readonly List<WeakReference<ContextMenu>> openMenus=[];
    private static WeakReference<Button>? closingAnchor;
    static OverlayMenus()
    {
        // A ContextMenu captures the mouse. Consume a second anchor gesture before
        // popup dismissal can replay it as a fresh Button.Click and reopen the menu.
        InputManager.Current.PreProcessInput+=(_,e)=> { if(e.StagingItem.Input is KeyEventArgs {IsDown:true})closingAnchor=null;if(e.StagingItem.Input is MouseButtonEventArgs mouse&&ConsumeAnchorGesture(mouse))e.Cancel(); };
        EventManager.RegisterClassHandler(typeof(Button),Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>ConsumeAnchorGesture(e)),true);
        EventManager.RegisterClassHandler(typeof(Button),Mouse.PreviewMouseUpEvent,new MouseButtonEventHandler((_,e)=>ConsumeAnchorGesture(e)),true);
    }
    private static bool Within(DependencyObject? source,DependencyObject anchor)
    { for(var node=source;node!=null;node=node is System.Windows.Media.Visual||node is System.Windows.Media.Media3D.Visual3D?System.Windows.Media.VisualTreeHelper.GetParent(node):(node as FrameworkContentElement)?.Parent)if(ReferenceEquals(node,anchor))return true;return false; }
    private static bool AnchorContains(Button button)
    { if(!button.IsVisible||!button.IsLoaded||PresentationSource.FromVisual(button)==null)return false;return new Rect(button.PointToScreen(new()),button.PointToScreen(new(button.ActualWidth,button.ActualHeight))).Contains(Native.CursorPosition()); }
    private static void CloseMenu(ContextMenu menu)
    {menu.IsOpen=false;if(menu.PlacementTarget is Button button)UI.SetPressed(button,false);}
    private static bool ConsumeAnchorGesture(MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left)return false;
        bool down=e.RoutedEvent==Mouse.PreviewMouseDownEvent||e.RoutedEvent==Mouse.MouseDownEvent;
        bool up=e.RoutedEvent==Mouse.PreviewMouseUpEvent||e.RoutedEvent==Mouse.MouseUpEvent;
        if(up&&closingAnchor!=null){closingAnchor=null;e.Handled=true;return true;}
        if(!down)return false;
        // A release outside our application may not be routed back. A new press
        // starts a new gesture, except the same already-handled routed press.
        if(e.Handled&&closingAnchor!=null)return true;
        closingAnchor=null;
        var source=e.OriginalSource as DependencyObject??e.Source as DependencyObject;
        foreach(var weak in openMenus.ToArray())if(weak.TryGetTarget(out var menu)&&menu.IsOpen&&menu.PlacementTarget is Button anchor)
        { bool menuContent=source!=null&&!ReferenceEquals(source,menu)&&Within(source,menu);if(Within(source,anchor)||!menuContent&&AnchorContains(anchor)){closingAnchor=new(anchor);CloseMenu(menu);e.Handled=true;return true;} }
        return false;
    }
    public static object ClickThroughHeader(){var hint=UI.Text("",10,"Muted");hint.SetResourceReference(TextBlock.TextProperty,"ClickThroughShortcutHint");return UI.Stack(UI.Text("Click-through",12),hint);}
    internal static bool DismissForForegroundChange(bool timed,bool changed,bool gameOrCompanion)=>changed&&(!timed||!gameOrCompanion);
    public static MenuItem Choice(ItemsControl parent, object header, Func<bool> selected, Action action)
    {
        var item = new MenuItem { Header = header, IsCheckable = true }; item.Click += (_, _) => { action(); item.IsChecked = selected(); };
        parent.Items.Add(item);
        void Refresh() => item.IsChecked = selected();
        Refresh(); if (parent is ContextMenu menu) menu.Opened += (_, _) => Refresh(); else if (parent is MenuItem group) group.SubmenuOpened += (_, _) => Refresh();
        return item;
    }
    public static Button Button(ContextMenu menu, string help, string label = "⋯", bool timedDismissal = false)
    {
        Button? button = null; button = UI.Button(label, () => Toggle(menu, button)); button.Width = button.Height = 24; button.Padding = new(0); button.ToolTip = help; button.ContextMenu = menu;
        menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; AutoDismiss(menu, timedDismissal); return button;
    }
    public static void Toggle(ContextMenu menu, FrameworkElement? anchor = null)
    { if(closingAnchor?.TryGetTarget(out var closing)==true&&ReferenceEquals(closing,anchor??menu.PlacementTarget))return;if (!menu.IsOpen && anchor != null) menu.PlacementTarget = anchor;bool next=!menu.IsOpen;if(next&&!openMenus.Any(w=>w.TryGetTarget(out var item)&&ReferenceEquals(item,menu)))openMenus.Add(new(menu));menu.IsOpen=next;if (menu.PlacementTarget is Button button) UI.SetPressed(button,menu.IsOpen); }
    public static void AutoDismiss(ContextMenu menu, bool timedDismissal = false)
    {
        if (configured.TryGetValue(menu, out _)) return; configured.Add(menu, new());
        menu.StaysOpen = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; var state = new MenuInteractionState();
        bool keyboard = false; Point last = default; bool wasDown = false; IntPtr foreground = IntPtr.Zero;
        bool Contains(FrameworkElement? element, Point cursor)
        {
            if (element == null || !element.IsVisible || !element.IsLoaded || PresentationSource.FromVisual(element) == null) return false;
            return new Rect(element.PointToScreen(new()), element.PointToScreen(new(element.ActualWidth, element.ActualHeight))).Contains(cursor);
        }
        bool InMenu(ItemsControl items, Point cursor)
        {
            if (Contains(items, cursor)) return true;
            foreach (var item in items.Items.OfType<MenuItem>())
                if (item.IsSubmenuOpen && item.Template.FindName("PART_Popup", item) is Popup { Child: FrameworkElement child } && (Contains(child, cursor) || InMenu(item, cursor))) return true;
            return false;
        }
        void Begin() { if(!openMenus.Any(w=>w.TryGetTarget(out var item)&&ReferenceEquals(item,menu)))openMenus.Add(new(menu));keyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice; state.Reset(); foreground = Native.ForegroundHandle; last = Native.CursorPosition(); wasDown = Mouse.LeftButton == MouseButtonState.Pressed; timer.Start(); Usability.Tooltips(menu); if (menu.PlacementTarget is Button b) UI.SetPressed(b, true); }
        menu.Opened += (_, _) => Begin();
        menu.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseMenu(menu); e.Handled = true; } else { keyboard = true; state.Reset(); } };
        menu.PreviewMouseDown += (_, e) => { if(ConsumeAnchorGesture(e))return;if (!InMenu(menu, Native.CursorPosition())) { CloseMenu(menu); e.Handled = true; } };
        timer.Tick += (_, _) =>
        {
            var currentForeground = Native.ForegroundHandle; if (currentForeground != foreground) { foreground = currentForeground; if (DismissForForegroundChange(timedDismissal,true,Native.GameOrCompanionForeground())) { CloseMenu(menu); return; } }
            var cursor = Native.CursorPosition(); if (cursor != last) keyboard = false; last = cursor;
            bool inside = InMenu(menu, cursor), down = Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed;
            if ((!wasDown && down && !inside && !Contains(menu.PlacementTarget as FrameworkElement, cursor)) || (timedDismissal && state.ShouldDismiss(DateTime.UtcNow, inside || Contains(menu.PlacementTarget as FrameworkElement, cursor), keyboard))) CloseMenu(menu);
            wasDown = down;
            if (menu.PlacementTarget != null && Window.GetWindow(menu.PlacementTarget) is { IsVisible: false }) CloseMenu(menu);
        };
        menu.Closed += (_, _) => { openMenus.RemoveAll(w=>!w.TryGetTarget(out var item)||ReferenceEquals(item,menu));timer.Stop(); state.Reset(); if (menu.PlacementTarget is Button b) UI.SetPressed(b, false); };
        menu.Unloaded += (_, _) => timer.Stop();
        if (menu.IsOpen) Begin();
    }
}
