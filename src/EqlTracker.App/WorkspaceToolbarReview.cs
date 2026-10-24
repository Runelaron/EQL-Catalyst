using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class WorkspaceToolbarReview
{
    internal static async Task Run(TrackerController controller,QuestWindow tracker,DpsWindow lab,OverlayWindow overlay,string folder)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic review only");Directory.CreateDirectory(folder);File.Delete(Path.Combine(folder,"partial-toolbar-checks.json"));
        bool previousMotion=controller.Profile.SmoothMeterMotion;controller.Profile.SmoothMeterMotion=false;
        var checks=new List<string>();var timings=new List<object>();var events=new List<string>();var clock=Stopwatch.StartNew();
        void Check(bool value,string name){if(!value){Json.Write(Path.Combine(folder,"partial-toolbar-checks.json"),new{Checks=checks,Failed=name});throw new InvalidOperationException(name);}checks.Add(name);}
        void Flush(Window w){w.UpdateLayout();w.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);w.UpdateLayout();}
        void Capture(string name,Window window)
        {
            double minW=window.MinWidth,minH=window.MinHeight,w=window.Width,h=window.Height;
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture((FrameworkElement)window.Content,window.ActualWidth,window.ActualHeight,1)));using var output=File.Create(Path.Combine(folder,name+".png"));png.Save(output);
            window.MinWidth=minW;window.MinHeight=minH;window.Width=w;window.Height=h;Flush(window);
        }
        tracker.Placement!.ReviewEnabled=lab.Placement!.ReviewEnabled=true;
        var args=Environment.GetCommandLineArgs();int restore=Array.IndexOf(args,"--toolbar-restore");
        if(restore<0)
        {
            var startupPanel=new FrostWindow(controller,"Startup workspace probe","",700,500){Opacity=0,ShowActivated=false};
            var startup=new WorkspaceToolbarWindow(controller,_=>startupPanel,()=>{},new()){Opacity=0,ShowActivated=false};
            startup.Open("Tracker");Flush(startup);
            Check(startup.State.Placement.ScreenLeft.HasValue&&startup.State.Placement.ScreenTop.HasValue&&startup.State.Placement.Height==28,"First explicit open persists placement after deferred WPF Loaded without another interaction");startup.TryHide();
        }
        var initial=restore<0?new WorkspaceToolbarState():Json.Read<WorkspaceToolbarState>(args[restore+1]);
        var bar=new WorkspaceToolbarWindow(controller,id=>id=="Tracker"?tracker:lab,()=>{},initial){ShowActivated=false,Opacity=0};bar.Show();Flush(bar);
        if(restore>=0){await Task.Delay(100);Flush(bar);bar.SaveState();Json.Write(Path.Combine(folder,"restored.json"),bar.State);bar.TryHide();return;}
        bar.Left=70;bar.Top=40;bar.Width=420;Flush(bar);bar.SaveState();Check(bar.ActualHeight==28&&bar.OuterResize!.Grip.IsVisible,"Toolbar has fixed 28-DIP height and a pointer-enabled resize handle");
        var app=(App)Application.Current;var ready=Usability.Tree(bar).OfType<Button>().Single(b=>Equals(b.Tag,"tracker-update-ready"));Check(!ready.IsVisible,"Unready updates reserve no toolbar button space");double initialMinimum=bar.MinWidth;
        app.Updates.ReviewState(ApplicationUpdateState.Ready);Flush(bar);var actions=(StackPanel)ready.Parent;Check(ready.IsVisible&&actions.Children.IndexOf(ready)==actions.Children.IndexOf(bar.SettingsButton)+1,"Ready-update icon appears immediately to Settings' right");Check(bar.MinWidth>initialMinimum&&bar.ActualHeight==28,"Control-derived minimum width accommodates ready updates");Capture("toolbar-update-ready",bar);app.Updates.ReviewState(ApplicationUpdateState.Idle);Flush(bar);
        string independent=JsonSerializer.Serialize(controller.Profile.OverlayFooterDisplay);string main=JsonSerializer.Serialize(controller.Profile.OverlayMeter);
        Check(bar.Open("Tracker")&&tracker.IsVisible&&tracker.IsAttachedWorkspace&&tracker.Owner==bar,"Tracker is attached to the shared bar");Flush(tracker);Check(!lab.IsVisible&&bar.Buttons["Tracker"].IsChecked==true,"Only the selected workspace is visible and its button is pressed");
        Check(!Usability.Tree(tracker).OfType<FrameworkElement>().Any(e=>e.IsVisible&&Equals(e.Tag,"tracker-settings-footer")),"Settings footer is reclaimed in attached Tracker");
        bool allow=false;var original=tracker.CanHide;tracker.CanHide=()=>allow;
        Check(!bar.Open("CombatLab")&&tracker.IsVisible&&bar.State.Active=="Tracker","Cancel from the existing dirty-workspace guard keeps Tracker open");
        Check(!bar.TryHide()&&bar.IsVisible&&tracker.IsVisible,"Cancel prevents toolbar hide as well as panel switching");
        allow=true;Check(bar.Open("CombatLab")&&lab.IsVisible&&!tracker.IsVisible,"Accepted workspace guard switches persistent panels");tracker.CanHide=original;Flush(lab);
        Check(bar.Open("CombatLab")&&lab.IsVisible,"Explicit open does not toggle the requested panel closed");bar.Open("CombatLab",true);Check(bar.State.Active==""&&!lab.IsVisible&&bar.IsVisible,"Clicking the selected workspace returns to the shared bar");
        var header=Usability.Tree(bar).OfType<DockPanel>().First(p=>p.ContextMenu!=null);
        void Double(UIElement element){var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(e,2);element.RaiseEvent(e);Flush(bar);}
        Double(header);Check(bar.State.Active=="CombatLab"&&lab.IsVisible,"Empty toolbar double-click restores the last workspace");Double(header);Check(bar.State.Active==""&&!lab.IsVisible,"Empty toolbar double-click closes the current workspace");Double(bar.SettingsButton);Check(bar.State.Active=="","Interactive toolbar double-click does not toggle a workspace");
        bar.Open("Tracker");Flush(tracker);tracker.ToggleCollapse();Check(bar.State.Active==""&&!tracker.IsVisible,"Existing Tracker collapse returns to shared bar");bar.Open("CombatLab");Flush(lab);lab.ToggleCollapse();Check(bar.State.Active==""&&!lab.IsVisible,"Existing Combat Lab collapse returns to shared bar");
        var apply=typeof(WindowResizeController).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic)!;
        void Gesture(FrostWindow w,double first,double final,bool cancel=false,bool escape=false)
        {
            var grip=w.OuterResize!.Grip;double before=w.ActualWidth,height=w.ActualHeight;events.Clear();
            void Lost(object? sender,MouseEventArgs e)=>events.Add("LostMouseCapture");void Completed(object? sender,DragCompletedEventArgs e)=>events.Add("DragCompleted:"+e.Canceled);
            grip.LostMouseCapture+=Lost;grip.DragCompleted+=Completed;var watch=Stopwatch.StartNew();
            using(w.Dispatcher.DisableProcessing())
            {
                grip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});Check(grip.IsMouseCaptured&&grip.IsDragging,"Real Thumb mouse-down owns capture");
                var origin=Mouse.GetPosition(grip);var start=grip.PointToScreen(origin);var dpi=VisualTreeHelper.GetDpi(w);
                void Move(double x){var now=grip.PointToScreen(origin);grip.RaiseEvent(new DragDeltaEventArgs((start.X+x*dpi.DpiScaleX-now.X)/dpi.DpiScaleX,20){RoutedEvent=Thumb.DragDeltaEvent});apply.Invoke(w.OuterResize,["review-frame"]);w.UpdateLayout();}
                Move(first);Move(final);
                if(escape)w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});else if(cancel)grip.ReleaseMouseCapture();else grip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
            }
            Flush(w);Check(Math.Abs(w.ActualWidth-(cancel||escape?before:before+final))<1,"Release, reversal or cancellation retains the expected width");
            if(w==bar)Check(w.ActualHeight==28,"Collapsed resize ignores vertical pointer movement");
            if(!cancel&&!escape)Check(events.SequenceEqual(["LostMouseCapture","DragCompleted:False"]),"Normal WPF capture release precedes successful completion");
            Check(!grip.IsDragging&&!grip.IsMouseCaptured,"Gesture releases native/WPF capture");
            var native=JsonSerializer.SerializeToElement(WindowResizeController.Geometry(w,"committed"));Check(Math.Abs(native.GetProperty("NativeWidth").GetDouble()/native.GetProperty("DpiX").GetDouble()-w.ActualWidth)<1,"Native and rendered committed width agree");
            timings.Add(new{Name=w.GetType().Name,Before=before,After=w.ActualWidth,HeightBefore=height,HeightAfter=w.ActualHeight,Cancel=cancel,Escape=escape,Milliseconds=watch.Elapsed.TotalMilliseconds,Events=events.ToArray()});grip.LostMouseCapture-=Lost;grip.DragCompleted-=Completed;
        }
        string expanded=JsonSerializer.Serialize(controller.Profile.Panels);Gesture(bar,60,60);Gesture(bar,40,-25);Gesture(bar,30,30,cancel:true);Gesture(bar,20,20,escape:true);bar.SaveState();
        Check(expanded==JsonSerializer.Serialize(controller.Profile.Panels),"Collapsed resizing never modifies either expanded workspace record");Check(Math.Abs(bar.State.Placement.Width-bar.ActualWidth)<1,"Toolbar width is committed separately");
        bar.IsLocked=true;Flush(bar);Check(!bar.OuterResize!.Grip.IsVisible,"Lock disables toolbar resizing");bar.IsLocked=false;Flush(bar);Check(bar.OuterResize.Grip.IsVisible,"Unlock restores toolbar resizing without a size change");
        bar.Open("Tracker");Flush(tracker);Gesture(tracker,30,30);double trackerWidth=tracker.ActualWidth;Check(Math.Abs(controller.Profile.Panels["Quest & Gear Tracker"].Width-trackerWidth)<1,"Attached resize commits Tracker expanded dimensions");bar.ClosePanel();Gesture(bar,20,20);bar.Open("Tracker");Flush(tracker);Check(Math.Abs(tracker.ActualWidth-trackerWidth)<1,"Reopening restores expanded dimensions independently of toolbar width");
        bar.Open("CombatLab");Flush(lab);Gesture(lab,25,25);Check(Math.Abs(controller.Profile.Panels["Combat Lab"].Width-lab.ActualWidth)<1&&Math.Abs(controller.Profile.Panels["Quest & Gear Tracker"].Width-trackerWidth)<1,"Combat Lab expanded size remains independently saved");
        double offsetX=lab.Left-bar.Left,offsetY=lab.Top-bar.Top;bar.Left+=25;bar.Top+=18;Flush(bar);Check(Math.Abs(lab.Left-bar.Left-offsetX)<1&&Math.Abs(lab.Top-bar.Top-offsetY)<1,"Dragging/repositioning the bar moves its attached workspace");
        bar.WindowState=WindowState.Minimized;Flush(bar);Check(!lab.IsVisible&&!bar.OuterResize.Grip.IsVisible,"Windows minimize hides the attached panel and grip");bar.WindowState=WindowState.Normal;Flush(bar);Check(lab.IsVisible&&bar.OuterResize.Grip.IsVisible,"Windows restore recovers panel and grip");
        bar.TryHide();Flush(bar);Check(!bar.IsVisible&&!lab.IsVisible,"Hide hides the whole workspace group");bar.Open("CombatLab");Flush(bar);Check(lab.IsVisible&&bar.OuterResize.Grip.IsVisible,"Explicit reopen recovers the persistent panel and resize input");
        var button=bar.Buttons["CombatLab"];var context=button.ContextMenu!.Items.OfType<MenuItem>().First();context.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Flush(bar);Check(bar.State.Order[0]=="CombatLab"&&bar.State.Active=="CombatLab","Accessible Move left reorders without changing active workspace");bar.Move("CombatLab",1);Check(bar.State.Order[0]=="Tracker","Workspace reordering reverses without replacing windows");
        var moving=bar.Buttons["Tracker"];var startPoint=moving.TranslatePoint(new(5,5),bar);var destination=button.TranslatePoint(new(button.ActualWidth-3,5),bar);
        bar.BeginReorder("Tracker",startPoint);Check(!bar.ContinueReorder(new(startPoint.X+1,startPoint.Y)),"Sub-threshold pointer motion does not reorder buttons");Check(bar.ContinueReorder(destination)&&bar.State.Order[1]=="Tracker","Pointer reorder follows real arranged button hit bounds");moving.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(bar.State.Active=="CombatLab","Reorder completion suppresses accidental button activation");bar.Move("Tracker",-1);
        bar.BeginReorder("Tracker",moving.TranslatePoint(new(5,5),bar));bar.ContinueReorder(button.TranslatePoint(new(button.ActualWidth-3,5),bar));bar.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(bar)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush(bar);Check(bar.State.Order[0]=="Tracker"&&bar.State.Active=="CombatLab","Escape cancels reorder without activating a workspace");
        double barWidth=bar.Width;var resize=bar.OuterResize.Grip;resize.CaptureMouse();resize.RaiseEvent(new DragStartedEventArgs(5,5){RoutedEvent=Thumb.DragStartedEvent});resize.RaiseEvent(new DragDeltaEventArgs(60,0){RoutedEvent=Thumb.DragDeltaEvent});bar.TryHide();Flush(bar);bar.Open("CombatLab");Flush(bar);Check(Math.Abs(bar.Width-barWidth)<1,"Hiding invalidates pending resize callbacks before reopening");
        Check(independent==JsonSerializer.Serialize(controller.Profile.OverlayFooterDisplay)&&main==JsonSerializer.Serialize(controller.Profile.OverlayMeter),"Toolbar operations preserve independent DPS selectors");
        bar.Open("Tracker");tracker.ShowView(TrackerTab.Pet);Flush(tracker);Capture("toolbar",bar);Capture("attached-tracker",tracker);bar.Open("CombatLab");Flush(lab);Capture("attached-combat",lab);bar.ClosePanel();bar.Width=bar.MinWidth;Flush(bar);Capture("toolbar-minimum",bar);Check(bar.ActualHeight==28&&bar.Buttons.Values.All(b=>b.ActualHeight==24),"Minimum toolbar retains full-height buttons");
        bar.SaveState();var state=EquipmentPositions.Copy(bar.State);state.Order=["CombatLab","Tracker"];state.Active="CombatLab";state.Last="CombatLab";string statePath=Path.Combine(folder,"toolbar-state.json");Json.Write(statePath,state);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")start.ArgumentList.Add(typeof(App).Assembly.Location);
        foreach(string arg in new[]{"--preview",Path.Combine(folder,"restart"),"--workspace-toolbar-only","--toolbar-restore",statePath,"--character","Sample","--server","Test","--offline"})start.ArgumentList.Add(arg);
        using(var child=Process.Start(start)!){await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));Check(child.ExitCode==0,"Separate process restores toolbar state");}
        var restored=Json.Read<WorkspaceToolbarState>(Path.Combine(folder,"restart/restored.json"));Check(restored.Active=="CombatLab"&&restored.Order.SequenceEqual(state.Order)&&Math.Abs(restored.Placement.Width-state.Placement.Width)<1,"Restart retains width, button order and open workspace");
        await WorkspacePresentationReview.Run(controller,bar,tracker,lab,folder,Check,Gesture);controller.Profile.SmoothMeterMotion=previousMotion;
        bar.TryHide();Json.Write(Path.Combine(folder,"workspace-toolbar-checks.json"),new{Passed=checks.Count,Checks=checks,Timings=timings,Milliseconds=clock.Elapsed.TotalMilliseconds,Scope="Synthetic native WPF windows, real Thumb down/up lifecycle with physical input pumping suppressed, capture, native geometry and separate-process restart. Physical monitor/DPI transitions remain untested.",AppSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location)))});
        await overlay.ReviewPetIconCandidates(folder);
    }
}
