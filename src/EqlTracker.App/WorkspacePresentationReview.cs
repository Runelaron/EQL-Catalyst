using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class WorkspacePresentationReview
{
    internal static async Task Run(TrackerController controller,WorkspaceToolbarWindow bar,QuestWindow tracker,DpsWindow lab,string folder,Action<bool,string> check,Action<FrostWindow,double,double,bool,bool> gesture)
    {
        void Flush(){bar.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);bar.UpdateLayout();tracker.UpdateLayout();lab.UpdateLayout();}
        void Capture(string name,FrameworkElement visual,double width,double height)
        {var image=PreviewRenderer.Capture(visual,width,height,1);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(Path.Combine(folder,name+".png"));encoder.Save(stream);}
        check(bar.SurfaceStyle==WindowSurface.Theme&&bar.WorkspaceSurface.Background==tracker.WorkspaceSurface.Background,"Toolbar and Tracker use the same translucent theme surface");
        check(bar.WorkspaceSurface.Background is SolidColorBrush brush&&brush.Color.A<255&&bar.WorkspaceSurface.Opacity==1,"Translucency belongs to the surface brush; toolbar artwork stays opaque");
        bar.Open("Tracker");Flush();double barWidth=bar.Width;
        check(tracker.WorkspaceSurface.Padding.Top==5&&Equals(tracker.HeadingTitle.ToolTip,"EQL Catalyst · Tracker"),"Attached branded title begins six DIP from the joined native edge");
        check(!Usability.Tree(tracker).OfType<TextBlock>().Any(t=>t.IsVisible&&t.ToolTip is string s&&s=="Drag window"),"Obsolete attached-panel drag grip is hidden");
        check(bar.WorkspaceSurface.Joined&&tracker.WorkspaceSurface.Joined&&bar.WorkspaceSurface.JoinTop!=tracker.WorkspaceSurface.JoinTop,"Unequal native windows share opposite unframed join spans");
        check(Math.Abs(bar.Width-barWidth)<.1&&tracker.Width>bar.Width,"Joining does not stretch the independently saved toolbar width");
        // Native-size visual proof combines the two real content roots over a striped backing.
        void JoinedCapture(string name,bool above)
        {
            double w=Math.Max(bar.ActualWidth,tracker.ActualWidth),h=bar.ActualHeight+tracker.ActualHeight;
            var scene=new Grid{Width=w,Height=h,Background=new DrawingBrush(new GeometryDrawing(Brushes.DimGray,new Pen(Brushes.Gray,8),Geometry.Parse("M0 0L16 16 M0 16L16 0"))){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new(0,0,32,32)}};
            var p=new Border{Width=tracker.ActualWidth,Height=tracker.ActualHeight,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,above?0:28,0,0),Background=new VisualBrush((Visual)tracker.Content){Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top}};
            var b=new Border{Width=bar.ActualWidth,Height=28,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new(0,above?tracker.ActualHeight:0,0,0),Background=new VisualBrush((Visual)bar.Content){Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top}};
            scene.Children.Add(p);scene.Children.Add(b);Capture(name,scene,w,h);
        }
        JoinedCapture("joined-below",false);
        // Explicit native repositioning exercises the actual above/below attachment branch.
        var display=PlanningWindowPlacement.AvailableDisplays(bar).First();double oldTop=bar.Top;
        bar.Top=(display.Work.Bottom-30*display.DpiY)/display.DpiY;Flush();bar.ArrangePanel();Flush();
        check(tracker.WorkspaceMotion!.Above&&bar.WorkspaceSurface.JoinTop&&!tracker.WorkspaceSurface.JoinTop,"Panel opens above a near-bottom toolbar and reverses its joined edges");JoinedCapture("joined-above",true);
        bar.Top=oldTop;Flush();bar.ArrangePanel();
        // Narrower panel than toolbar exposes only the remaining outer toolbar edge.
        double narrowBar=bar.Width;bar.Width=tracker.ActualWidth+40;Flush();
        check(bar.WorkspaceSurface.JoinEnd<bar.ActualWidth-10&&tracker.WorkspaceSurface.JoinEnd>=tracker.ActualWidth-1,"Wider toolbar retains its exposed outer edge beyond the panel");JoinedCapture("joined-wide-toolbar",false);bar.Width=narrowBar;Flush();
        var saved=new Size(tracker.Width,tracker.Height);var measured=new List<object>();
        controller.Profile.SmoothMeterMotion=true;bar.ClosePanel();bar.SnapTransitions(true);bar.Open("Tracker");
        var watch=Stopwatch.StartNew();await Task.Delay(35);Flush();double opening=tracker.WorkspaceMotion.Progress;
        measured.Add(new{Phase="Opening",Milliseconds=watch.Elapsed.TotalMilliseconds,Progress=opening});
        if(SystemParameters.ClientAreaAnimation)
        {
            check(opening>0&&opening<1&&bar.IsTransitioning,"Opening progresses through a visual fade/slide without layout animation");
            bar.ClosePanel();await Task.Delay(12);double closing=tracker.WorkspaceMotion.Progress;bar.Open("Tracker");
            check(tracker.WorkspaceMotion.Progress>=0&&tracker.WorkspaceMotion.Progress<=1,"Reversal resumes the currently rendered progress");
            measured.Add(new{Phase="Reversed",Progress=closing});await Task.Delay(170);Flush();
            check(tracker.IsVisible&&!bar.IsTransitioning&&tracker.WorkspaceMotion.Progress==1,"Stale closing callback cannot hide a reopened workspace");
        }
        else check(!bar.IsTransitioning&&tracker.WorkspaceMotion.Progress==1,"Windows reduced-motion preference snaps presentation");
        check(tracker.Width==saved.Width&&tracker.Height==saved.Height,"Visual transitions preserve native workspace dimensions");
        bar.ClosePanel();bar.SnapTransitions(true);bar.Open("Tracker");await Task.Delay(25);gesture(tracker,18,18,false,false);Flush();
        check(!bar.IsTransitioning&&tracker.WorkspaceMotion.Progress==1,"Real Thumb resize during opening snaps without losing capture or rolling back committed bounds");
        tracker.HeaderActions.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        check(bar.State.Active==""&&(!SystemParameters.ClientAreaAnimation||tracker.WorkspaceMotion.Running),"Attached header close uses the guarded closing transition");await Task.Delay(120);Flush();
        check(!tracker.IsVisible&&!bar.IsTransitioning,"Attached header close completes at the shared toolbar");bar.Open("Tracker");await Task.Delay(150);Flush();
        bar.Open("CombatLab");check(!tracker.IsVisible||!lab.IsVisible,"Switch closes outgoing native panel before showing incoming");bar.Open("Tracker");bar.Open("CombatLab");await Task.Delay(260);Flush();
        check(bar.State.Active=="CombatLab"&&lab.IsVisible&&!tracker.IsVisible&&!bar.IsTransitioning,"Rapid switching resolves to latest intent with exactly one workspace visible");
        bool blocked=false;var guard=lab.CanHide;lab.CanHide=()=>blocked;
        double progress=lab.WorkspaceMotion!.Progress;
        check(!bar.Open("Tracker")&&lab.IsVisible&&lab.WorkspaceMotion.Progress==progress&&!bar.IsTransitioning,"Dirty-work cancellation occurs before any presentation change");lab.CanHide=guard;
        bar.ClosePanel();bar.Open("CombatLab");bar.InteractionStarting!();Flush();check(!bar.IsTransitioning&&lab.IsVisible&&lab.WorkspaceMotion.Progress==1,"Resize/drag entry snaps presentation before capturing geometry");
        bar.ClosePanel();bar.Open("Tracker");bar.WindowState=WindowState.Minimized;Flush();await Task.Delay(150);bar.WindowState=WindowState.Normal;Flush();
        Json.Write(Path.Combine(folder,"minimize-motion-state.json"),new{bar.State.Active,BarVisible=bar.IsVisible,BarState=bar.WindowState.ToString(),TrackerVisible=tracker.IsVisible,TrackerState=tracker.WindowState.ToString(),LabVisible=lab.IsVisible,LabState=lab.WindowState.ToString(),bar.IsTransitioning,Progress=tracker.WorkspaceMotion.Progress,LabProgress=lab.WorkspaceMotion.Progress});
        check(tracker.IsVisible&&!lab.IsVisible&&!bar.IsTransitioning&&tracker.WorkspaceMotion.Progress==1,"Minimize/restore invalidates old animation completions");
        bar.ClosePanel();bar.Open("CombatLab");bar.Hide();Flush();await Task.Delay(150);bar.Show();Flush();check(lab.IsVisible&&!tracker.IsVisible&&!bar.IsTransitioning,"Temporary focus visibility hiding snaps and restores the latest panel");
        bar.ClosePanel();bar.Open("CombatLab");controller.Profile.SmoothMeterMotion=false;bar.ApplyMotionPreference();check(lab.IsVisible&&!bar.IsTransitioning,"Disabling app motion snaps an in-flight transition");bar.ClosePanel();check(!lab.IsVisible&&!bar.IsTransitioning,"App reduced-motion preference closes immediately");bar.Open("Tracker");check(tracker.IsVisible&&tracker.WorkspaceMotion.Progress==1&&!bar.IsTransitioning,"App reduced-motion preference opens immediately");
        // Rendering policy is based on DIP size, independent of physical raster decode size.
        check(!MonsterArtwork.Detailed(24,Core.IconArtworkStyle.Vibrant)&&MonsterArtwork.Detailed(32,Core.IconArtworkStyle.Vibrant)&&!MonsterArtwork.Detailed(64,Core.IconArtworkStyle.Clean),"Approved Monster uses its selected simplified treatment through 24 DIP and all Clean sizes");
        check(IconArtwork.Painted("pet-monster",192)!=null&&IconArtwork.ActorRole("Pet","Monster")=="pet-monster"&&IconArtwork.ActorRole("Pet","Unknown")=="pet-generic","Approved Monster uses shared painted routing and keeps unresolved generic fallback");
        Json.Write(Path.Combine(folder,"presentation-timings.json"),new{Samples=measured,WindowsAnimations=SystemParameters.ClientAreaAnimation,OpenMilliseconds=120,CloseMilliseconds=90,SlideDIP=4});
    }
}
