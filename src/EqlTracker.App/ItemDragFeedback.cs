using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

/// <summary>A passive, window-local drag image. It never owns input or creates a native window.</summary>
internal sealed class ItemDragFeedback : Adorner
{
    private readonly Border image;
    private readonly ItemRoleGlyph glyph = new() { Width=32,Height=32, Margin=new(0) };
    private Point point;
    private static ItemDragFeedback? active;
    internal static void Destination(ItemDefinition item, string? slot)
    {
        if(active==null)return;
        var icons=ItemEquipmentIcons.For(item);var slots=ItemEquipmentIcons.ArmorSlots(item);
        int index=!ItemEquipmentIcons.IsWeapon(item)&&slot!=null?slots.ToList().FindIndex(s=>s==EquipmentPositions.Normalize(slot)):-1;
        string role=icons[index>=0&&index<icons.Count?index:0].Role;
        if(active.glyph.Role!=role){active.glyph.Role=role;active.glyph.InvalidateVisual();}
    }
    private ItemDragFeedback(UIElement host,ItemDefinition item,int rank):base(host)
    {
        IsHitTestVisible=false;
        glyph.Role=ItemEquipmentIcons.For(item)[0].Role;
        image=UI.Card(UI.Row(glyph,UI.Text("+"+rank,11,"Foreground",true)));image.Padding=new(4);image.Opacity=.94;
        AddVisualChild(image);
    }
    internal static FrameworkElement Preview(ItemDefinition item,int rank)
    { var ghost=new ItemDragFeedback(new Border(),item,rank);ghost.RemoveVisualChild(ghost.image);return ghost.image; }
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>image;
    protected override Size MeasureOverride(Size constraint){image.Measure(new(100,50));return constraint;}
    protected override Size ArrangeOverride(Size size){image.Arrange(new Rect(point,image.DesiredSize));return size;}
    internal static bool ShouldCancel(bool escape,IntPtr started,IntPtr foreground,bool current)=>escape||started!=foreground||!current;
    internal static DragDropEffects Run(FrameworkElement source,IDataObject data,DragDropEffects effects,ItemDefinition item,int rank,Func<bool> stillCurrent,Action? interrupted=null)
    {
        var window=Window.GetWindow(source);if(window?.Content is not UIElement host)return DragDropEffects.None;
        var layer=AdornerLayer.GetAdornerLayer(host);var ghost=new ItemDragFeedback(host,item,rank);var foreground=Native.ForegroundHandle;bool cancel=false;
        var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
        void Clear(){if(cancel)return;cancel=true;ghost.Visibility=Visibility.Collapsed;interrupted?.Invoke();}
        void Move(){using var measure=UpdateDiagnostics.Measure("Item drag feedback");if(ShouldCancel(false,foreground,Native.ForegroundHandle,window.IsVisible&&stillCurrent()))Clear();if(cancel)return;if(host.IsVisible){var p=host.PointFromScreen(Native.CursorPosition());ghost.point=new(p.X+14,p.Y+12);ghost.InvalidateArrange();}}
        QueryContinueDragEventHandler query=(_,e)=>{if(cancel||ShouldCancel(e.EscapePressed,foreground,Native.ForegroundHandle,window.IsVisible&&stillCurrent())){Clear();e.Action=DragAction.Cancel;}};
        GiveFeedbackEventHandler feedback=(_,e)=>{e.UseDefaultCursors=true;Move();};
        timer.Tick+=(_,_)=>Move();source.QueryContinueDrag+=query;source.GiveFeedback+=feedback;
        try{active=ghost;layer?.Add(ghost);Move();timer.Start();return DragDrop.DoDragDrop(source,data,effects);}
        finally{timer.Stop();source.QueryContinueDrag-=query;source.GiveFeedback-=feedback;layer?.Remove(ghost);active=null;}
    }
}

internal static class GearDragRows
{
    internal static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(int),typeof(GearDragRows),new FrameworkPropertyMetadata(0));
    internal static void State(DataGridRow row,int value)=>row.SetValue(StateProperty,value);
    internal static Style Style(Style? basis)
    {
        var style=new Style(typeof(DataGridRow),basis);
        var invalid=new Trigger{Property=StateProperty,Value=1};invalid.Setters.Add(new Setter(UIElement.OpacityProperty,.35));style.Triggers.Add(invalid);
        var valid=new Trigger{Property=StateProperty,Value=2};valid.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(106,214,234))));valid.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));style.Triggers.Add(valid);return style;
    }
}
