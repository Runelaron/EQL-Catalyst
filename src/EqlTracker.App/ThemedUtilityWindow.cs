using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EqlTracker.App;

/// <summary>Small modeless tools use the main theme surface, never whole-window text opacity.</summary>
internal class ThemedUtilityWindow : Window
{
    internal StackPanel Body { get; }=new();
    internal WrapPanel Actions { get; }=new() { Margin=new(0,6,0,0) };
    internal ThemedUtilityWindow(string title,Window owner,double width,double height)
    {
        Title=CatalystBranding.Name+" — "+title;Owner=owner;Icon=CatalystBranding.WindowIcon;
        Width=width;Height=height;MinWidth=280;MinHeight=140;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Opacity=1;
        ResizeMode=ResizeMode.CanResizeWithGrip;ShowInTaskbar=false;Topmost=owner.Topmost;WindowStartupLocation=WindowStartupLocation.CenterOwner;UseLayoutRounding=SnapsToDevicePixels=true;
        var root=new DockPanel();var heading=new DockPanel { Background=Brushes.Transparent,Margin=new(0,0,0,5) };
        var close=UI.Button("×",Close);close.Width=close.Height=22;close.Margin=new(0);close.Padding=new(0);close.ToolTip="Close this window; live changes are retained.";DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);heading.Children.Add(UI.Text(title,15,"Foreground",true));
        heading.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is DependencyObject o&&Usability.Tree(close).Contains(o))return;if(e.LeftButton==MouseButtonState.Pressed)DragMove();};
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);DockPanel.SetDock(Actions,Dock.Bottom);root.Children.Add(Actions);root.Children.Add(UI.Scroll(Body));
        var frame=new Border { Padding=new(9),BorderThickness=new(1),CornerRadius=new(4),Child=root };frame.SetResourceReference(Border.BackgroundProperty,"Surface");frame.SetResourceReference(Border.BorderBrushProperty,"Accent");Content=frame;Appearance.RaisedButtons(root);
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};
        Loaded+=(_,_)=>{Constrain();Usability.Tooltips(root);};DpiChanged+=(_,_)=>Constrain();
    }
    private void Constrain()
    {
        if(Left < -10000||Top < -10000)return;
        var screen=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(Owner??this).Handle).WorkingArea;var dpi=VisualTreeHelper.GetDpi(this);
        MaxWidth=Math.Max(200,screen.Width/dpi.DpiScaleX-16);MaxHeight=Math.Max(140,screen.Height/dpi.DpiScaleY-16);MinWidth=Math.Min(MinWidth,MaxWidth);MinHeight=Math.Min(MinHeight,MaxHeight);Width=Math.Min(Width,MaxWidth);Height=Math.Min(Height,MaxHeight);
        Left=Math.Clamp(Left,screen.Left/dpi.DpiScaleX,screen.Right/dpi.DpiScaleX-Width);Top=Math.Clamp(Top,screen.Top/dpi.DpiScaleY,screen.Bottom/dpi.DpiScaleY-Height);
    }
}
