using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private async Task ReviewProgressActions(string folder,List<string> checks,List<object> timings,Action<string,double,double> capture)
    {
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
        void Flush(){Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        string[] Texts(DependencyObject root)=>Usability.Tree(root).OfType<TextBlock>().Select(t=>t.Text).ToArray();
        void Click(Button button){button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});Flush();}
        ShowView(TrackerTab.Progress,"Bard Epic Quest");Flush();
        Check(!Usability.Tree(Body).OfType<Expander>().Any(e=>Equals(e.Header,"Quest options")),"Quest options no longer reserves a row above the tree");
        Check(!progressOpen.IsVisible&&progressOptional.IsCheckable&&progressOptional.IsEnabled,"Optional steps replace the route strip and toolbar walkthrough");
        bool optional=progressOptional.IsChecked;progressOptional.IsChecked=!optional;progressOptional.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Flush();Check(Controller.Profile.Routes[ProgressQuest!.Id]==(!optional?"Alternative":"Default"),"More optional-step toggle changes the selected quest's existing route preference");progressOptional.IsChecked=optional;progressOptional.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Flush();
        Click(progressCollapse);Check(progressNodes.SelectMany(n=>n.Descendants()).Where(n=>n.Children.Count>0).All(n=>!n.IsExpanded)&&Equals(progressCollapse.Content,"Expand all"),"Collapse all collapses branches and offers Expand all");Click(progressCollapse);Flush();
        Check(Equals(progressCollapse.Content,"Collapse all")&&progressCollapse.TranslatePoint(new(progressCollapse.ActualWidth,0),progressMore.Parent as UIElement).X<=progressMore.TranslatePoint(new(),progressMore.Parent as UIElement).X,"Expand all restores branches and its button sits before More");
        var originalQueue=EquipmentPositions.Copy(Controller.Profile.GoalQueue);Controller.Profile.GoalQueue.Clear();RefreshQueueActions();
        SelectProgressNode(n=>n.Kind==QuestNodeKind.Quest);Flush();
        var queue=Usability.Tree(howTo).OfType<Button>().Single(b=>Equals(b.Content,"Queue"));var walkthrough=Usability.Tree(howTo).OfType<Button>().Single(b=>Equals(b.Content,"Open walkthrough"));
        Check(queue.Parent==walkthrough.Parent&&queue.Parent is WrapPanel,"Queue and walkthrough share one wrapping action row");
        Check(Usability.HasTooltip(queue)&&ToolTipService.GetIsEnabled(queue)&&ToolTipService.GetInitialShowDelay(queue)==EqlTracker.Core.TooltipPreferences.Delay(Controller.Profile.TooltipDelayMilliseconds),"Queue tooltip covers its full button and respects delay/Essential preferences");
        Click(queue);queue=Usability.Tree(howTo).OfType<Button>().Single(b=>Equals(b.Content,"Queue"));Check(Usability.GetIsPressed(queue)&&Controller.Profile.GoalQueue.Count==1,"Queue adds one exact goal and remains pressed");
        bool tracked=QuestTracking.Selected(ProgressQuest!,Controller.Profile,Controller.Inventory,Controller.Catalog);Click(queue);queue=Usability.Tree(howTo).OfType<Button>().Single(b=>Equals(b.Content,"Queue"));Check(!Usability.GetIsPressed(queue)&&Controller.Profile.GoalQueue.Count==0&&QuestTracking.Selected(ProgressQuest!,Controller.Profile,Controller.Inventory,Controller.Catalog)==tracked,"Queue removes only its pin and retains tracking/completion");
        ShowView(TrackerTab.Progress,"Warrior Epic Quest");SelectProgressNode(n=>n.Kind==QuestNodeKind.Quest);Flush();Check(!Usability.Tree(howTo).OfType<Button>().Any(b=>Equals(b.Content,"Open item wiki")),"Multi-reward Warrior root does not open an arbitrary item wiki");
        SelectProgressNode(n=>n.Kind!=QuestNodeKind.Quest&&n.ItemKeys.Count==1&&n.ItemKeys[0]=="unjeweled dragon head hilt");Flush();
        var hiltCards=howTo.Children.OfType<Border>().Where(b=>b.Tag is ItemSource).ToArray();Check(hiltCards.Length==1&&Texts(hiltCards[0]).Count(t=>t.StartsWith("Coordinates:"))==1&&Texts(hiltCards[0]).Contains("Under the aviak tower"),"Hilt renders one location card with coordinates once and its landmark retained");
        Check(Usability.Tree(howTo).OfType<Button>().Count(b=>Equals(b.Content,"Open item wiki"))==1,"Single-component detail retains its correctly scoped item-wiki action");
        var gem=new ItemDefinition{Key="review:gem",Name="Sapphire sample",Sources=[new(){Zone="Chardok",Mob="a Di’Zok Cryptmaster"},new(){Zone="Chardok",Mob="A Di'Zok Cryptmaster",Notes="Second source record"},new(){Zone="Chardok",Mob="a reanimated berserker",Notes="Only at night"},new(){Zone="Freeport",Mob="Merchant Alpha",Method="Vendor"},new(){Zone="Freeport",Mob="Merchant Beta",Method="Sold by"}]};
        var sources=QuestPresentationView.Sources(ProgressPresentation.SourcesFor([gem.Key],new(){Items=[gem]})).ToArray();
        Check(sources.Length==2&&sources.All(b=>Usability.Tree(b).OfType<ComboBox>().Single().Items.Count==2),"Multi-mob and vendor records share two compact zone cards with deduplicated actors");
        var select=Usability.Tree(sources[0]).OfType<ComboBox>().Single();select.SelectedIndex=1;Check(Texts(sources[0]).Contains("Only at night"),"Selecting a mob shows that actor's documented condition");
        var focused=new StackPanel();AddFocusedSources(focused,gem.Sources,gem);Check(Usability.Tree(focused).OfType<ComboBox>().Count()==2,"Queue, quest and effect source panes share the compact acquisition renderer");
        var acquisition=new ItemAcquisitionView();acquisition.Show(gem,new(){Items=[gem]},new());Check(Usability.Tree(acquisition).OfType<ComboBox>().Count()==2,"Item acquisition surfaces reuse the same zone/mob/vendor presentation");
        gem.Sources[0].Notes="Newly documented condition";acquisition.Show(gem,new(){Items=[gem]},new());Check(Texts(acquisition).Contains("Newly documented condition"),"Late source conditions refresh item acquisition without requiring a name or zone change");
        howTo.Children.Add(acquisition);Flush();Check(Texts(acquisition).Contains("a Di’Zok Cryptmaster")&&Texts(acquisition).Contains("Merchant Alpha"),"Rendered source selectors show NPC/vendor names through real WPF bindings");howTo.Children.Remove(acquisition);gem.Sources[0].Notes="";
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Width=1220;Height=800;Flush();capture(theme+"-hilt-actions",1220,800);capture(theme+"-hilt-actions-narrow",640,670);
            if(!Environment.GetCommandLineArgs().Contains("--checks-only"))
            {
                var samples=UI.Row(RewardStyleSamples.Create("Raised Lettering"),RewardStyleSamples.Create("Inset Plaque"),RewardStyleSamples.Create("Engraved Text"));var board=UI.Card(samples);board.Background=UI.Brush("Surface");
                foreach(double scale in new[]{1d,1.5,2d,3d})
                {
                    var watch=Stopwatch.StartNew();var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(board,810,390,scale)));using var file=File.Create(Path.Combine(folder,"qa",theme+"-reward-options-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));png.Save(file);timings.Add(new{Name=theme+"-reward-options",Scale=scale,Milliseconds=watch.Elapsed.TotalMilliseconds,Bytes=0});
                }
                var sourceBoard=UI.Card(UI.Stack(QuestPresentationView.Sources(ProgressPresentation.SourcesFor([gem.Key],new(){Items=[gem]})).ToArray()));
                foreach(double scale in new[]{1d,1.5,2d,3d}){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(sourceBoard,360,500,scale)));using var file=File.Create(Path.Combine(folder,"qa",theme+"-zone-sources-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));png.Save(file);}
            }
        }
        Controller.Profile.GoalQueue=originalQueue;Controller.Save();
        // Exercise the actual outer grip's routed drag handlers, with native HWND bounds.
        Width=1220;Height=800;Flush();var grip=OuterResize!.Grip;var resize=new List<object>();WindowResizeController.Observe=resize.Add;
        int nativeSizingLoops=0;HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WatchNative);
        IntPtr WatchNative(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled){if(message==0x0231)nativeSizingLoops++;return IntPtr.Zero;}
        void Begin(){grip.RaiseEvent(new DragStartedEventArgs(5,5){RoutedEvent=Thumb.DragStartedEvent});}
        Point origin=new();double initialWidth=0,initialHeight=0;
        void Start(){Flush();initialWidth=ActualWidth;initialHeight=ActualHeight;origin=grip.PointToScreen(new(5,5));Begin();}
        void Move(double x,double y,bool flush=true){var dpi=VisualTreeHelper.GetDpi(this);var now=grip.PointToScreen(new(5,5));grip.RaiseEvent(new DragDeltaEventArgs((origin.X+x*dpi.DpiScaleX-now.X)/dpi.DpiScaleX,(origin.Y+y*dpi.DpiScaleY-now.Y)/dpi.DpiScaleY){RoutedEvent=Thumb.DragDeltaEvent});if(flush)Flush();}
        void End(bool cancel=false){grip.RaiseEvent(new DragCompletedEventArgs(0,0,cancel){RoutedEvent=Thumb.DragCompletedEvent});Flush();}
        var limits=WindowResizeController.Limits(this);Start();Move(100,60,false);Move(140,90,false);Flush();Json.Write(Path.Combine(folder,"resize-first-frame.json"),new{initialWidth,initialHeight,Events=resize,Current=WindowResizeController.Geometry(this,"first-frame")});Check(Math.Abs(ActualWidth-Math.Min(initialWidth+140,limits.Width))<1&&Math.Abs(ActualHeight-Math.Min(initialHeight+90,limits.Height))<1,"Outer resize coalesces total pointer displacement without double-counting unrendered deltas");Move(65,30);End();Check(Math.Abs(ActualWidth-Math.Min(initialWidth+65,limits.Width))<1&&Math.Abs(ActualHeight-Math.Min(initialHeight+30,limits.Height))<1,"Outer resize reverses and retains its drawn dimensions after release");
        var geometry=JsonSerializer.SerializeToElement(WindowResizeController.Geometry(this,"settled"));Check(Math.Abs(geometry.GetProperty("NativeWidth").GetDouble()/geometry.GetProperty("DpiX").GetDouble()-ActualWidth)<2&&Math.Abs(((FrameworkElement)Content).ActualWidth-ActualWidth)<1,"Committed native, WPF and rendered surface widths agree");
        Start();Move(180,100);End(true);Check(Math.Abs(ActualWidth-initialWidth)<1&&Math.Abs(ActualHeight-initialHeight)<1,"Cancelled outer resize restores the drag snapshot");
        Start();Move(-30,-20);RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush();Check(Math.Abs(ActualWidth-initialWidth)<1,"Escape cancels the outer grip and restores its snapshot");
        Start();Move(-25,-15);grip.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.LostMouseCaptureEvent});Flush();Check(Math.Abs(ActualWidth-initialWidth)<1,"Capture loss clears outer drag state and queued resize work");
        Start();Move(-20,-10);double interruptedWidth=ActualWidth;OuterResize.InterruptForDpi();Move(90,90);Flush();Check(Math.Abs(ActualWidth-interruptedWidth)<1,"A DPI transition rejects stale drag coordinates instead of applying old-monitor deltas");
        Start();Move(-5000,-5000);End();Check(ActualWidth>=MinWidth&&ActualHeight>=MinHeight,"Outer resize respects existing minimum dimensions");
        Width=1220;Height=800;Flush();Start();Move(1100,500);End();await Task.Delay(100);Flush();resize.Add(WindowResizeController.Geometry(this,"large-settled"));Check(Math.Abs(ActualWidth-Math.Min(initialWidth+1100,limits.Width))<2&&Math.Abs(ActualHeight-Math.Min(initialHeight+500,limits.Height))<2&&Math.Abs(Width-ActualWidth)<2&&Math.Abs(Height-ActualHeight)<2,"Large valid outer dimensions stay committed without snap-back");
        Check(nativeSizingLoops==0&&((SolidColorBrush)((Border)Content).Background).Color.A<255&&Opacity==1,"Managed outer grip avoids native sizing preview and preserves themed transparency");
        WindowResizeController.Observe=null;HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).RemoveHook(WatchNative);Json.Write(Path.Combine(folder,"outer-resize.json"),new{Events=resize,NativeSizingLoops=nativeSizingLoops,Scope="Synthetic offscreen HWND, actual outer Thumb routed events and native rectangle checks; physical monitor transitions/live-game visuals remain separate."});
        foreach(var extra in new FrostWindow[]{new DpsWindow(Controller),new FrostWindow(Controller,"Solid resize check","",700,500)})
        {
            extra.Left=extra.Top=-15000;extra.Width=700;extra.Height=500;extra.ShowActivated=false;extra.Show();extra.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);var otherGrip=extra.OuterResize!.Grip;double before=extra.ActualWidth;
            otherGrip.RaiseEvent(new DragStartedEventArgs(5,5){RoutedEvent=Thumb.DragStartedEvent});otherGrip.RaiseEvent(new DragDeltaEventArgs(20,10){RoutedEvent=Thumb.DragDeltaEvent});otherGrip.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);extra.UpdateLayout();
            Check(Math.Abs(extra.ActualWidth-before-20)<1,"Shared outer resize works in "+(extra is DpsWindow?"Combat Lab":"solid companion windows"));Check(((SolidColorBrush)((Border)extra.Content).Background).Color.A==(extra.SurfaceStyle==WindowSurface.Solid?255:UI.Brush("Surface").Color.A),"Shared resizing retains the window's original surface policy");extra.Hide();
        }
        Width=1220;Height=800;ShowView(TrackerTab.Progress,"Bard Epic Quest");Flush();
    }
}
