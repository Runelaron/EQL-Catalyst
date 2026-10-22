using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace EqlTracker.App;

/// <summary>Live corner resizing for transparent workspaces without a native rubber-band sizing loop.</summary>
internal sealed class WindowResizeController
{
    internal readonly Thumb Grip;
    private readonly FrostWindow window;
    private Point start,origin;
    private double width,height,scaleX,scaleY,pendingWidth,pendingHeight;
    private bool active,queued;
    private long generation;
    internal static Action<object>? Observe;
    [StructLayout(LayoutKind.Sequential)]private struct RectNative{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct PointNative{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MinMaxInfo{public PointNative Reserved,MaximumSize,MaximumPosition,MinimumTrack,MaximumTrack;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr handle,out RectNative rect);
    [DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern IntPtr QueryMinMax(IntPtr handle,int message,IntPtr wp,ref MinMaxInfo info);
    [DllImport("user32.dll")]private static extern int GetSystemMetrics(int index);
    internal static Size Limits(Window window)
    {
        // Windows seeds these defaults before a native sizing operation. A direct
        // WM_GETMINMAXINFO query must supply them too, before WPF applies overrides.
        var info=new MinMaxInfo{MaximumTrack=new(){X=GetSystemMetrics(59),Y=GetSystemMetrics(60)}};QueryMinMax(new WindowInteropHelper(window).Handle,0x0024,IntPtr.Zero,ref info);var dpi=VisualTreeHelper.GetDpi(window);
        return new(Math.Min(window.MaxWidth,info.MaximumTrack.X>0?info.MaximumTrack.X/dpi.DpiScaleX:double.PositiveInfinity),Math.Min(window.MaxHeight,info.MaximumTrack.Y>0?info.MaximumTrack.Y/dpi.DpiScaleY:double.PositiveInfinity));
    }
    internal WindowResizeController(FrostWindow window)
    {
        this.window=window;Grip=new Thumb{Width=14,Height=14,Margin=new(0,-2,-2,0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Cursor=Cursors.SizeNWSE,ToolTip="Resize window",Focusable=true};
        Grip.Template=(ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb"><Border Background="Transparent"><Path Stroke="{DynamicResource Accent}" StrokeThickness="1" Data="M 2,12 L 12,2 M 6,12 L 12,6 M 10,12 L 12,10" Opacity="0.65"/></Border></ControlTemplate>
        """);
        System.Windows.Automation.AutomationProperties.SetName(Grip,"Resize window");Usability.RoutineTooltip(Grip);
        KeyboardNavigation.SetIsTabStop(Grip,false);
        Grip.DragStarted+=(_,e)=>
        {
            if(window.IsLocked||window.IsCollapsed||window.WindowState!=WindowState.Normal){Grip.CancelDrag();return;}
            generation++;queued=false;active=true;origin=new(e.HorizontalOffset,e.VerticalOffset);start=Grip.PointToScreen(origin);var dpi=VisualTreeHelper.GetDpi(window);scaleX=dpi.DpiScaleX;scaleY=dpi.DpiScaleY;
            width=pendingWidth=window.ActualWidth;height=pendingHeight=window.ActualHeight;Record("start");
        };
        Grip.DragDelta+=(_,e)=>
        {
            if(!active)return;
            // Thumb deltas are relative to its moving local origin. Recover the
            // absolute pointer and apply total displacement to the drag snapshot.
            var point=Grip.PointToScreen(new(origin.X+e.HorizontalChange,origin.Y+e.VerticalChange));
            var limits=Limits(window);pendingWidth=Math.Clamp(width+(point.X-start.X)/scaleX,Math.Min(window.MinWidth,limits.Width),limits.Width);pendingHeight=Math.Clamp(height+(point.Y-start.Y)/scaleY,Math.Min(window.MinHeight,limits.Height),limits.Height);
            if(queued)return;queued=true;long revision=generation;
            window.Dispatcher.BeginInvoke(()=>{if(revision==generation){queued=false;if(active)Apply("frame");}},DispatcherPriority.Render);
        };
        Grip.DragCompleted+=(_,e)=>Finish(e.Canceled);
        Grip.LostMouseCapture+=(_,_)=>{if(active)Finish(true);};
        window.AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler((_,e)=>{if(active&&e.Key==Key.Escape){Finish(true);Grip.CancelDrag();e.Handled=true;}}),true);
        window.Deactivated+=(_,_)=>{if(active){Finish(false);Grip.CancelDrag();}};
        window.DpiChanged+=(_,_)=>InterruptForDpi();
        window.IsVisibleChanged+=(_,_)=>{if(!window.IsVisible&&active){Finish(false);Grip.CancelDrag();}};
        window.SizeChanged+=(_,_)=>Grip.Visibility=window.IsCollapsed||window.ResizeMode==ResizeMode.NoResize?Visibility.Collapsed:Visibility.Visible;
    }
    private void Apply(string phase)
    {
        var watch=Stopwatch.StartNew();window.Width=pendingWidth;window.Height=pendingHeight;
        Observe?.Invoke(new{Phase=phase,RequestedWidth=pendingWidth,RequestedHeight=pendingHeight,Milliseconds=watch.Elapsed.TotalMilliseconds});
    }
    private void Finish(bool cancel)
    {
        if(!active)return;generation++;queued=false;active=false;
        if(cancel){pendingWidth=width;pendingHeight=height;}Apply(cancel?"cancel":"commit");Record(cancel?"cancelled":"released");
    }
    internal void InterruptForDpi()
    { if(!active)return;generation++;queued=false;active=false;Grip.CancelDrag();Record("dpi-interrupted"); }
    private void Record(string phase)=>Observe?.Invoke(Geometry(window,phase));
    internal static object Geometry(Window window,string phase)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle,out var rect);var dpi=VisualTreeHelper.GetDpi(window);var surface=window.Content as FrameworkElement;
        return new{Phase=phase,window.Width,window.Height,window.ActualWidth,window.ActualHeight,NativeWidth=rect.Right-rect.Left,NativeHeight=rect.Bottom-rect.Top,DpiX=dpi.DpiScaleX,DpiY=dpi.DpiScaleY,SurfaceWidth=surface?.ActualWidth,SurfaceHeight=surface?.ActualHeight};
    }
}
