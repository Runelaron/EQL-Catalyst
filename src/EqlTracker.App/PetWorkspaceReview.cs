using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewPetWorkspace(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only.");
        var checks=new List<string>();var watch=Stopwatch.StartNew();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        async Task Wait(Func<bool> ready){var t=Stopwatch.StartNew();while(!ready()){if(t.ElapsedMilliseconds>15000)throw new InvalidOperationException("Timed out: "+checks.LastOrDefault());await Task.Delay(30);Flush();}}
        void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Capture(string name,FrameworkElement element,double width,double height){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(element,width,height,1)));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);}
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","SHD","BRD"];owner.Level=50;
        var saved=PetInventoryEditing.Confirmed(owner,Controller.Catalog);saved.Items.Clear();saved.SlotConfigurations.Clear();saved.SlotConfigurationVersion=1;saved.DualWield=true;saved.FollowDetectedSummon=false;saved.PetType="Air";saved.LevelMode=PetLevelMode.Manual;saved.PetLevel=50;saved.SummonSpell="Greater Conjuration: Air";saved.SummonClass="MAG";saved.SummonRank=10;saved.SummonRankKnown=true;
        foreach(var (name,itemRank,slot) in new[]{("Bloodmoon",4,"Primary"),("Evensong",5,"Secondary"),("Indicolite Breastplate",5,"Chest"),("Indicolite Bracer",7,"Wrist")}){var item=Controller.Catalog.Find(name)!;saved.Items.Add(new(){Position=saved.Items.Count+1,ItemKey=item.Key,Name=item.Name,Rank=itemRank,Slot=slot,EquipmentVerified=true});}
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");RefreshPet();Width=915;Height=650;Flush();await Wait(()=>petItems.Items.Count>0);
        Check(!petWorkspaceSave.IsEnabled&&!petWorkspaceUndo.IsEnabled,"Current opens with clean Save and Undo");
        string original=PetWorkspaceDraft.Key(saved);var build=SelectedPetInventory!;
        CommitPetRank(build.Items[0].Id,7);Flush();await Wait(()=>petWorkspaceSave.IsEnabled);
        Check(PetWorkspaceDraft.Key(saved)==original&&petWorkspaceUndo.IsEnabled,"Rank edit previews in an isolated draft");Click(petWorkspaceUndo);Flush();Check(!petWorkspaceSave.IsEnabled&&!petWorkspaceUndo.IsEnabled&&SelectedPetInventory!.Items[0].Rank==4,"Undo restores the full baseline and disables both actions");
        build=SelectedPetInventory!;CommitPetRank(build.Items[0].Id,6);Flush();Click(petWorkspaceSave);Flush();Check(!petWorkspaceSave.IsEnabled&&!petWorkspaceUndo.IsEnabled&&owner.ConfirmedPetInventory!.Items[0].Rank==6,"Save commits Current and establishes a clean baseline");
        petSearch.Text="Bloodmoon";Flush();await Task.Delay(120);Check(!petWorkspaceSave.IsEnabled,"Search and ordinary refreshes do not create draft changes");petSearch.Clear();
        Check(Usability.Tree(this).OfType<Button>().Any(b=>Equals(b.Content,"Pet Details"))&&!Usability.Tree(this).OfType<Button>().Any(b=>Equals(b.Content,"Compare pets…")),"Pet actions omit literal ellipses");
        var tools=Usability.Tree(this).OfType<Button>().Single(b=>Equals(b.Content,"Tools ▾"));Click(tools);Flush();var menu=tools.ContextMenu!;Check(menu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"Compare pets"))&&menu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"Weights"))&&menu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"Pet data"))==TrackerController.PetResearchEnabled,"Tools menu has Compare pets, Weights and gated Pet data");menu.IsOpen=false;
        Capture("pet-workspace",(FrameworkElement)Content,915,650);Width=700;Flush();Capture("pet-workspace-narrow",(FrameworkElement)Content,700,650);
        OpenPetDetails();Flush();var details=petDetailsPopup!;details.Left=details.Top=-15000;details.Opacity=0;await Wait(()=>details.Sheet.HasResult);Check(!details.Sheet.Values.ContainsKey("Endurance")&&!details.Sheet.Values.ContainsKey("EnduranceRegen"),"Visible sheet omits Endurance and its regeneration");details.Close();
        // Editor operates on a clone; opening it alone never creates a workspace edit.
        build=SelectedPetInventory!;var entry=build.Items[0];var editor=new PetItemDialog(Controller,Controller.Catalog.FindKey(entry.ItemKey),build,PetInventoryDestination.Confirmed,entry,latest:()=>SelectedPetInventory){Owner=this,ShowActivated=false};
        editor.Show();editor.UpdateLayout();await Wait(()=>editor.MetadataReady);
        Check(editor.Title.EndsWith("Edit pet slot 1")&&editor.Width==460&&editor.Height==360,"Editor opens with a numbered title and compact dimensions");
        Check(!editor.SaveButton.IsEnabled&&editor.ItemChoice.Width==270,"Opening the editor is clean and its Item field is 270 DIP");
        Check(!Usability.Tree(editor).OfType<CheckBox>().Any(c=>c.Content?.ToString()?.Contains("Verified")==true)&&!Usability.Tree(editor).OfType<TextBlock>().Any(t=>t.Text is "POSITION" or "INTENDED SLOT"),"Position, intended slot and verification controls are removed");
        var rank=Usability.Tree(editor).OfType<ComboBox>().Single(c=>c.Width==50);rank.Text="8";editor.UpdateLayout();await Task.Delay(30);Check(rank.IsEditable&&rank.Items.Contains("20")&&editor.SaveButton.IsEnabled,"Typed rank enables Save");rank.Text="invalid";editor.UpdateLayout();await Task.Delay(30);Check(!editor.SaveButton.IsEnabled,"Invalid typed rank disables Save");rank.Text=entry.Rank.ToString();editor.UpdateLayout();await Task.Delay(30);Check(!editor.SaveButton.IsEnabled,"Returning to the original rank clears dirty state");
        var exalt=Usability.Tree(editor).OfType<Expander>().Single(e=>Equals(e.Header,"Exaltations"));double compact=editor.Height,width=editor.Width;exalt.IsExpanded=true;editor.UpdateLayout();await Task.Delay(70);Check(editor.Height>compact&&editor.Width==width&&editor.Height<=editor.MaxHeight,"Exaltations grow height within the monitor without changing width");Capture("pet-slot-expanded",(FrameworkElement)editor.Content,editor.Width,editor.Height);exalt.IsExpanded=false;editor.UpdateLayout();await Task.Delay(70);Check(editor.Height==compact,"Collapsing Exaltations restores compact height");
        Capture("pet-slot",(FrameworkElement)editor.Content,editor.Width,editor.Height);
        editor.WindowStartupLocation=WindowStartupLocation.Manual;editor.Left=80;editor.Top=80;editor.Opacity=1;editor.UpdateLayout();await Task.Delay(200);
        var frame=NativePetWorkspace.Capture(editor,Path.Combine(folder,"pet-slot-native.png"));Check(frame.Captured&&frame.ClientTop==0&&frame.WhiteTopPixels==0,"Native dialog capture has no caption/glass white strip");editor.Close();
        Check(!petWorkspaceSave.IsEnabled,"Canceling an unchanged editor leaves the workspace clean");
        Width=915;Flush();await Wait(()=>petItems.Items.Count>2);
        DataGridRow Row(int position){petItems.ScrollIntoView(petItems.Items.OfType<PetItemRow>().Single(r=>r.Position==position));petItems.UpdateLayout();return (DataGridRow)petItems.ItemContainerGenerator.ContainerFromItem(petItems.Items.OfType<PetItemRow>().Single(r=>r.Position==position));}
        void Drop(FrameworkElement target,DataObject data)
        {var constructor=typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single();var ev=(DragEventArgs)constructor.Invoke(new object[]{data,DragDropKeyStates.None,DragDropEffects.All,target,new Point(5,5)});ev.RoutedEvent=DragDrop.DropEvent;target.RaiseEvent(ev);Flush();}
        string aId=SelectedPetInventory!.Items.Single(i=>i.Position==1).Id,swapId=SelectedPetInventory.Items.Single(i=>i.Position==3).Id,bId=SelectedPetInventory.Items.Single(i=>i.Position==4).Id;
        Drop(Row(4),new DataObject("EQL.PetInventory",new PetInventoryDrag(build.Id,swapId,PetDestination)));
        Json.Write(Path.Combine(folder,"drop-last.json"),new{Feedback=petFeedback.Text,Items=SelectedPetInventory!.Items.Select(i=>new{i.Id,i.Position,i.Name}),MoveBuild=build.Id,Current=SelectedPetInventory.Id,Save=petWorkspaceSave.IsEnabled});
        Check(SelectedPetInventory!.Items.Single(i=>i.Position==4).Id==swapId&&SelectedPetInventory.Items.Single(i=>i.Position==3).Id==bId&&petWorkspaceSave.IsEnabled,"Routed pet-row drop swaps exactly two identities in the draft");Click(petWorkspaceUndo);Flush();await Wait(()=>petItems.Items.Count>2);
        Drop(Row(1),new DataObject("EQL.PetInventory",new PetInventoryDrag(SelectedPetInventory!.Id,aId,PetDestination)));Check(!petWorkspaceSave.IsEnabled,"Routed same-position drop is a clean no-op");
        var replacement=Controller.Catalog.Find("Evensong")!;Drop(Row(1),new DataObject("EQL.CatalogItem",replacement.Key));Check(SelectedPetInventory!.Items.Single(i=>i.Position==1).ItemKey==replacement.Key&&SelectedPetInventory.Items.Count==4,"Routed candidate drop replaces its occupied target without redirecting");Click(petWorkspaceUndo);Flush();await Wait(()=>petItems.Items.Count>2);
        Drop(petResults,new DataObject("EQL.PetInventory",new PetInventoryDrag(SelectedPetInventory!.Id,aId,PetDestination)));Check(SelectedPetInventory!.Items.All(i=>i.Position!=1)&&petWorkspaceSave.IsEnabled,"Routed inventory-to-candidate drop removes only the loadout item");Click(petWorkspaceUndo);Flush();Check(SelectedPetInventory!.Items.Count==4&&!petWorkspaceSave.IsEnabled,"Undo restores drop operations completely");

        // UI Automation checks the exact focus/wrap boundary without taking over the desktop cursor.
        var probe=new PetFilterProbe{ShowActivated=true,WindowStartupLocation=WindowStartupLocation.Manual,Left=80,Top=80,Topmost=true};probe.Show();probe.Activate();await Task.Delay(200);
        var bounds=new List<object>();foreach(double w in new[]{424d,425,426,427,600}){probe.Width=w;probe.UpdateLayout();var before=probe.Toggle.TransformToAncestor(probe).TransformBounds(new Rect(probe.Toggle.RenderSize));probe.Toggle.Focus();probe.UpdateLayout();((System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(probe.Toggle)).Toggle();probe.UpdateLayout();var after=probe.Toggle.TransformToAncestor(probe).TransformBounds(new Rect(probe.Toggle.RenderSize));Json.Write(Path.Combine(folder,"filter-last.json"),new{Width=w,Checked=probe.Toggle.IsChecked,Before=before.ToString(),After=after.ToString(),Height=probe.Toggle.ActualHeight,Pointer=NativePetWorkspace.LastPointer});Check(probe.Toggle.IsChecked==true&&before==after&&probe.Toggle.ActualHeight==28,"Filter focus and first UI Automation toggle preserve bounds at width "+w);bounds.Add(new{Width=w,Before=before.ToString(),After=after.ToString()});((System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(probe.Toggle)).Toggle();probe.UpdateLayout();Check(probe.Toggle.IsChecked==false,"Filter second toggle collapses at width "+w);}probe.Close();
        // Comparison polling must not recalculate because timestamps or undo bookkeeping changed.
        var comparison=new PetComparisonWindow(Controller,EquipmentPositions.Copy(SelectedPetInventory!),()=>{},()=>SelectedPetInventory,()=>petEditRevision){Owner=this,Opacity=0,Left=-15000,Top=-15000,ShowActivated=false};comparison.Show();await Wait(()=>comparison.Rows.Count>0&&!comparison.IsUpdating);long completed=comparison.CompletedRequests;string status=comparison.Status.Text;
        PetBuildEditing.Remember(SelectedPetInventory!,"bookkeeping only");SelectedPetInventory!.Items[0].ConfirmedAt=DateTime.Now;petEditRevision++;await Task.Delay(2200);Check(comparison.CompletedRequests==completed&&comparison.Status.Text==status&&!comparison.IsUpdating,"Idle comparison ignores timestamps history and refresh counters");
        CommitPetRank(SelectedPetInventory.Items[0].Id,9);Flush();await Wait(()=>comparison.CompletedRequests>completed&&!comparison.IsUpdating);Check(comparison.Rows.Count>0,"Comparison refreshes actual draft changes while retaining results");comparison.Close();Click(petWorkspaceUndo);Flush();
        bool Exit(string choice)
        {var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<TheorycraftExitDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();Click(Usability.Tree(dialog).OfType<Button>().Single(b=>Equals(b.Content,choice)));};timer.Start();bool result=ConfirmPetWorkspaceExit();timer.Stop();return result;}
        CommitPetRank(SelectedPetInventory!.Items[0].Id,8);Flush();Check(!Exit("Cancel")&&petDraft!.Dirty,"Cancel retains the draft when leaving");Check(Exit("Discard"),"Discard permits leaving");RefreshBuildChoices("current");Check(!petWorkspaceSave.IsEnabled&&SelectedPetInventory!.Items[0].Rank==6,"Discard reloads the committed baseline");CommitPetRank(SelectedPetInventory!.Items[0].Id,7);Flush();Check(Exit("Save")&&owner.ConfirmedPetInventory!.Items[0].Rank==7,"Save on leaving commits the draft");RefreshBuildChoices("current");
        var copied=new PetBuild();var scenario=new PetBuild{ComparisonScenario=new(),LevelEvidence="Saved context"};CopyPetAssumptions(scenario,copied);Check(copied.ComparisonScenario!=null&&copied.LevelEvidence=="Saved context","Copying a plan retains comparison assumptions and level evidence");
        var plan=GuidedPetBuilds.Clone(SelectedPetInventory!,"Draft plan");int beforePlans=owner.PetBuilds.Count;BeginPetDraft(plan,true);PetEdited();Check(owner.PetBuilds.Count==beforePlans&&petWorkspaceSave.IsEnabled,"New plans remain outside the saved profile until Save");Click(petWorkspaceSave);Flush();Check(owner.PetBuilds.Any(p=>p.Id==plan.Id)&&!petWorkspaceSave.IsEnabled,"Saving a new plan establishes its independent baseline");
        var savedPlan=owner.PetBuilds.Single(p=>p.Id==plan.Id);int savedRank=savedPlan.Items[0].Rank;CommitPetRank(SelectedPetInventory!.Items[0].Id,11);Flush();Check(savedPlan.Items[0].Rank==savedRank,"Existing planned build edits remain drafts");savedPlan.Items[0].Rank=12;Check(!SavePetWorkspace()&&petWorkspaceSave.IsEnabled&&petFeedback.Text.Contains("saved build changed"),"Concurrent saved changes require review and retain the draft");Click(petWorkspaceUndo);Flush();Check(SelectedPetInventory!.Items[0].Rank==12&&!petWorkspaceSave.IsEnabled,"Undo reloads concurrently updated saved data without overwriting it");
        RefreshBuildChoices("current");Flush();
        ShowView(TrackerTab.Progress);Flush();Check(!Usability.Tree(this).OfType<CheckBox>().Any(c=>Equals(c.Content,"All")),"Progress no longer displays All");progressByZone.IsChecked=true;Flush();Check(progressMode==ProgressBrowseMode.Zone&&progressSelectorLabel.Text=="ZONE"&&progressTracked.IsChecked==false,"By zone is an independent mode starting unfiltered");Check(questSelection.Items.Count>0&&ProgressChoice!.ZoneKey.Length>0,"Zone mode contains documented normalized zones");string zone=ProgressChoice!.Key;progressSearch.Text="sky";Flush();progressByItem.IsChecked=true;Flush();Check(progressByZone.IsChecked==false&&progressMode==ProgressBrowseMode.Item,"Item and Zone modes are mutually exclusive");progressByZone.IsChecked=true;Flush();Check(progressSearch.Text=="sky","Zone search is retained independently");progressSearch.Clear();progressByZone.IsChecked=false;Flush();Check(progressMode==ProgressBrowseMode.Quest,"Unchecking the active browse mode returns to Quest");progressByZone.IsChecked=true;Flush();Capture("progress-zones",(FrameworkElement)Content,700,650);
        petDraft=null;
        Json.Write(Path.Combine(folder,"pet-workspace-checks.json"),new{Passed=checks.Count,Checks=checks,PrivateResearch=TrackerController.PetResearchEnabled,Milliseconds=watch.Elapsed.TotalMilliseconds,Frame=frame,FilterBounds=bounds,AppSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location)))});
    }
    private sealed class PetFilterProbe:FrostDialog
    {
        internal ToggleButton Toggle=PetFilterToggle.Create();
        internal PetFilterProbe():base("Filter focus review",460,360)
        {var wrap=new WrapPanel();wrap.Children.Add(new TextBox{Width=200,Height=28});wrap.Children.Add(new ComboBox{Width=130,Height=28});wrap.Children.Add(Toggle);var extra=UI.Text("Expanded filters",12);extra.Visibility=Visibility.Collapsed;Toggle.Checked+=(_,_)=>extra.Visibility=Visibility.Visible;Toggle.Unchecked+=(_,_)=>extra.Visibility=Visibility.Collapsed;Editor.Children.Add(UI.Stack(wrap,extra));}
    }
}

internal static class NativePetWorkspace
{
    [StructLayout(LayoutKind.Sequential)]private struct RectNative{internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct PointNative{internal int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint window,out RectNative rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(nint window,ref PointNative point);
    [DllImport("user32.dll")]private static extern bool PrintWindow(nint window,nint dc,uint flags);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")]private static extern nint SendMessage(nint window,uint message,nint wparam,nint lparam);
    internal sealed record FrameResult(bool Captured,int Width,int Height,int ClientTop,int WhiteTopPixels,double RenderedWidth,double RenderedHeight);
    internal static FrameResult Capture(Window window,string path)
    {
        var handle=new WindowInteropHelper(window).Handle;GetWindowRect(handle,out var rect);var origin=new PointNative();ClientToScreen(handle,ref origin);int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        using var bitmap=new System.Drawing.Bitmap(width,height);using var graphics=System.Drawing.Graphics.FromImage(bitmap);nint dc=graphics.GetHdc();bool captured;try{captured=PrintWindow(handle,dc,2);}finally{graphics.ReleaseHdc(dc);}bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        int white=0;for(int y=2;y<Math.Min(6,height);y++)for(int x=10;x<width-10;x++){var c=bitmap.GetPixel(x,y);if(c.R>235&&c.G>235&&c.B>235)white++;}
        return new(captured,width,height,origin.Y-rect.Top,white,window.ActualWidth,window.ActualHeight);
    }
    [DllImport("user32.dll")]private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    internal static object? LastPointer;
    [DllImport("user32.dll")]private static extern nint WindowFromPoint(PointNative point);
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint handle);
    internal static async Task Click(Window window,FrameworkElement control)
    {
        var hwnd=new WindowInteropHelper(window).Handle;SetForegroundWindow(hwnd);GetCursorPos(out var previous);var screen=control.PointToScreen(new Point(control.ActualWidth/2,control.ActualHeight/2));
        try{await Task.Delay(100);SetCursorPos((int)screen.X,(int)screen.Y);await Task.Delay(70);mouse_event(2,0,0,0,0);await Task.Delay(40);mouse_event(4,0,0,0,0);await Task.Delay(40);GetCursorPos(out var actual);LastPointer=new{Target=hwnd.ToInt64(),At=WindowFromPoint(actual).ToInt64(),Foreground=GetForegroundWindow().ToInt64(),X=screen.X,Y=screen.Y,ActualX=actual.X,ActualY=actual.Y,control.IsMouseOver,window.IsActive,window.Left,window.Top,Dpi=VisualTreeHelper.GetDpi(window).DpiScaleX};}finally{mouse_event(4,0,0,0,0);SetCursorPos(previous.X,previous.Y);}
    }
}
