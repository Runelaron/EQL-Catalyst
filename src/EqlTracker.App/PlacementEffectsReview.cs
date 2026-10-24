using System.Diagnostics;
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

public sealed partial class QuestWindow
{
    internal async Task ReviewPlacementEffects(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));
        File.Delete(Path.Combine(folder,"placement-effect-checks.json"));
        void Flush(Window? w = null) { (w ?? this).UpdateLayout(); Dispatcher.Invoke(() => { },DispatcherPriority.ApplicationIdle); (w ?? this).UpdateLayout(); }
        if (Environment.GetCommandLineArgs().Contains("--placement-restore"))
        { await Task.Delay(100); Flush(); Json.Write(Path.Combine(folder,"restored.json"),new { IsLoaded, IsCollapsed, State=Controller.Profile.Panels["Quest & Gear Tracker"], Geometry=WindowResizeController.Geometry(this,"child-restored") }); return; }
        var checks=new List<string>(); var timings=new List<object>(); var drags=new List<object>();
        void Check(bool ok,string name) { if(!ok) { Json.Write(Path.Combine(folder,"partial-checks.json"),new{Passed=checks.Count,Checks=checks,Failed=name}); throw new InvalidOperationException(name); } checks.Add(name); }
        void Capture(string name,Window w,FrameworkElement? content=null,double width=0,double height=0)
        {
            foreach(double scale in new[]{1d,1.5,2d,3d})
            {
                var watch=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();var encoder=new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content??(FrameworkElement)w.Content,width>0?width:w.Width,height>0?height:w.Height,scale)));
                using var stream=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));encoder.Save(stream);
                timings.Add(new{Name=name,Scale=scale,Milliseconds=watch.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-allocated});
            }
        }
        Opacity=0; Placement!.ReviewEnabled=true; Width=900;Height=600;Flush();
        var grip=OuterResize!.Grip;var events=new List<string>();
        grip.LostMouseCapture+=(_,_)=>events.Add("LostMouseCapture");grip.DragCompleted+=(_,e)=>events.Add("DragCompleted:"+e.Canceled);
        var apply=typeof(WindowResizeController).GetMethod("Apply",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        void Gesture(double first,double final,bool cancel=false,bool escape=false)
        {
            events.Clear();double before=ActualWidth;double drawn;var watch=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
            // Real Thumb down/up class handlers own capture and completion. Do not
            // pump physical released-button reports during a synthetic held gesture.
            using(Dispatcher.DisableProcessing())
            {
                grip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});
                Check(grip.IsDragging&&grip.IsMouseCaptured,"Actual Thumb down captures pointer");
                var origin=Mouse.GetPosition(grip);var start=grip.PointToScreen(origin);var dpi=VisualTreeHelper.GetDpi(this);
                void Move(double x) { var now=grip.PointToScreen(origin);grip.RaiseEvent(new DragDeltaEventArgs((start.X+x*dpi.DpiScaleX-now.X)/dpi.DpiScaleX,0){RoutedEvent=Thumb.DragDeltaEvent});apply.Invoke(OuterResize,["review-frame"]);UpdateLayout(); }
                Move(first);Move(final);drawn=ActualWidth;
                if(escape)RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                else if(cancel)grip.ReleaseMouseCapture();
                else grip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
                UpdateLayout();
            }
            Flush();double expected=cancel||escape?before:before+final;
            Check(Math.Abs(ActualWidth-expected)<1,"Natural release/cancel preserves width: expected "+expected+" actual "+ActualWidth);
            Check(!grip.IsDragging&&!grip.IsMouseCaptured,"Thumb state cleared after release");
            if(!cancel&&!escape)Check(events.SequenceEqual(new[]{"LostMouseCapture","DragCompleted:False"}),"Capture loss precedes successful completion without rollback");
            var geometry=JsonSerializer.SerializeToElement(WindowResizeController.Geometry(this,"released"));
            Check(Math.Abs(geometry.GetProperty("NativeWidth").GetDouble()/geometry.GetProperty("DpiX").GetDouble()-ActualWidth)<1&&Math.Abs(((FrameworkElement)Content).ActualWidth-ActualWidth)<1,"Native, committed and rendered widths agree");
            Check(Math.Abs(Controller.Profile.Panels["Quest & Gear Tracker"].Width-ActualWidth)<1,"Completed width is saved");
            drags.Add(new{Before=before,Drawn=drawn,Released=ActualWidth,Cancel=cancel,Escape=escape,Events=events.ToArray(),Milliseconds=watch.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-allocated});
        }
        Gesture(60,60);Gesture(40,-25);Gesture(30,30,true);Gesture(20,20,escape:true);
        double retained=Width;Hide();Show();Flush();Check(Math.Abs(Width-retained)<1&&grip.IsVisible,"Hide/reopen keeps committed size and grip");
        ToggleCollapse();Flush();Check(Controller.Profile.Panels["Quest & Gear Tracker"].Height>=420,"Collapsed title bar never replaces expanded saved height");ToggleCollapse();Flush();
        var prior=EquipmentPositions.Copy(Controller.Profile.Panels["Quest & Gear Tracker"]);WindowState=WindowState.Minimized;Placement.Commit();WindowState=WindowState.Normal;Flush();Check(Controller.Profile.Panels["Quest & Gear Tracker"].Height==prior.Height,"Minimized geometry is not persisted");
        Width=940;Height=600;Left=70;Top=50;Flush();Placement.Commit();var saved=EquipmentPositions.Copy(Controller.Profile.Panels["Quest & Gear Tracker"]);
        Check(saved.ScreenLeft.HasValue&&saved.ScreenTop.HasValue&&saved.DpiX>0&&saved.MonitorDevice.Length>0,"Saved normal bounds include position and monitor/DPI context");
        string stateFile=Path.Combine(folder,"restart-state.json");Json.Write(stateFile,Controller.Profile.Panels);
        var process=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))process.ArgumentList.Add(typeof(App).Assembly.Location);
        foreach(string arg in new[]{"--preview",Path.Combine(folder,"restart"),"--placement-effects-only","--placement-restore",stateFile,"--character","Sample","--server","Test","--offline"})process.ArgumentList.Add(arg);
        using(var child=Process.Start(process)!) { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));Check(child.ExitCode==0,"Separate process restores serialized placement"); }
        var restored=JsonSerializer.SerializeToElement(Json.Read<object>(Path.Combine(folder,"restart/restored.json"))).GetProperty("Geometry");
        Check(Math.Abs(restored.GetProperty("ActualWidth").GetDouble()-saved.Width)<1&&Math.Abs(restored.GetProperty("ActualHeight").GetDouble()-saved.Height)<1,"Restart restores saved dimensions");
        Check(Math.Abs(restored.GetProperty("NativeLeft").GetDouble()-saved.ScreenLeft!.Value)<1&&Math.Abs(restored.GetProperty("NativeTop").GetDouble()-saved.ScreenTop!.Value)<1,"Restart restores exact saved physical position");
        Controller.Profile.Panels["Quest & Gear Tracker"].Collapsed=true;Json.Write(stateFile,Controller.Profile.Panels);
        using(var child=Process.Start(process)!) { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));Check(child.ExitCode==0,"Separate process restores a collapsed window"); }
        var collapsed=JsonSerializer.SerializeToElement(Json.Read<object>(Path.Combine(folder,"restart/restored.json")));
        Check(collapsed.GetProperty("Geometry").GetProperty("ActualHeight").GetDouble()==28&&collapsed.GetProperty("State").GetProperty("Height").GetDouble()==saved.Height,"Collapsed restart retains expanded height");
        Controller.Profile.Panels["Quest & Gear Tracker"].Collapsed=false;
        var lab=new DpsWindow(Controller){Opacity=0,ShowActivated=false};Check(lab.Width==878,"Combat Lab unsaved width is 878 DIP");lab.Placement!.ReviewEnabled=true;lab.Left=lab.Top=-15000;lab.Show();Flush(lab);lab.Width=850;lab.Height=550;lab.Placement.Commit();Check(Controller.Profile.Panels["Combat Lab"].Width==850&&Controller.Profile.Panels["Quest & Gear Tracker"].Width==saved.Width,"Main windows save independently");lab.Hide();
        var defaultTracker=new QuestWindow(Controller){Opacity=0};Check(defaultTracker.Width==915,"Tracker unsaved width is 915 DIP");
        var displays=new[]{new PlanningDisplay("A",new(0,0,1920,1080),1,1),new PlanningDisplay("B",new(-2560,0,2560,1440),1.5,1.5)};
        var bounds=PlanningWindowPlacement.Resolve(new(){Width=1000,Height=700,ScreenLeft=120,ScreenTop=90,MonitorDevice="A",DpiX=1,DpiY=1},915,800,640,420,displays);
        Check(bounds==new PlanningBounds(120,90,1000,700),"Valid saved geometry beats new defaults");
        var negative=PlanningWindowPlacement.Resolve(new(){Width=900,Height=600,ScreenLeft=-2200,ScreenTop=120,MonitorDevice="B",MonitorLeft=-2560,DpiX=1.5,DpiY=1.5},915,800,640,420,displays);Check(negative.Left==-2200&&negative.Top==120,"Negative virtual-screen coordinates remain valid");
        var scaled=PlanningWindowPlacement.Resolve(new(){Width=900,Height=600,ScreenLeft=-2200,ScreenTop=120,MonitorDevice="B",MonitorLeft=-2560,DpiX=1.5,DpiY=1.5},915,800,640,420,[new("B",new(-2560,0,3840,2160),2,2)]);Check(scaled.Left==-2080&&scaled.Top==160&&scaled.Width==900,"DPI changes preserve relative placement and logical dimensions");
        var legacy=PlanningWindowPlacement.Resolve(new(){Width=1100,Height=650},915,800,640,420,displays);Check(legacy.Width==1100&&legacy.Height==650&&double.IsFinite(legacy.Left),"Legacy sizes restore with centered placement");
        var invalid=PlanningWindowPlacement.Resolve(new(){Width=double.NaN,Height=-1,ScreenLeft=double.PositiveInfinity},915,800,640,420,displays);Check(invalid.Width==915&&invalid.Height==800&&double.IsFinite(invalid.Left),"Invalid stored fields use safe defaults");
        foreach(double dpi in new[]{1d,1.5,2d,3d})
        {
            var recovered=PlanningWindowPlacement.Resolve(new(){Width=1500,Height=1000,ScreenLeft=-6000,ScreenTop=5000,MonitorDevice="Removed",MonitorLeft=-8000,DpiX=1,DpiY=1},915,800,640,420,[new("Available",new(0,0,3840,2160),dpi,dpi)]);
            Check(recovered.Left+recovered.Width*dpi>=64*dpi&&recovered.Left<=3840-64*dpi&&recovered.Top>=0&&recovered.Top<=2160-28*dpi,"Removed-monitor recovery keeps title bar reachable at "+dpi);
        }
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","BRD","SHD"];owner.Level=50;
        var weapon=new ItemDefinition{Key="review weapon",Name="Review weapon",Slots=["PRIMARY","SECONDARY"],Classes=["ALL"],Display=new(){Skill="1H Slashing"},Stats=new(){["Damage"]=10,["Delay"]=20},Effects=["Focus: Catalog default"],MetadataComplete=true,IntroducedExpansion="Classic"};
        var sources=new[]{"Focus","Click","Worn","Proc"}.Select(k=>new ItemDefinition{Key="review "+k.ToLowerInvariant(),Name="Review "+k,Slots=["PRIMARY","SECONDARY"],Classes=["ALL"],Effects=[k+": Example "+k],MetadataComplete=true,IntroducedExpansion="Classic"}).ToList();CatalogItemMerge.Apply(Controller.Catalog,[weapon,..sources]);
        var build=new PetBuild{Name="Current build",PetType="Earth",PetLevel=50,DualWield=true,SlotConfigurationVersion=PetSlotPlanning.Version,Items=[new(){Id="review-copy",ItemKey=weapon.Key,Name=weapon.Name,Position=1,Slot="Primary",Rank=4}]};owner.ConfirmedPetInventory=build;
        PetSlotPlanning.SetEffects(build,1,sources.Select(s=>new ExaltationAssignment{SourceName=s.Name,EffectId=s.Key,Effect=s.Effects[0],State="Socketed"}),"review-copy");
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");petKey="";RefreshPet();Flush();
        var row=petItems.Items.OfType<PetItemRow>().First(r=>r.Position==1);Check(row.Procs.StartsWith("Proc:")&&!row.Procs.Contains("Catalog default"),"Weapon Exalt cell leads with selected Proc and excludes catalog defaults");
        var options=Usability.Tree(this).OfType<Button>().Single(b=>b.IsVisible&&b.Content?.ToString()=="Options ▾");
        Check(options.Height==28&&options.ContextMenu!.Items.OfType<MenuItem>().Select(m=>m.Header?.ToString()).SequenceEqual(["Edit selected","Remove selected"]),"Options has exact renamed commands");
        options.ContextMenu!.Opacity=0;options.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(options.ContextMenu.IsOpen,"Options opens the existing action menu");options.ContextMenu.IsOpen=false;
        Check(Usability.Tree(this).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text=="INVENTORY"),"Inventory title is visible above Options");
        Check(Math.Abs(options.TranslatePoint(new(),this).Y-petSearch.TranslatePoint(new(),this).Y)<1,"Options and Search have aligned control baselines");
        var copy=EquipmentPositions.Copy(build);copy.SlotConfigurations[0].Effects.Add(new(){SourceName="Review Focus",EffectId="second-focus",Effect="Focus: Other example",State="Socketed"});PetSlotPlanning.Sync(copy);
        var dialog=new PetItemDialog(Controller,weapon,copy,PetInventoryDestination.Confirmed,copy.Items[0]){Owner=this,Opacity=0,ShowActivated=false,Left=-15000,Top=-15000};dialog.Show();
        for(int i=0;i<100&&!dialog.MetadataReady;i++){await Task.Delay(20);Flush(dialog);}Check(dialog.MetadataReady,"Slot editor metadata is ready");dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush(dialog);
        Check(dialog.Result==null&&dialog.ErrorText.Text.Contains("Choose one Focus"),"Slot editor blocks duplicate types with an actionable explanation");dialog.Close();
        string splits=JsonSerializer.Serialize(Controller.Profile.PanelSplits);
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Width=915;Height=800;owner.ConfirmedPetInventory=build;RefreshBuildChoices("current");petSearch.Text="Review weapon";petKey="";RefreshPet();Flush();
            for(int i=0;i<50&&petResults.Items.Count==0;i++){await Task.Delay(20);Flush();}petResults.SelectedIndex=0;ShowPetComparison();Flush();
            Check(options.ActualHeight==28&&petComparison.IsVisible,theme+": compact controls and inspector available");Capture(theme+"-effects",this);
            Width=640;Height=650;Flush();Check(petComparison.IsVisible&&petItems.ActualHeight>80,theme+": narrow layout preserves inventory and inspector");Capture(theme+"-narrow",this);
            Width=915;Height=800;owner.ConfirmedPetInventory=copy;RefreshBuildChoices("current");petKey="";RefreshPet();Flush();var conflictRow=petItems.Items.OfType<PetItemRow>().First(r=>r.Position==1);Check(conflictRow.Procs.Contains("conflicting Focus"),theme+": legacy conflicts visible and inactive");
            Check(Usability.Tree(petComparison).OfType<TextBlock>().Count(t=>t.Text.Contains("inactive; conflicting Focus"))==2,theme+": selected inspector refreshes both conflicting effects without reselection");Capture(theme+"-conflicts",this);
            var tooltipBoard=UI.Card(UI.Stack(UI.Text("Weapon Exalt tooltip · selected effects",14,"Accent",true),UI.Text(row.Procs,12),UI.Text("Existing conflicting type · preserved for review",14,"Accent",true),UI.Text(conflictRow.Procs,12)));tooltipBoard.Background=UI.Brush("Surface");Capture(theme+"-tooltips",this,tooltipBoard,790,350);
            lab.Width=878;lab.Height=740;lab.Show();Flush(lab);Capture(theme+"-combat",lab);lab.Hide();
        }
        Check(JsonSerializer.Serialize(Controller.Profile.PanelSplits)==splits,"Window sizing preserves saved pane proportions");
        string Sha(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Json.Write(Path.Combine(folder,"placement-effect-checks.json"),new{Passed=checks.Count,Checks=checks,Timings=timings,Drags=drags,AppAssemblySha256=Sha(typeof(App).Assembly.Location),CoreAssemblySha256=Sha(typeof(GearScorer).Assembly.Location),Scope="Synthetic real Thumb down/up with physical input pumping suppressed; native bounds, separate-process restart, abstract monitor recovery and 100–300% renders. Physical monitor transitions/live-game acceptance remain separate."});
    }
}
