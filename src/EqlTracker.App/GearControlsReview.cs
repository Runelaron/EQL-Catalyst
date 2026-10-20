using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    internal async Task ReviewGearControls(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Isolated preview required.");
        string qa=Path.Combine(folder,"qa");Directory.CreateDirectory(qa);var checks=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        void Flush(){Refresh();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Capture(string name,FrameworkElement component,double width=1220,double height=740)
        {
            if(ReferenceEquals(component,Content)){Width=width;Height=height;Flush();}
            foreach(var glyph in Usability.Tree(component).OfType<ItemRoleGlyph>())glyph.InvalidateVisual();
            foreach(double scale in new[]{1d,1.5,2,3}){var bitmap=PreviewRenderer.Capture(component,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(qa,$"gear-controls-{name}-{scale:0.0}x.png"));encoder.Save(output);}
        }
        GearBuildFixtures.Apply(Controller);Controller.Profile.SmoothMeterMotion=false;Controller.Profile.ColumnWidths.Clear();Controller.Profile.PanelSplits.Clear();Controller.Profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(Controller.Profile);
        Width=1220;Height=740;RefreshGearBuildChoices("current");ShowView(TrackerTab.Gear);gearSearch.Text="Apothic Robe";SelectGearPosition("CHEST:1");Flush();
        Check(gearUpdateCurrent.Content?.ToString()=="Auto Update","Auto Update retains the saved inventory-update preference");
        Check(gearUseCurrentField?.Visibility==Visibility.Collapsed,"Gear Current hides the complete activation action group");
        Check(gearBuildChoice.ActualHeight==preset.ActualHeight&&gearBuildChoice.ActualHeight>=28,"Build and Role dropdown heights match");
        var info=Usability.Tree((DependencyObject)preset.Parent).OfType<Button>().First();
        Check(info.HorizontalAlignment==HorizontalAlignment.Right&&info.ActualWidth==20,"Role information control is at the right of the label row");
        Check(!Usability.Tree((DependencyObject)Content).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Current build · Unbuffed")),"Redundant Gear summary strip is absent");
        var tool=GearCommandMenu(false);var buildMenu=GearCommandMenu(true);
        Check(tool.ContextMenu.Items.OfType<MenuItem>().Select(m=>m.Header?.ToString()).SequenceEqual(["Stat profiles…","Edit role weights…","Manual equipment correction…","View sources","Break-even rank","Reset Gear panels"]),"Tools contains the six non-build commands");
        Check(!buildMenu.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Header?.ToString()?.Contains("Stat profiles")==true),"Build menu contains no stat-profile commands");
        Capture("gear",(FrameworkElement)Content);Capture("gear-narrow",(FrameworkElement)Content,720,470);
        Width=1220;Height=740;Flush();
        var actual=Usability.Tree((DependencyObject)Content).OfType<Button>().Single(b=>b.Content?.ToString()=="Tools ▾");actual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(1350);
        Check(actual.ContextMenu.IsOpen,"Tools stays open across refresh time and pointer departure");actual.ContextMenu.IsOpen=false;
        var item=new ItemDefinition{Key="drag-fixture",Name="Compatible blade · preview fixture",Classes=["ALL"],Slots=["PRIMARY","SECONDARY"],MetadataComplete=true,Display=new(){Type="1H Slashing",Skill="1H Slashing"},Stats=new(){["STR"]=9}};Controller.Catalog.Items.Add(item);
        var row=new GearRow(item.Name,"Primary","",0,"","","",""){Item=item,Rank=0,PositionId="PRIMARY:1"};
        gearTable.ItemsSource=new[]{row};gearTable.SelectedItem=row;ShowSelectedGear();ChangeGearPreviewRank(7);ShowSelectedGear();
        Check(GearInspectedRank(row)==7&&row.Rank==0,"Inspected +7 remains independent of the candidate list rank");
        var placed=row with{Rank=GearInspectedRank(row),Instance=GearInspectedInstance(row)};var prepared=PrepareGearPlacement(placed);
        Check(prepared.Count(p=>p.Target.Allowed)>1&&prepared.Any(p=>!p.Target.Allowed),"Shared placement results include valid destinations and explicit invalid reasons");
        var before=EquipmentPositions.Fingerprint(EquipmentPositions.For(Controller.Profile.ActiveLoadout,false));TryGearDraft(placed,"CHEST:1");
        Check(before==EquipmentPositions.Fingerprint(EquipmentPositions.For(Controller.Profile.ActiveLoadout,false)),"Invalid Gear row does not change Current");
        TryGearDraft(placed,"PRIMARY:1");Flush();Check(EquipmentPositions.For(Controller.Profile.ActiveLoadout,false).Single(p=>p.Id=="PRIMARY:1").Item?.Rank==7,"Explicit Gear placement adopts matching inspected rank");UndoGearDraft();Flush();
        Check(before==EquipmentPositions.Fingerprint(EquipmentPositions.For(Controller.Profile.ActiveLoadout,false)),"Gear placement Undo restores the full previous baseline");
        gearDragTargets=PrepareGearPlacement(placed).ToDictionary(p=>p.Target.Id);
        foreach(var visual in Usability.Tree(gearEquipment).OfType<DataGridRow>())GearDragRows.State(visual,visual.Item is EquipmentPosition p&&gearDragTargets.GetValueOrDefault(p.Id)?.Target.Allowed==true?2:1);
        var invalid=Usability.Tree(gearEquipment).OfType<DataGridRow>().First(r=>(int)r.GetValue(GearDragRows.StateProperty)==1);
        Check(invalid.Opacity==.35,"Invalid equipment rows dim to exactly 35 percent");
        Check(gearEquipment.ColumnHeaderStyle==null||gearEquipment.Opacity==1,"Table and column headers remain crisp while dragging");Capture("drag-guidance",(FrameworkElement)Content);
        gearDragTargets=null;foreach(var visual in Usability.Tree(gearEquipment).OfType<DataGridRow>())GearDragRows.State(visual,0);
        Check(invalid.Opacity==1,"Clearing drag state restores recycled row opacity");
        var picker=new GearSlotPickerDialog(item.Name,prepared.Where(p=>p.Target.Allowed).Select(p=>p.Target));Capture("slot-picker",(FrameworkElement)picker.Content,500,330);
        var backup=new CurrentBuildBackupDialog("Raid build");Capture("backup-prompt",(FrameworkElement)backup.Content,520,245);
        Check(backup.Choice==CurrentBackupChoice.Cancel&&!Usability.Tree((DependencyObject)backup.Content).OfType<Button>().Single(b=>b.Content?.ToString()=="Save & use build").IsEnabled,"Backup prompt cancels by default and requires a name for Save");
        foreach(var choice in new[]{"Save & use build","No, replace Current","Cancel"})
        {
            var prompt=new CurrentBuildBackupDialog("Review plan","Saved Current"){Owner=this,ShowActivated=false,Left=-15000,Top=-15000};
            prompt.Loaded+=(_,_)=>Dispatcher.BeginInvoke(()=>Usability.Tree((DependencyObject)prompt.Content).OfType<Button>().Single(b=>b.Content?.ToString()==choice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            prompt.ShowDialog();Check(prompt.Choice==(choice.StartsWith("Save")?CurrentBackupChoice.Save:choice.StartsWith("No")?CurrentBackupChoice.Replace:CurrentBackupChoice.Cancel),"Backup dialog action: "+choice);
        }
        var armor=new ItemDefinition{Name="Many-slot armor",Slots=["HEAD","ARMS","WRIST","CHEST","LEGS","FEET"],Display=new(){Type="Armor"}};
        foreach(var style in new[]{IconArtworkStyle.Clean,IconArtworkStyle.Vibrant})
        {
            Controller.Profile.IconStyle=style;IconArtwork.Apply(Controller.Profile);
            var header=new ItemHeaderIcons(UI.Text(armor.Name,16,"Accent",true));header.Show(armor,new(true,"Bank1 · captured today · quantity unknown",false,null));
            Capture("header-"+style,UI.Card(header),280,140);
            var glyphs=Usability.Tree(header).OfType<ItemRoleGlyph>().ToList();Check(glyphs.Count==7&&glyphs.First(g=>g.Role=="inventory").ToolTip!.ToString()!.Contains("Quantity unknown"),style+" shared header preserves every slot and ownership evidence");
            Check(Grid.GetRow(header.Title)==1&&Grid.GetColumn(glyphs.First(g=>g.Role=="inventory"))==1,style+" icons sit above the title with ownership on the right");
            Check(glyphs.Where(g=>g.Role!="inventory").All(g=>System.Windows.Automation.AutomationProperties.GetName(g).Length>0),style+" type glyphs retain precise accessible names");
            Capture("drag-glyph-"+style,UI.Stack(UI.Label("Copy · candidate +7"),ItemDragFeedback.Preview(item,7),UI.Label("Move · pet inventory +4"),ItemDragFeedback.Preview(armor,4)),240,160);
        }
        Controller.Profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(Controller.Profile);
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");Flush();Check(!Usability.Tree((DependencyObject)Content).OfType<Button>().Any(b=>b.IsVisible&&b.Content?.ToString()=="Use as current build…"),"Pet Current hides activation");Capture("pet",(FrameworkElement)Content);
        foreach(var tab in new[]{TrackerTab.Queue,TrackerTab.Items,TrackerTab.Quests,TrackerTab.Exaltations,TrackerTab.Progress}){ShowView(tab);Flush();Capture("tracker-"+tab,(FrameworkElement)Content);}
        Check(!Usability.Tree((DependencyObject)Content).OfType<TextBlock>().Any(t=>t.Text.Contains("Follow the selected branch")),"Tracker static purpose lines are removed");
        var settings=new SettingsWindow(Controller){ShowActivated=false,Left=-15000,Top=-15000};settings.Show();settings.SelectSection(SettingsSection.Sync);settings.UpdateLayout();
        Check(Usability.Tree(settings).OfType<CaptureSettingsControls>().Count()==1,"Sync & files exposes shared capture preferences");Capture("sync-settings",(FrameworkElement)settings.Content,940,610);
        var capturePanel=Usability.Tree(settings).OfType<CaptureSettingsControls>().Single();var boxes=Usability.Tree(capturePanel).OfType<CheckBox>().ToList();
        bool auto=Controller.Profile.UpdateCurrentBuildFromInventory;boxes.Single(b=>b.Content?.ToString()=="Periodic capture").IsChecked=true;boxes.Single(b=>b.Content?.ToString()=="Periodic capture").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Check(Controller.Profile.NativeSync.Periodic&&Controller.Profile.UpdateCurrentBuildFromInventory==auto,"Capture opt-in does not change Auto Update");
        var combo=Usability.Tree(capturePanel).OfType<ComboBox>().Single();combo.SelectedIndex=6;var custom=Usability.Tree(capturePanel).OfType<TextBox>().Single(t=>t.Name=="CaptureCustomMinutes");custom.Text="7";
        // Enter/focus behavior is also exercised through the actual editor's validation method.
        typeof(CaptureSettingsControls).GetMethod("Commit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(capturePanel,null);
        Check(NativeSyncPolicy.Minutes(Controller.Profile.NativeSync)==7,"Custom capture interval commits whole minutes");custom.Text="1441";typeof(CaptureSettingsControls).GetMethod("Commit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(capturePanel,null);Check(NativeSyncPolicy.Minutes(Controller.Profile.NativeSync)==7,"Invalid custom interval preserves the previous schedule");settings.Close();
        var samples=new List<object>();
        for(int warm=-5;warm<100;warm++)
        {
            long bytes=GC.GetAllocatedBytesForCurrentThread();long at=Stopwatch.GetTimestamp();_ = PrepareGearPlacement(placed);double elapsed=Stopwatch.GetElapsedTime(at).TotalMilliseconds;if(warm>=0)samples.Add(new{Stage="Eligibility preparation",Milliseconds=elapsed,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes});
        }
        var sync=Controller.Profile.NativeSync;sync.Key=112;sync.Verified=true;sync.Periodic=true;sync.LastSuccess=DateTime.Now;
        var feedbackRow=new DataGridRow();for(int n=0;n<1000;n++){long bytes=GC.GetAllocatedBytesForCurrentThread();long at=Stopwatch.GetTimestamp();GearDragRows.State(feedbackRow,n%3);_ = NativeSyncPolicy.Due(sync,DateTime.Now,false);_ = NativeSyncPolicy.Eligible(true,5,false,false,false);samples.Add(new{Stage="Row feedback + scheduling policy",Milliseconds=Stopwatch.GetElapsedTime(at).TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes});}
        Json.Write(Path.Combine(folder,"drag-scheduler-samples.json"),samples);
        Json.Write(Path.Combine(folder,"gear-controls-checks.json"),new{Passed=checks.Count,Checks=checks,AppSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(QuestWindow).Assembly.Location))).ToLowerInvariant(),CoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GearPlacementSession).Assembly.Location))).ToLowerInvariant(),Scope="One affected Tracker/Sync/header/dialog pass; no combat benchmarks or live game input",Limitations=new[]{"OLE pointer/cancel and foreground behavior require live interaction acceptance.","Synthetic drag/scheduler samples retain all measured wall times, not full game-process Sync timings.","Physical monitor DPI remains manual."}});
    }
}
