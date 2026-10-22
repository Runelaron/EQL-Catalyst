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
    internal async Task ReviewProgressRewards(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(Path.Combine(folder,"qa"));var checks=new List<string>();var timings=new List<object>();var animation=new List<object>();
        void Check(bool v,string text){if(!v)throw new InvalidOperationException(text);checks.Add(text);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        string[] Texts(DependencyObject root)=>Usability.Tree(root).OfType<TextBlock>().Select(t=>t.Text).ToArray();
        void Pointer(UIElement control){control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});}
        void Click(ButtonBase b){Pointer(b);if(b is ToggleButton)typeof(ToggleButton).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(b,null);else b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));b.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});Flush();}
        void Escape(){progressRewardSlide!.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush();}
        void Capture(string name,double width,double height)
        {
            if(Environment.GetCommandLineArgs().Contains("--checks-only"))return;
            Width=width;Height=height;Flush();foreach(double scale in new[]{1d,1.5,2d,3d}){var clock=Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();var bitmap=PreviewRenderer.Capture((FrameworkElement)Content,width,height,scale);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(folder,"qa",name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png")))png.Save(f);timings.Add(new{Name=name,Scale=scale,Milliseconds=clock.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes});}
        }
        if(Environment.GetCommandLineArgs().Contains("--reward-styles-only"))
        {
            foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
            {
                Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);
                var board=UI.Card(UI.Row(RewardStyleSamples.Create("Raised Lettering"),RewardStyleSamples.Create("Inset Plaque"),RewardStyleSamples.Create("Engraved Text")));board.Background=UI.Brush("Surface");
                foreach(double scale in new[]{1d,1.5,2d,3d}){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(board,810,390,scale)));using var file=File.Create(Path.Combine(folder,"qa",theme+"-reward-options-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x.png"));png.Save(file);}
            }
            Json.Write(Path.Combine(folder,"reward-styles-review.json"),new{Styles=3,Themes=3,Scales=4,ReviewOnly=true,ProductionUnchanged=true});return;
        }
        GearBuildFixtures.Apply(Controller);Controller.Profile.CurrentClasses=["BRD","MAG","SHD"];Controller.Profile.ActiveLoadout.Classes=["BRD","MAG","SHD"];Controller.Profile.QuestTracking["brd-epic"]=true;Controller.Profile.QuestTracking["mag-epic"]=true;Controller.Profile.SmoothMeterMotion=false;
        Controller.Save();Width=1220;Height=800;ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();
        var bard=Controller.Catalog.Quests.Single(q=>q.Id=="brd-epic");var singing=Controller.Catalog.Find("Singing Short Sword")!;
        Check(progressFooter!=null&&Math.Abs(progressFooter.ColumnDefinitions[0].ActualWidth-progressFooter.ColumnDefinitions[2].ActualWidth)<1,"Footer halves share available width");
        Check(progressFooter!.ActualHeight<=96&&progressFooter.Children.OfType<Border>().Select(b=>b.TranslatePoint(new(),progressFooter).Y).Distinct().Count()==1,"Status and Reward sit on one capped-height row");
        Check(Texts(rewardActions[singing.Key]).Contains("Singing Short Sword")&&!Texts(rewardActions[singing.Key]).Any(t=>t.Contains("System.Windows")),"Reward buttons render actual item names, not control type names");
        var rootNode=selectedStep!;var howScroll=UI.FindScroll(howTo.Parent as DependencyObject??howTo)!;howScroll.ScrollToVerticalOffset(90);Flush();double offset=howScroll.VerticalOffset;var originalChildren=howTo.Children.Cast<UIElement>().ToArray();
        Click(rewardActions[singing.Key]);Check(progressRewardSlide!.IsOpen&&!progressRewardSlide.IsAnimating,"Motion disabled opens reward facts immediately");
        Check(Texts(progressRewardSlide.Surface).Contains("Singing Short Sword")&&Usability.Tree(progressRewardSlide.Surface).OfType<ClassicItemCard>().Count()==1,"Reward uses the shared item header and classic card");
        Check(!Usability.Tree(progressRewardSlide.Surface).OfType<Slider>().Any()&&!Texts(progressRewardSlide.Surface).Any(t=>t.Contains("Upgrade rating")),"Reward inspector is read-only without preview or rating controls");
        Check(originalChildren.SequenceEqual(howTo.Children.Cast<UIElement>()),"Walkthrough remains mounted beneath reward facts");Pointer(progressRewardSlide.Copies);Check(progressRewardSlide.IsOpen,"Inside reward interaction keeps slide-over open");
        Escape();Check(!progressRewardSlide.IsOpen&&Math.Abs(howScroll.VerticalOffset-offset)<1&&selectedStep?.Id==rootNode.Id,"Escape restores walkthrough selection and scroll");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(this),rewardActions[singing.Key])||rewardActions[singing.Key].IsKeyboardFocusWithin,"Escape restores reward action focus");
        int outsideActions=0;var outside=UI.Button("Review outside action",()=>outsideActions++);progressReward.Children.Add(outside);Click(rewardActions[singing.Key]);Click(outside);Check(!progressRewardSlide.IsOpen&&outsideActions==1,"Outside routed click dismisses and performs its original action");progressReward.Children.Remove(outside);
        Controller.Inventory.Entries.Add(new(){Name="Singing Short Sword +14",Count=1,Location="Bank"});Controller.Inventory.Entries.Add(new(){Name="Singing Short Sword +4",Count=1,Location="Bag"});Controller.Inventory.Invalidate();
        Click(rewardActions[singing.Key]);Check(Texts(progressRewardSlide.Surface).Contains("Singing Short Sword +14"),"Highest recorded physical copy is initially selected, including ranks above ten");
        progressRewardSlide.Copies.SelectedIndex=0;Flush();Check(Texts(progressRewardSlide.Surface).Contains("Singing Short Sword")&&!Texts(progressRewardSlide.Surface).Contains("Singing Short Sword +14"),"Catalog choice uses +0 without changing recorded ownership");
        progressRewardSlide.Copies.SelectedIndex=2;Flush();Check(Texts(progressRewardSlide.Surface).Contains("Singing Short Sword +4"),"Physical-copy selection displays its actual rank");
        Controller.Inventory.Invalidate();RefreshSteps();Flush();Check(progressRewardSlide.IsOpen&&progressRewardSlide.ItemKey==singing.Key,"Same-quest refresh preserves open reward and selected copy");
        progressRewardSlide.Dismiss(true);ShowView(TrackerTab.Progress,"Magician Epic Quest");RefreshSteps();Flush();var staff=Controller.Catalog.Find("Staff of Elemental Mastery: Water")!;
        Check(SelectProgressNode(n=>n.Kind!=QuestNodeKind.Quest&&n.ItemKeys.Contains(staff.Key)),"Magician branch exposes the supplied staff case");Flush();var text=Texts(howTo);var cards=howTo.Children.OfType<Border>().Where(b=>b.Tag is ItemSource s&&s.Mob.Contains("Phinigel")).ToArray();
        Check(cards.Length==1,"Exactly one Phinigel acquisition card is rendered");var facts=Texts(cards[0]);Check(Array.IndexOf(facts,"Phinigel Autropos")<Array.IndexOf(facts,"Kedge Keep")&&Array.IndexOf(facts,"Kedge Keep")<Array.IndexOf(facts,"Drop"),"Drop card order is Mob then Zone then Drop");
        Check(text.Contains("HOW TO OBTAIN")&&!text.Any(t=>t.StartsWith("Kill Phinigel Autropos in Kedge Keep",StringComparison.OrdinalIgnoreCase)),"How to obtain leads the card and redundant drop prose is absent");
        Check(text.Contains("Raid target")&&Usability.Tree(cards[0]).OfType<Expander>().Any(e=>e.Header?.ToString()=="Evidence"),"Raid qualification and source evidence remain accessible");
        ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();var alternatives=EquipmentPositions.Copy(bard.RewardAlternatives);bard.RewardAlternatives.Add(["pending-reward",staff.Key]);RefreshProgressRewards(bard);Flush();
        Check(rewardActions.Count==3,"Multiple alternative rewards remain separately selectable");Click(rewardActions["pending-reward"]);Check(Texts(progressRewardSlide.Surface).Any(t=>t.Contains("metadata pending")),"Missing reward metadata displays an honest pending state");
        Click(rewardActions[staff.Key]);CatalogItemMerge.Apply(Controller.Catalog,[new(){Key="pending-reward",Name="Delayed synthetic reward",MetadataComplete=true,Revision=1,Classes=["ALL"],Slots=["FINGER"],Stats=new(){["AC"]=3}}]);progressRewardSlide.Refresh();Check(progressRewardSlide.ItemKey==staff.Key&&!Texts(progressRewardSlide.Surface).Contains("Delayed synthetic reward"),"Late metadata cannot replace the newly selected reward");
        RefreshProgressRewards(bard);Click(rewardActions["pending-reward"]);Check(Texts(progressRewardSlide.Surface).Contains("Delayed synthetic reward"),"Repaired metadata refreshes the selected reward");
        string character=Controller.Profile.Character;Controller.Profile.Character="AnotherSyntheticHero";progressRewardSlide.Refresh();Check(!progressRewardSlide.IsOpen,"Character change closes the old reward context");Controller.Profile.Character=character;
        RefreshProgressRewards(bard);Click(rewardActions[singing.Key]);ShowView(TrackerTab.Items);Flush();Check(!progressRewardSlide.IsOpen,"Changing tabs closes the slide-over immediately");
        ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();Controller.Profile.SmoothMeterMotion=true;Click(rewardActions[singing.Key]);await Task.Delay(45);Flush();animation.Add(new{State="opening",Offset=progressRewardSlide.SlideOffset,Animating=progressRewardSlide.IsAnimating,WindowsAnimation=SystemParameters.ClientAreaAnimation});
        Check(!SystemParameters.ClientAreaAnimation||progressRewardSlide.IsAnimating,"Enabled motion uses a real WPF render-transform animation");progressRewardSlide.Dismiss();await Task.Delay(30);Click(rewardActions[staff.Key]);await Task.Delay(230);Flush();Check(progressRewardSlide.IsOpen&&progressRewardSlide.ItemKey==staff.Key&&!progressRewardSlide.IsAnimating&&Math.Abs(progressRewardSlide.SlideOffset)<.01,"Interrupted close/open finishes at the latest reward without stale completion");
        Controller.Profile.SmoothMeterMotion=false;progressRewardSlide.Dismiss(true);bard.RewardAlternatives=alternatives;RefreshProgressRewards(bard);
        Click(rewardActions[singing.Key]);Click(Usability.Tree(progressRewardSlide.Surface).OfType<Button>().Single(b=>b.Content?.ToString()=="‹ Back to walkthrough"));Check(!progressRewardSlide.IsOpen,"Back button restores the walkthrough");
        Click(rewardActions[singing.Key]);Click(Usability.Tree(progressRewardSlide.Surface).OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Close reward details"));Check(!progressRewardSlide.IsOpen,"Close control dismisses reward details");
        Click(rewardActions[singing.Key]);ShowView(TrackerTab.Progress,"Magician Epic Quest");RefreshSteps();Flush();Check(!progressRewardSlide.IsOpen,"Quest changes close the old reward immediately");
        var noReward=new QuestDefinition{Id="review:no-reward",Name="No documented reward",Class="MAG",WikiUrl="https://example.invalid/quest"};Controller.Catalog.Quests.Add(noReward);Controller.Profile.QuestTracking[noReward.Id]=true;FilterProgressQuests();ShowView(TrackerTab.Progress,noReward.Name);RefreshSteps();Flush();Check(rewardActions.Count==0&&Texts(progressReward).Contains("Reward not documented"),"A quest without documented rewards has an honest non-clickable empty state");
        Controller.Catalog.Quests.Remove(noReward);Controller.Profile.QuestTracking.Remove(noReward.Id);ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();Check(rewardActions.ContainsKey(singing.Key),"Returning from an empty reward quest restores normal reward actions");
        Controller.Inventory.Entries.Add(new(){Name="Singing Short Sword",Count=null,Location="Collection"});Controller.Inventory.Invalidate();Click(rewardActions[singing.Key]);Check(progressRewardSlide.Copies.Items.Count==3,"Collection presence never becomes a selectable physical copy");progressRewardSlide.Dismiss(true);

        await ReviewProgressBrowse(checks,timings,Capture);
        await ReviewProgressActions(folder,checks,timings,Capture);
        int audited=0;foreach(var q in Controller.Catalog.Quests){var projection=ProgressPresentation.Build(new(){Kind=QuestNodeKind.Quest,Name=q.Name},q,Controller.Profile,Controller.Catalog);foreach(var block in projection.Instructions){var panel=QuestPresentationView.Instruction(block,Controller.Catalog,true);var words=Texts(panel);Check(block.Instruction.Length==0||words.Count(w=>QuestPresentation.Normalize(w)==QuestPresentation.Normalize(block.Instruction))==1,"Rendered instruction once: "+q.Id+" / "+block.Step.Id);audited++;}}
        var counters=new List<object>();
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);Controller.Profile.SmoothMeterMotion=false;Width=1220;Height=800;
            ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();Capture(theme+"-bard",1220,800);Capture(theme+"-footer-narrow",640,670);
            Click(rewardActions[singing.Key]);Capture(theme+"-reward",1220,800);Capture(theme+"-reward-narrow",640,670);Check(progressRewardSlide.IsOpen,"Rendering keeps reward state intact: "+theme);progressRewardSlide.Dismiss(true);
            Width=1220;Height=800;ShowView(TrackerTab.Progress,"Magician Epic Quest");RefreshSteps();SelectProgressNode(n=>n.Kind!=QuestNodeKind.Quest&&n.ItemKeys.Contains(staff.Key));Flush();Capture(theme+"-drop",1220,800);Capture(theme+"-drop-narrow",640,670);
            Width=1220;Height=800;ShowView(TrackerTab.Quests,"Bard Epic Quest");Refresh();Flush();var checklist=libraryDetail.Children.OfType<Expander>().First(e=>e.Header?.ToString()=="Full checklist & coverage");checklist.IsExpanded=true;Capture(theme+"-checklist",1220,800);
            ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();Controller.Profile.SmoothMeterMotion=true;Click(rewardActions[singing.Key]);await Task.Delay(55);animation.Add(new{Theme=theme,State="open",Offset=progressRewardSlide.SlideOffset,Animating=progressRewardSlide.IsAnimating});await Task.Delay(200);Check(progressRewardSlide.IsOpen&&!progressRewardSlide.IsAnimating,"Animation settles in "+theme);Capture(theme+"-motion-settled",1220,800);progressRewardSlide.Dismiss();await Task.Delay(220);Controller.Profile.SmoothMeterMotion=false;
            counters.Add(new{Theme=theme,FooterWidth=progressFooter.ActualWidth,FooterHeight=progressFooter.ActualHeight});
        }
        Check(Controller.Inventory.Entries.Any(e=>e.Name=="Singing Short Sword +14")&&Controller.Inventory.Entries.Any(e=>e.Name=="Singing Short Sword +4"),"Reward browsing leaves recorded ranks intact");
        Json.Write(Path.Combine(folder,"progress-reward-checks.json"),new{Passed=checks.Count,Checks=checks,Quests=Controller.Catalog.Quests.Count,RenderedInstructionBlocks=audited,Animation=animation,Timings=timings,Footer=counters,Scope="Synthetic WPF routed clicks and keys; real animation clocks and actual 100/150/200/300% renders. Physical DPI and live-game acceptance remain separate."});
    }
}
