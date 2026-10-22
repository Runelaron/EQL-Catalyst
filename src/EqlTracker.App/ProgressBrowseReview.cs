using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private async Task ReviewProgressBrowse(List<string> checks,List<object> timings,Action<string,double,double> capture)
    {
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Click(ButtonBase button){button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});Flush();}
        void Toggle(ToggleButton button){var peer=new ToggleButtonAutomationPeer(button);((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();Flush();}
        var inventory=EquipmentPositions.Copy(Controller.Inventory.Entries);var tracking=EquipmentPositions.Copy(Controller.Profile.QuestTracking);var routes=EquipmentPositions.Copy(Controller.Profile.Routes);var completed=EquipmentPositions.Copy(Controller.Profile.CompletedQuests);
        var token=new ItemDefinition{Key="review:token",Name="Ancient Trial Token",Aliases=["Old Trial Token"],MetadataComplete=false};
        var first=new QuestDefinition{Id="review:item-a",Name="First Token Trial",Class="MAG",RewardKey="singing-short-sword",WikiUrl="https://eqlwiki.com/Synthetic_Trial",Steps=[new(){Id="review:step-a",Name="Deliver the token",Inputs=[new(){ItemKey=token.Key}],Instructions="Take the token to the trial keeper."}]};
        var second=new QuestDefinition{Id="review:item-b",Name="Second Token Trial",Class="BRD",RewardKey="staff-of-elemental-mastery-water",WikiUrl="https://eqlwiki.com/Synthetic_Trial",Steps=[new(){Id="review:step-b",Name="Another route",Optional=true,Route="Alternative",Inputs=[new(){ItemKey=token.Key}],Instructions="Use the token on the alternate route."}]};
        // Use actual catalog reward keys, never assume a slug convention.
        first.RewardKey=Controller.Catalog.Find("Singing Short Sword")!.Key;second.RewardKey=Controller.Catalog.Find("Staff of Elemental Mastery: Water")!.Key;
        Controller.Catalog.Items.Add(token);Controller.Catalog.Quests.AddRange([first,second]);Controller.Profile.QuestTracking[first.Id]=true;Controller.Profile.QuestTracking[second.Id]=false;
        Controller.Inventory.Entries.AddRange([new(){Name="Old Trial Token +14",Count=1,Location="Bank1"},new(){Name=token.Name+" +4",Count=1,Location="Bag1"}]);Controller.Inventory.Invalidate();
        ShowView(TrackerTab.Progress,"Bard Epic Quest");progressSearch.Text="Singing";Flush();
        var controls=new Control[]{progressSearch,questSelection,progressMore};
        Check(controls.All(c=>Math.Abs(c.ActualHeight-28)<.1),"Progress Search, selector and More measure exactly 28 DIP");
        Check(controls.Select(c=>Math.Round(c.TranslatePoint(new(),this).Y,2)).Distinct().Count()==1,"Progress controls share a baseline when the complete groups fit");
        Click(progressMore);Check(progressMore.ContextMenu!.IsOpen&&Usability.GetIsPressed(progressMore),"Progress More opens with synchronized pressed state");
        foreach(bool pipeline in new[]{false,true})
        {
            if(!progressMore.ContextMenu.IsOpen)Click(progressMore);
            var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent,Source=progressMore};
            if(pipeline)InputManager.Current.ProcessInput(down);else progressMore.RaiseEvent(down);Flush();
            Check(!progressMore.ContextMenu.IsOpen&&!Usability.GetIsPressed(progressMore),"Progress More closes on second routed anchor press: "+pipeline);
            progressMore.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(!progressMore.ContextMenu.IsOpen,"Closing gesture cannot reopen Progress More: "+pipeline);
            var up=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent,Source=progressMore};if(pipeline)InputManager.Current.ProcessInput(up);else progressMore.RaiseEvent(up);
        }
        Toggle(progressByItem);Check(progressMode==ProgressBrowseMode.Item&&progressAll.IsChecked==true&&progressSearch.Text=="","By item starts independently with All and an empty search");
        questSelection.SelectedItem=questSelection.Items.Cast<ProgressBrowseChoice>().Single(c=>c.ItemKey==token.Key);Flush();
        Check(progressNodes.Select(n=>n.Node.QuestId).SequenceEqual(new[]{first.Id,second.Id}),"An owned item reveals every related quest as a distinct root");
        Check(progressNodes.SelectMany(n=>n.Descendants()).Any(n=>n.IsItemMatch&&n.Node.ItemKeys.Contains(token.Key)),"The selected inventory item has an emphasized tree branch");
        Check(ProgressChoice!.Evidence.Contains("Bank")&&Controller.Inventory.Entries.Single(e=>e.Name=="Old Trial Token +14").Rank==14,"Bank and alias copies retain actual ranks and ownership evidence");
        SelectProgressNode(n=>n.QuestId==second.Id);Flush();
        Check(ProgressQuest?.Id==second.Id&&rewardActions.ContainsKey(second.RewardKey)&&coverage.Text.Contains("alternate route"),"Reward and route context follows the selected quest root with alternate-route explanation");
        Check(!Controller.Profile.Routes.ContainsKey(second.Id),"Discovering an alternate route does not change it");
        ToggleQuest();Check(Controller.Profile.CompletedQuests.Contains(second.Id)&&!Controller.Profile.CompletedQuests.Contains(first.Id),"Complete quest acts on the selected root, not the inventory dropdown");ToggleQuest();
        route.SelectedIndex=1;Flush();Check(Controller.Profile.Routes.GetValueOrDefault(second.Id)=="Alternative","Route selection updates the selected root only");
        progressSearch.Text="Old Trial Token";SetProgressTracking(true);Flush();Check(progressNodes.Count==1&&progressNodes[0].Node.QuestId==first.Id,"Tracked filters related quests without hiding the matching item");
        Toggle(progressByItem);Check(progressSearch.Text=="Singing"&&progressTracked.IsChecked==true,"Quest search and tracking filter survive a mode switch");
        Toggle(progressByItem);Check(progressSearch.Text=="Old Trial Token"&&progressTracked.IsChecked==true&&ProgressChoice?.ItemKey==token.Key,"Item search selection and tracking filter survive a mode switch");
        var selected=selectedStep?.Id;var expanded=progressNodes[0].IsExpanded;Controller.Inventory.Invalidate();RefreshSteps();Flush();Check(selectedStep?.Id==selected&&progressNodes[0].IsExpanded==expanded,"Inventory refresh preserves item-mode selection and expansion");
        progressSearch.Text="Second Token Trial";SetProgressTracking(false);Flush();Check(ProgressChoice?.ItemKey==token.Key,"Related quest names find owned inventory items");
        var copies=Controller.Inventory.Entries.Where(e=>e.Name.Contains("Trial Token")).ToList();Controller.Inventory.Entries.RemoveAll(e=>copies.Contains(e));Controller.Inventory.Entries.Add(new(){Name=token.Name,Count=null,Location="Collection"});Controller.Inventory.Invalidate();RefreshSteps();Flush();Check(ProgressChoice==null&&progressNodes.Count==0&&!progressOpen.IsEnabled,"Losing the last physical copy removes its quests while collection presence remains excluded");
        Controller.Inventory.Entries.AddRange(copies);Controller.Inventory.Invalidate();RefreshSteps();Flush();Check(ProgressChoice?.ItemKey==token.Key&&progressNodes.Count==2,"A new loot/export observation restores the matching item and its quests immediately");
        progressSearch.Text="no such inventory quest";Flush();Check(progressNodes.Count==0&&!progressOpen.IsEnabled&&!progressCompleteQuest.IsEnabled&&!progressCompleteStep.IsEnabled&&rewardActions.Count==0,"Empty item results clear stale context and disable quest actions");
        ShowView(TrackerTab.Progress,"Bard Epic Quest");Flush();Check(progressMode==ProgressBrowseMode.Quest&&ProgressQuest?.Id=="brd-epic","Existing quest navigation explicitly returns to quest mode");
        var action=rewardActions[first.RewardKey];Toggle(action);Check(progressRewardSlide!.IsOpen&&action.IsChecked==true,"Text-only reward exposes an active native toggle state");Toggle(action);Check(!progressRewardSlide.IsOpen&&action.IsChecked==false,"Clicking the active reward closes it and clears its selected state");
        action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush();Check(progressRewardSlide.IsOpen&&action.IsChecked==true,"Enter opens the focused reward text");action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush();Check(!progressRewardSlide.IsOpen&&action.IsChecked==false,"Enter also closes the active reward text");
        Controller.Profile.SmoothMeterMotion=true;var underlay=progressRewardSlide.Underlay;var location=underlay.TranslatePoint(new(),progressRewardSlide);var scroll=UI.FindScroll(underlay)!;scroll.ScrollToVerticalOffset(65);Flush();double offset=scroll.VerticalOffset;
        Toggle(action);await Task.Delay(45);Flush();
        Check(underlay.TranslatePoint(new(),progressRewardSlide)==location&&underlay.RenderTransform.Value.IsIdentity&&Math.Abs(scroll.VerticalOffset-offset)<.1,"Walkthrough coordinates and scrolling stay fixed during opening");
        Check(Math.Abs(progressRewardSlide.UnderlayVisibleWidth-Math.Clamp(progressRewardSlide.SlideOffset,0,progressRewardSlide.ActualWidth))<.1,"Covered walkthrough text is clipped exactly under the moving reward");
        capture("slide-opening",1220,800);
        Toggle(action);await Task.Delay(35);Flush();Check(!action.IsChecked!.Value&&underlay.TranslatePoint(new(),progressRewardSlide)==location,"Repeated reward activation reverses motion without shifting the walkthrough");
        Toggle(action);await Task.Delay(220);Flush();Check(progressRewardSlide.IsOpen&&!progressRewardSlide.IsAnimating&&progressRewardSlide.UnderlayVisibleWidth==0,"Interrupted close/open settles with no underlay text bleed");
        // A tree selection performs its action while the old reward slides off above it.
        var target=progressNodes.SelectMany(n=>n.Descendants()).First(n=>n.Node.Kind!=QuestNodeKind.Quest);SelectProgressNode(n=>n.Id==target.Node.Id);Flush();
        Check(!progressRewardSlide.IsOpen&&(!SystemParameters.ClientAreaAnimation||progressRewardSlide.IsAnimating)&&selectedStep?.Id==target.Node.Id,"Quest-tree selection dismisses with animation and still selects its step");
        Check(underlay.TranslatePoint(new(),progressRewardSlide)==location,"Walkthrough does not move during tree-triggered closing");await Task.Delay(220);Flush();Check(!progressRewardSlide.IsAnimating&&progressRewardSlide.Surface.Visibility==Visibility.Collapsed,"Closing settles without a stale callback reopening reward facts");
        Controller.Profile.SmoothMeterMotion=false;
        var clock=Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();for(int n=0;n<100;n++)RefreshSteps();timings.Add(new{Name="100 unchanged Progress refreshes",Milliseconds=clock.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes});
        foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
        {
            Appearance.ApplySecondaryText(TrackerPresentation.Secondary(Controller.Profile,theme));Appearance.Apply(theme);SwitchProgressMode(ProgressBrowseMode.Item);progressSearch.Clear();SetProgressTracking(false);questSelection.SelectedItem=questSelection.Items.Cast<ProgressBrowseChoice>().Single(c=>c.ItemKey==token.Key);Flush();capture(theme+"-by-item",1220,800);capture(theme+"-by-item-narrow",640,670);
            Check(controls.All(c=>Math.Abs(c.ActualHeight-28)<.1),"Wrapped Progress controls retain 28-DIP height: "+theme);
            Width=1220;Height=800;Flush();SelectProgressNode(n=>n.QuestId==first.Id);Toggle(rewardActions[first.RewardKey]);capture(theme+"-reward-text",1220,800);Check(rewardActions[first.RewardKey].IsChecked==true,"Selected reward text survives rendering: "+theme);progressRewardSlide.Dismiss(true);
        }
        Controller.Catalog.Items.Remove(token);Controller.Catalog.Quests.RemoveAll(q=>q.Id==first.Id||q.Id==second.Id);Controller.Inventory.Entries=inventory;Controller.Inventory.Invalidate();Controller.Profile.QuestTracking=tracking;Controller.Profile.Routes=routes;Controller.Profile.CompletedQuests=completed;
        ShowView(TrackerTab.Progress,"Bard Epic Quest");RefreshSteps();Flush();
    }
}
