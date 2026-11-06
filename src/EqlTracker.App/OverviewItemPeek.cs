using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class OverviewItemPeek : Popup
{
    private readonly TrackerController controller;
    private readonly Border frame=new(){BorderThickness=new(1),Padding=new(8),CornerRadius=new(2),Focusable=true};
    private readonly StackPanel content=new();
    private readonly WorkspacePanelMotion motion;
    private LootReceiptGroup? receipt;
    private string shown="",requested="";
    private bool closing;
    private IInputElement? returnFocus;
    internal OverviewItemPeek(TrackerController controller)
    {
        this.controller=controller;AllowsTransparency=true;StaysOpen=true;Placement=PlacementMode.Bottom;VerticalOffset=4;
        frame.SetResourceReference(Border.BackgroundProperty,"DetailSurface");frame.SetResourceReference(Border.BorderBrushProperty,"Accent");
        frame.Child=UI.Scroll(content);Child=frame;motion=new(frame);
        System.Windows.Automation.AutomationProperties.SetName(frame,"Loot item details");
        KeyboardNavigation.SetTabNavigation(frame,KeyboardNavigationMode.Cycle);
        Opened+=(_,_)=>{frame.UpdateLayout();motion.Snap(false);motion.To(true,Animate,()=>{});Keyboard.Focus(frame);Mouse.Capture(frame,CaptureMode.SubTree);};
        frame.PreviewMouseDown+=(_,e)=>{var p=e.GetPosition(frame);if(p.X<0||p.Y<0||p.X>frame.ActualWidth||p.Y>frame.ActualHeight){e.Handled=true;Dismiss();}};
        frame.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Dismiss();}};
        frame.LostMouseCapture+=(_,_)=>{if(IsOpen&&!closing)Dismiss();};
        Closed+=(_,_)=>{closing=false;if(Mouse.Captured==frame)Mouse.Capture(null);if(returnFocus is DependencyObject d&&d is not UIElement {IsVisible:false})Keyboard.Focus(returnFocus);};
    }
    private bool Animate=>controller.Profile.SmoothMeterMotion&&SystemParameters.ClientAreaAnimation;
    internal void Show(LootReceiptGroup row,FrameworkElement anchor)
    {
        returnFocus=Keyboard.FocusedElement;receipt=row;shown="";closing=false;PlacementTarget=anchor;
        var point=anchor.PointToScreen(new Point());var area=System.Windows.Forms.Screen.FromPoint(new((int)point.X,(int)point.Y)).WorkingArea;var dpi=VisualTreeHelper.GetDpi(anchor);
        frame.Width=Math.Max(200,Math.Min(420,area.Width/dpi.DpiScaleX-16));frame.MaxHeight=Math.Max(120,Math.Min(480,area.Height/dpi.DpiScaleY-16));
        Refresh();if(!IsOpen)IsOpen=true;else motion.To(true,Animate,()=>{});
    }
    internal void Refresh()
    {
        if(receipt==null)return;var catalog=controller.EquipmentView?.Catalog??controller.Catalog;var item=catalog.FindKey(receipt.ItemKey)??catalog.Find(receipt.Name);int? rank=receipt.Receipts.FirstOrDefault()?.Rank;
        string key=receipt.Identity+"|"+(item==null?"missing":PetResearch.Hash(item));if(key==shown)return;shown=key;
        content.Children.Clear();
        var title=UI.Text((item?.Name??Identity.Canonical(receipt.Name))+(rank.HasValue?" +"+rank:" · rank unknown"),16,"Accent",true);title.Margin=new(0,0,0,4);
        var header=new DockPanel();var close=UI.Button("×",()=>Dismiss());close.Width=close.Height=24;close.Padding=new(0);close.Margin=new(4,0,0,0);close.ToolTip="Close item details · Escape";DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var glyph=new ItemRoleGlyph{Role=item==null?"glyph-unknown":ItemEquipmentIcons.For(item).Select(i=>i.Role).FirstOrDefault()??"glyph-unknown"};DockPanel.SetDock(glyph,Dock.Left);header.Children.Add(glyph);header.Children.Add(title);content.Children.Add(header);
        if(item!=null&&rank.HasValue)content.Children.Add(new ClassicItemCard(item,rank.Value));
        else content.Children.Add(UI.Text(item==null?"Item statistics are not yet available.":"Statistics at an unknown rank cannot be calculated.",12,"SecondaryText"));
        if(item is {MetadataComplete:false})content.Children.Add(UI.Text("Some item metadata is unavailable.",11,"SecondaryText"));
        content.Children.Add(UI.Text("Recorded upgrade rank · "+(rank?.ToString()??"Unknown"),11,"SecondaryText"));
        if((item==null||!item.MetadataComplete)&&requested!=receipt.ItemKey+receipt.Name&&!controller.Demo){requested=receipt.ItemKey+receipt.Name;controller.RequestGearMetadata(item?.Name??receipt.Name);}
    }
    internal void Dismiss(bool snap=false)
    {
        if(!IsOpen)return;if(closing&&!snap)return;closing=true;if(Mouse.Captured==frame)Mouse.Capture(null);
        motion.To(false,!snap&&Animate,()=>IsOpen=false);
    }
}
