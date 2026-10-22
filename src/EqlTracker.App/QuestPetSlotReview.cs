using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewQuestPetSlots(string folder)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic review only.");
        Directory.CreateDirectory(Path.Combine(folder,"qa")); var checks=new List<string>();var timings=new List<object>();
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        void Capture(string name,Window window,double width,double height)
        {
            window.Width=width;window.Height=height;window.UpdateLayout();
            foreach(double scale in new[]{1d,1.5,2d,3d}){var watch=Stopwatch.StartNew();var bitmap=PreviewRenderer.Capture((FrameworkElement)window.Content,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png")))encoder.Save(output);timings.Add(new{Name=name,Scale=scale,Milliseconds=watch.Elapsed.TotalMilliseconds});}
        }
        async Task Ready(PetItemDialog dialog){for(int n=0;n<100&&!dialog.MetadataReady;n++)await Task.Delay(25);Check(dialog.MetadataReady,"Effect metadata loads for slot editing");}
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","BRD","SHD"];owner.Level=50;Controller.Profile.CurrentClasses=["MAG","BRD","SHD"];SelfRoleRatings.Apply(Controller.Profile);owner.Preset="Balanced";owner.PetPreset="DPS";
        Controller.Profile.QuestTracking["brd-epic"]=false;
        ShowView(TrackerTab.Quests);librarySearch.Text="Singing Short Sword";RefreshLibrary();Flush();
        Check(libraryTable.Items.OfType<QuestLibraryRow>().Any(r=>r.Quest.Id=="brd-epic"),"Quest library reward search finds Bard Epic");
        libraryTable.SelectedItem=libraryTable.Items.OfType<QuestLibraryRow>().First(r=>r.Quest.Id=="brd-epic");ShowLibraryDetail();
        Check(libraryDetail.Children.OfType<Expander>().Any(e=>e.Header?.ToString()=="Reward Comparison"),"Reward Comparison has a concise header");
        Check((libraryTable.SelectedItem as QuestLibraryRow)?.Preset == "Balanced", "Quest reward ratings use the current role rather than an older published profile");
        int pins=Controller.Profile.GoalQueue.Count;
        Usability.Tree(libraryDetail).OfType<Button>().Single(b=>b.Content?.ToString()=="Track").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
        Check(Controller.Profile.QuestTracking["brd-epic"]&&Controller.Profile.GoalQueue.Count==pins,"Track enables Progress without adding a Queue pin");
        Check(Usability.Tree(libraryDetail).OfType<Button>().Any(b=>b.Content?.ToString()=="Untrack"),"Track button changes to Untrack");
        ShowView(TrackerTab.Progress,"Bard Epic Quest");progressSearch.Text="Singing Short Sword";RefreshSteps();Flush();
        Check(questSelection.SelectedItem?.ToString()=="Bard Epic Quest"&&Usability.Tree(progressReward).OfType<TextBlock>().Any(t=>t.Text=="Singing Short Sword"),"Progress searches rewards and displays the selected quest reward");
        Check(progressTracked.ToolTip?.ToString()=="Filter Tracked Quests","Tracking tooltip is direct");
        Check(progressSearch.Height==28&&librarySearch.Height==28&&search.Height==28,"Quest and item search controls match 28-DIP dropdowns");
        Check(tabs.Items.OfType<TabItem>().Select(t=>t.Tag).SequenceEqual(TrackerNavigation.Order.Cast<object>())&&TrackerNavigation.Order.Take(3).SequenceEqual(new[]{TrackerTab.Items,TrackerTab.Quests,TrackerTab.Queue}),"Browse tabs precede management tabs without changing IDs");
        var blood=Controller.Catalog.Find("Bloodmoon")!;var evensong=Controller.Catalog.Find("Evensong")!;var armor=Controller.Catalog.Find("Indicolite Breastplate")!;
        var build=new PetBuild{Preset="DPS",Name="Current build",PetType="Earth",PetLevel=50,LevelMode=PetLevelMode.Manual,Items=[new(){Id="slot-primary",Position=1,Slot="Storage",ItemKey=blood.Key,Name=blood.Name,Rank=4},new(){Id="slot-secondary",Position=2,Slot="Storage",ItemKey=evensong.Key,Name=evensong.Name,Rank=5}]};
        PetSlotPlanning.Migrate(build,Controller.Catalog);owner.ConfirmedPetInventory=build;Controller.Save();
        var procSource=Controller.Catalog.Find("Earthshaker")!;string proc=procSource.Effects.First(e=>ExaltationPlanner.EffectType(e)=="Proc");
        PetSlotPlanning.SetEffects(build,1,[new(){EffectId=ExaltationPlanner.EffectId(procSource.Key,proc),SourceName=procSource.Name,Effect=proc,State="Socketed"}],build.Items[0].Id);
        var snapshot=EquipmentPositions.Copy(build.Items[0]);var edit=new PetItemDialog(Controller,blood,build,PetInventoryDestination.Confirmed,snapshot,latest:()=>build){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};
        edit.Loaded+=async(_,_)=>{await Ready(edit);edit.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
        Check(edit.ShowDialog()==true&&edit.Result?.Id==snapshot.Id&&edit.Result.Exaltations.Count==1,"Saving a snapshot-based item edit does not falsely report an occupied position");
        var stale=new PetItemDialog(Controller,blood,build,PetInventoryDestination.Confirmed,snapshot,latest:()=>build){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};
        stale.Loaded+=async(_,_)=>{await Ready(stale);build.Name="Changed while editing";stale.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(stale.Result==null&&stale.ErrorText.Text.Contains("changed"),"Stale slot saves are rejected with an explanation");stale.Close();};stale.ShowDialog();build.Name="Current build";
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");RefreshPet();Flush();
        Check(petDualWield.IsChecked==true,"Dual wield reflects migrated compatible hand configuration");
        _=Dispatcher.BeginInvoke(new Action(async()=>{var dialog=Application.Current.Windows.OfType<PetItemDialog>().Last();await Ready(dialog);dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}),DispatcherPriority.Loaded);
        EditPetItem(EquipmentPositions.Copy(build.Items[0]));build=owner.ConfirmedPetInventory!;RefreshPet();Flush();
        Check(petItems.Items.OfType<PetItemRow>().First(i=>i.Position==1).Procs.Contains("Earthquake"),"Saved slot proc immediately appears in the inventory Procs column");
        double beforeRating=petResults.Items.OfType<PetSearchRow>().First(r=>r.Item.Key==evensong.Key).Comparison?.Evaluation?.Unbuffed.Delta??double.NaN;
        build.Items.RemoveAll(i=>i.Position==2);PetEdited();RefreshPet();Flush();
        double afterRating=petResults.Items.OfType<PetSearchRow>().First(r=>r.Item.Key==evensong.Key).Comparison?.Evaluation?.Unbuffed.Delta??double.NaN;
        Check(beforeRating!=afterRating,"Pet inventory changes refresh searched candidate replacement ratings");
        // Raise a routed double-click from a realized item cell; close the actual editor it opens.
        petItems.ScrollIntoView(petItems.Items[0]);Flush();bool opened=false;
        _=Dispatcher.BeginInvoke(new Action(()=>{var dialog=Application.Current.Windows.OfType<PetItemDialog>().LastOrDefault();if(dialog!=null){opened=true;dialog.Close();}}),DispatcherPriority.Loaded);
        var cell=Usability.Tree(petItems).OfType<DataGridCell>().First(c=>c.Column.SortMemberPath=="Name");
        petItems.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=cell});
        Check(opened,"Routed inventory-name double-click opens the slot editor");
        var effect=Controller.Catalog.Items.First(i=>i.Effects.Any(e=>ExaltationPlanner.EffectType(e)=="Proc"));
        PetSlotPlanning.SetEffects(build,3,[new(){EffectId=ExaltationPlanner.EffectId(effect.Key,effect.Effects.First(e=>ExaltationPlanner.EffectType(e)=="Proc")),SourceName=effect.Name,Effect=effect.Effects.First(e=>ExaltationPlanner.EffectType(e)=="Proc"),State="Planned"}],"");
        for(int themeIndex=0;themeIndex<3;themeIndex++)
        {
            string theme=new[]{"coldain","sunstrider","shadowman"}[themeIndex];Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);
            ShowView(TrackerTab.Quests);librarySearch.Text="Singing Short Sword";RefreshLibrary();Flush();Capture(theme+"-quest",this,1220,800);
            ShowView(TrackerTab.Progress,"Bard Epic Quest");Flush();Capture(theme+"-progress",this,1220,800);Capture(theme+"-progress-narrow",this,760,720);
            ShowView(TrackerTab.Pet);RefreshPet();petResults.SelectedItem=petResults.Items.OfType<PetSearchRow>().First(r=>r.Item.Key==evensong.Key);ShowPetComparison();Flush();Capture(theme+"-pet",this,1400,820);Capture(theme+"-pet-narrow",this,820,720);
            var empty=new PetItemDialog(Controller,null,build,PetInventoryDestination.Confirmed,null,null,3){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};empty.Show();await Ready(empty);Usability.Tree(empty).OfType<Expander>().First(e=>e.Header?.ToString()=="Exaltations").IsExpanded=true;empty.UpdateLayout();Capture(theme+"-empty-slot",empty,650,620);Check(empty.ItemChoice.SelectedItem?.ToString()=="Empty position","Empty positions expose the item selector and configured effects");empty.Close();
            var compare=new PetComparisonWindow(Controller,EquipmentPositions.Copy(build),()=>{}){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};compare.Show();await compare.Recalculate();for(int wait=0;wait<100&&compare.Results.Items.Count==0;wait++)await Task.Delay(25);compare.StopReviewPolling();compare.UpdateLayout();Check(compare.Results.Items.Count>0,"Comparison cards are populated before rendering");Capture(theme+"-pet-columns",compare,1180,800);Capture(theme+"-pet-columns-narrow",compare,760,720);
            var panel=Usability.Tree(compare.Results).OfType<StackPanel>().FirstOrDefault(p=>p.IsItemsHost);Check(panel?.Orientation==Orientation.Horizontal,"Pet cards use horizontal columns in "+theme);compare.Close();
        }
        Json.Write(Path.Combine(folder,"quest-pet-checks.json"),new{Passed=checks.Count,Checks=checks,Timings=timings,Scope="Synthetic WPF controls, routed editing, quest navigation and three-theme 100/150/200/300 percent renders; physical DPI and live game remain separate."});
    }
}
