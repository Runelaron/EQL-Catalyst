using System.Diagnostics;
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
    internal async Task ReviewCompactPet(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var timings=new List<object>();var drags=new List<CascadingDrag.Observation>();
        void Check(bool v,string message){if(!v)throw new InvalidOperationException(message);checks.Add(message);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        string[] Texts(DependencyObject view)=>Usability.Tree(view).OfType<TextBlock>().Select(t=>t.Text).ToArray();
        void Capture(string name,Window window,double width,double height)
        {
            window.Width=width;window.Height=height;window.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            foreach(double scale in new[]{1d,1.5,2d,3d}){var clock=Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();var bitmap=PreviewRenderer.Capture((FrameworkElement)window.Content,width,height,scale);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png")))encoder.Save(output);timings.Add(new{Name=name,Scale=scale,Milliseconds=clock.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes});}
        }
        void Drag(Thumb thumb,double delta,bool cancel=false,bool captureLoss=false)
        {
            var root=thumb as FrameworkElement;Point point=new(0,0);CascadingDrag.ReviewPosition=(_,_)=>point;
            thumb.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
            point=new(delta,0);thumb.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.PreviewMouseMoveEvent});Flush();
            // Reverse to the final position from the same immutable snapshot.
            point=new(delta*.65,0);thumb.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.PreviewMouseMoveEvent});Flush();
            if(captureLoss)thumb.ReleaseMouseCapture();
            else if(cancel)thumb.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(thumb)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            else thumb.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});
            CascadingDrag.ReviewPosition=null;Flush();
        }
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","BRD","SHD"];owner.Level=50;owner.PetType="Earth";owner.PetLevel=50;owner.PetPreset="DPS";
        var blood=Controller.Catalog.Find("Bloodmoon")!;var ev=Controller.Catalog.Find("Evensong")!;
        var armor=new ItemDefinition{Key="synthetic-cuirass",Name="Synthetic cuirass",Slots=["CHEST"],Classes=["ALL"],Stats=new(){["HP"]=80,["AC"]=20},MetadataComplete=true,MetadataVersion=PetItemRules.MetadataVersion,IntroducedExpansion="Classic"};CatalogItemMerge.Apply(Controller.Catalog,[armor]);
        var build=new PetBuild{Preset="DPS",Name="Current build",PetType="Earth",PetLevel=50,SlotConfigurationVersion=PetSlotPlanning.Version,DualWield=true,Items=[new(){Id="a",Position=1,Slot="Storage",ItemKey=blood.Key,Name=blood.Name,Rank=4},new(){Id="b",Position=2,Slot="Storage",ItemKey=ev.Key,Name=ev.Name,Rank=5},new(){Id="c",Position=3,Slot="Storage",ItemKey=armor.Key,Name=armor.Name,Rank=0}]};owner.ConfirmedPetInventory=build;
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");petSearch.Text="";RefreshPet();Width=1400;Height=820;Flush();
        Check(!Texts(this).Contains("FIND ITEMS FOR PET INVENTORY"),"Redundant candidate title removed");
        Check(petRank.Items[0]?.ToString()=="Actual"&&petRank.Width==68&&petSlot.Width==84&&petSearch.Width==165,"Compact selectors use Actual / Set Rank / Slot dimensions");
        Check(petItems.Columns[0].ActualWidth==16,"Inventory grip remains 16 DIP including padding");
        var wing=Controller.Catalog.Find("Spiroc Wingblade")!;Check(wing.Slots.SequenceEqual(new[]{"PRIMARY","SECONDARY"})&&wing.RequiredLevel==0&&wing.EffectActivationLevel==45,"Offline Wingblade slots and activation level repaired");
        petSearch.Text="Spiroc";petKey="";RefreshPet();Flush();Check(petResults.Items.OfType<PetSearchRow>().Any(r=>r.Item.Key==wing.Key),"Spiroc Wingblade discoverable using pet class access");
        petRank.SelectedItem="4";RefreshPet();Check(petResults.Items.OfType<PetSearchRow>().All(r=>r.Rank==4),"Explicit rank updates searched candidates");petRank.SelectedIndex=0;petSearch.Text="";petKey="";RefreshPet();Flush();
        Json.Write(Path.Combine(folder,"table-dimensions.json"),new[]{petResults,petItems}.Select(t=>new{t.ActualWidth,Scroll=Usability.Tree(t).OfType<ScrollViewer>().Select(v=>new{v.Name,v.ActualWidth,v.ViewportWidth,v.ExtentWidth,Parent=v.TemplatedParent?.GetType().Name}),Columns=t.Columns.Select(c=>new{c.Header,c.ActualWidth,c.MinWidth,c.Width,Display=c.DisplayIndex})}));
        var evrow=petResults.Items.OfType<PetSearchRow>().Single(r=>r.Item.Key==ev.Key);Check(evrow.Rank==5&&evrow.Comparison?.Evaluation?.Unbuffed.Delta==0,"Actual preserves recorded rank and equipped self-comparison");petResults.SelectedItem=evrow;ShowPetComparison();
        CascadingDrag.Observe=d=>drags.Add(d);
        var type=petResults.Columns.Single(c=>c.SortMemberPath=="Type");Check(type.MinWidth<100,"Type content no longer becomes a permanent minimum");
        var headers=Usability.Tree(petResults).OfType<DataGridColumnHeader>().Where(h=>h.Column!=null).ToArray();var header=headers.Single(h=>h.Column==petResults.Columns[0]);var gripper=Usability.Tree(header).OfType<Thumb>().Single(t=>t.Name=="PART_RightHeaderGripper");
        int selected=petResults.SelectedIndex;var rows=petResults.ItemsSource;double total=petResults.Columns.Where(c=>c.Visibility==Visibility.Visible).Sum(c=>c.ActualWidth);
        Drag(gripper,-24);Check(drags.Count>0&&!drags[^1].Canceled,"Header resizing uses routed captured pointer events");
        var splitter=petOuterSplit!.Children.OfType<GridSplitter>().Single();Drag(splitter,-90);Drag(gripper,20);Drag(splitter,55);Drag(gripper,-80,true);
        Check(drags.Count==5&&drags[^1].Canceled,"Alternating panel/header drags reverse and Escape restores the snapshot");
        Check(ReferenceEquals(rows,petResults.ItemsSource)&&petResults.SelectedIndex==selected,"Alternating drags preserve rows and selection");
        Check(petResults.Columns.Where(c=>c.Visibility==Visibility.Visible).All(c=>c.ActualWidth+.75>=c.MinWidth),"Columns respect minimums after alternating drags");
        double manualType=type.ActualWidth;
        type.DisplayIndex=0;Flush();var nameLeft=Usability.Tree(header).OfType<Thumb>().Single(t=>t.Name=="PART_LeftHeaderGripper");Drag(nameLeft,12);
        Check(drags[^1].Boundary==0,"Left header edge follows visible reordered columns");
        type.DisplayIndex=1;Flush();Drag(gripper,10000);Check(drags[^1].After.Zip(drags[^1].Minimums).All(p=>p.First+.01>=p.Second),"Routed extreme drag respects all column minimums");
        Drag(gripper,-22,captureLoss:true);Check(drags[^1].Canceled,"Capture loss cancels routed resizing cleanly");
        double savedName=Controller.Profile.ColumnWidths["tracker.pet.candidates:Name"];Width=1050;Flush();Width=1400;Flush();Check(Controller.Profile.ColumnWidths["tracker.pet.candidates:Name"]==savedName,"Panel/window fitting does not overwrite saved column preferences");
        var resultScroll=Usability.Tree(petResults).OfType<ScrollViewer>().First(v=>ReferenceEquals(v.TemplatedParent,petResults));resultScroll.ScrollToVerticalOffset(50);Flush();double offset=resultScroll.VerticalOffset;Drag(gripper,-10);Check(Math.Abs(resultScroll.VerticalOffset-offset)<1,"Routed column drag retains scroll position");
        TableLayout.Reset(petResults);TableLayout.Reset(petItems);Flush();Check(!Controller.Profile.ColumnWidths.Keys.Any(k=>k.StartsWith("tracker.pet.candidates:")||k.StartsWith("tracker.pet.inventory:")),"Reset columns uses compact defaults without clearing other layouts");
        howTo.Children.Clear();var staff=Controller.Catalog.Find("Staff of Elemental Mastery: Water")!;AddProgressSources([staff.Key],[]);Check(howTo.Children.OfType<Border>().Count(b=>b.Tag is ItemSource source&&source.Mob.Contains("Phinigel"))==1,"Progress renders one Phinigel acquisition card with combined evidence");
        Width=640;Height=470;Flush();Check(petComparison.IsVisible&&petOuterSplit.ColumnDefinitions[2].ActualWidth>=219,"Narrow short Pet keeps Item Details available");
        var filterBlock=Usability.Tree(this).OfType<Expander>().Single(e=>e.Header?.ToString()=="Filter"&&e.IsVisible);filterBlock.IsExpanded=true;Flush();Check(petResults.ActualHeight>28,"Expanded filters retain a results viewport at short height");filterBlock.IsExpanded=false;
        var workspace=petOuterSplit.Parent as ScrollViewer;Check(workspace!=null&&workspace.ExtentWidth>=620,"Pet workspace scrolls below combined pane minimums");
        ShowView(TrackerTab.Gear);Flush();Check(gearInspector.IsVisible&&gearEquipment.IsVisible,"Narrow short Gear keeps all three panes available");
        CascadingDrag.Observe=null;
        var fixtureRows=Json.Read<List<PetItemRules.WikiFixture>>(Path.Combine(AppContext.BaseDirectory,"data/catalog-slot-repairs.json")).Select(f=>WikiClient.ParseItem(f.Title,f.Text,f.Revision)).ToList();
        Json.Write(Path.Combine(folder,"catalog-audit.json"),new{Parser=PetItemRules.MetadataVersion,Audited=fixtureRows.Select(i=>new{i.Name,i.Slots,i.RequiredLevel,i.EffectActivationLevel,i.MetadataComplete,i.Revision,i.WikiUrl}),Scope="All bundled definitions and indexed title coverage accounted separately; unresolved pages remain queued for resumable metadata refresh."});
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Width=1400;Height=820;ShowView(TrackerTab.Pet);petSearch.Text="Evensong";petKey="";RefreshPet();petResults.SelectedIndex=0;ShowPetComparison();Flush();
            Capture(theme+"-pet",this,1400,820);
            var filters=Usability.Tree(this).OfType<Expander>().Single(e=>e.Header?.ToString()=="Filter"&&e.IsVisible);filters.IsExpanded=true;Flush();Capture(theme+"-pet-filters",this,1400,820);filters.IsExpanded=false;Capture(theme+"-pet-narrow",this,640,680);Capture(theme+"-pet-short",this,820,470);
            ShowView(TrackerTab.Gear);Capture(theme+"-gear-narrow",this,640,680);
            ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();var quest=Controller.Catalog.Quests.Single(q=>q.Id=="brd-epic");foreach(var step in quest.Steps){var parts=QuestPresentation.Instruction(quest,step);Check(parts.Heading.Length==0||!QuestPresentation.Normalize(parts.Instruction).StartsWith(QuestPresentation.Normalize(parts.Heading)),"Bard instruction distinct: "+step.Id+" "+theme);}Capture(theme+"-quest",this,1220,800);
            var compare=new PetComparisonWindow(Controller,EquipmentPositions.Copy(build),()=>{}){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};compare.Show();await compare.Recalculate();for(int n=0;n<100&&compare.Results.Items.Count==0;n++)await Task.Delay(25);compare.StopReviewPolling();compare.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);compare.UpdateLayout();
            Check(compare.Results.Items.Count>0&&compare.Rows.All(r=>r.GearHp==80&&r.DamagePreferenceDelta==0&&r.DefensePreferenceDelta==0),"Computed cards use shared equipment and Current deltas: "+theme);
            Check(Texts(compare).Any(t=>t.StartsWith("Damage preference:"))&&Texts(compare).Any(t=>t.StartsWith("Gear HP: 80")),"Computed values lead cards rather than Unknown predictions: "+theme);Capture(theme+"-cards",compare,1180,800);Capture(theme+"-cards-narrow",compare,660,710);compare.Close();
            var options=new Grid();for(int i=0;i<3;i++)options.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            string[] names=["Theme Accent","Antique Gold","Bright Parchment"];string[] brushes=["Accent","Melee","Foreground"];
            for(int i=0;i<3;i++){var column=UI.Stack(UI.Text(names[i],15,"Accent",true));for(int state=0;state<4;state++){var e=FocusedLayout.Disclosure("Filters & Rank",UI.Text("Slot · Set Rank · Current item",11),"Review-only color treatment",state==1);e.SetResourceReference(Control.ForegroundProperty,brushes[i]);e.ApplyTemplate();column.Children.Add(UI.Text(new[]{"Collapsed","Expanded","Hover sample","Focus sample"}[state],11));column.Children.Add(e);e.UpdateLayout();int visualState=state;if(state>=2){e.Loaded+=(_,_)=>{var face=Usability.Tree(e).OfType<Border>().FirstOrDefault(b=>b.Name=="DisclosureFace");if(face!=null){if(visualState==2)face.SetResourceReference(Border.BackgroundProperty,"Selection");else{face.SetResourceReference(Border.BorderBrushProperty,"Accent");face.BorderThickness=new(1);}}};}}Grid.SetColumn(column,i);options.Children.Add(column);}
            var style=new RatingPairReviewDialog(options){Owner=this,Left=-15000,Top=-15000,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};style.Show();style.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            foreach(var expander in Usability.Tree(style).OfType<Expander>()){
                var arrow=Usability.Tree(expander).OfType<TextBlock>().FirstOrDefault(t=>t.Name=="Arrow");var label=Usability.Tree(expander).OfType<TextBlock>().FirstOrDefault(t=>t.Text=="Filters & Rank");
                Check(arrow!=null&&label!=null&&Equals(arrow.Foreground,label.Foreground),"Expander arrow and label use one brush: "+theme);
            }
            Capture(theme+"-expander-options",style,1000,550);style.Close();
        }
        Check(build.Items[0].Rank==4&&build.Items[1].Rank==5,"Preview and comparison leave inventory ranks unchanged");
        Json.Write(Path.Combine(folder,"compact-pet-checks.json"),new{Passed=checks.Count,Checks=checks,Drags=drags,Timings=timings,Scope="Synthetic routed mouse events with injected coordinates, real capture and shared handlers; actual WPF renders at 100/150/200/300%. Physical DPI / live-game acceptance separate."});
    }
}
