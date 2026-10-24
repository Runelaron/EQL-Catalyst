using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private readonly CheckBox progressByItem = new() { Content = "By item", ToolTip = "Browse quests connected to your inventory items" };
    private readonly CheckBox progressByZone=new(){Content="By zone",ToolTip="Browse documented quest locations"};
    private readonly TextBlock progressSelectorLabel = UI.Label("Quest"), progressSearchLabel = UI.Label("Find quest / reward");
    private Button progressOpen = null!, progressMore = null!;
    private MenuItem progressCompleteStep = null!, progressCompleteQuest = null!,progressOptional=null!;
    private Button progressCollapse=null!;
    private bool ProgressHasExpandedBranches()=>progressNodes.SelectMany(n=>n.Descendants()).Any(n=>n.Children.Count>0&&n.IsExpanded);
    private void UpdateProgressExpansionAction()
    {
        if(progressCollapse==null)return;
        bool collapse=ProgressHasExpandedBranches();progressCollapse.Content=collapse?"Collapse all":"Expand all";
        progressCollapse.ToolTip=collapse?"Collapse every quest branch":"Expand every quest branch";
        progressCollapse.IsEnabled=progressNodes.Any(n=>n.Children.Count>0);
    }
    private UIElement BuildProgressSelector()
    {
        var group=new Grid{MinWidth=300,Margin=new(0,0,5,4),VerticalAlignment=VerticalAlignment.Bottom};
        group.RowDefinitions.Add(new(){Height=GridLength.Auto});group.RowDefinitions.Add(new(){Height=new(28)});
        var heading=new Grid();heading.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        heading.Children.Add(progressSelectorLabel);
        var checks=UI.Row(progressByItem,progressByZone,progressTracked);checks.HorizontalAlignment=HorizontalAlignment.Right;
        foreach(var check in checks.Children.OfType<CheckBox>()){check.Margin=new(7,0,0,0);check.VerticalAlignment=VerticalAlignment.Center;}
        Grid.SetColumn(checks,1);heading.Children.Add(checks);group.Children.Add(heading);
        questSelection.Width=double.NaN;questSelection.HorizontalAlignment=HorizontalAlignment.Stretch;Grid.SetRow(questSelection,1);group.Children.Add(questSelection);
        progressTree.AddHandler(TreeViewItem.ExpandedEvent,new RoutedEventHandler((_,_)=>Dispatcher.BeginInvoke(UpdateProgressExpansionAction)));
        progressTree.AddHandler(TreeViewItem.CollapsedEvent,new RoutedEventHandler((_,_)=>Dispatcher.BeginInvoke(UpdateProgressExpansionAction)));
        return group;
    }
    private void SetProgressOptional(bool enabled){if(ProgressQuest is not {} q)return;Controller.Profile.Routes[q.Id]=enabled?"Alternative":"Default";Changed();}
    private sealed class BrowseState(bool tracked)
    {
        internal bool Tracked = tracked;
        internal string Query = "", Choice = "", Node = "";
        internal readonly Dictionary<string, bool> Expanded = [];
    }
    private readonly BrowseState questBrowse = new(true), itemBrowse = new(false),zoneBrowse=new(false);
    private ProgressBrowseMode progressMode;
    private BrowseState Browse => progressMode switch{ProgressBrowseMode.Item=>itemBrowse,ProgressBrowseMode.Zone=>zoneBrowse,_=>questBrowse};
    private ProgressBrowseChoice? ProgressChoice => questSelection.SelectedItem as ProgressBrowseChoice;
    private ProgressBrowseIndex? progressIndex;
    private string progressIndexVersion = "", progressCharacter = "", progressTreeChoice = "";
    private bool progressSyncing;
    private QuestDefinition? ProgressQuest => selectedStep is {} node ? Controller.Catalog.Quests.FirstOrDefault(q => q.Id == node.QuestId) : null;

    private UIElement ProgressField(TextBlock label, FrameworkElement control)
    {
        var group = (Grid)UI.Field("", control);
        group.Children.RemoveAt(0);group.Children.Insert(0,label);
        return group;
    }
    private void ConfigureProgressBrowse()
    {
        questSelection.DisplayMemberPath = nameof(ProgressBrowseChoice.Label);
        var row = new Style(typeof(ComboBoxItem),TryFindResource(typeof(ComboBoxItem)) as Style);row.Setters.Add(new Setter(ToolTipProperty,new Binding(nameof(ProgressBrowseChoice.Evidence))));questSelection.ItemContainerStyle = row;
        foreach (var control in new Control[] { progressSearch, questSelection, progressOpen, progressMore })
        { control.Height = control.MinHeight = 28;control.Margin = new(0);control.VerticalContentAlignment=VerticalAlignment.Center; }
        progressOpen.Padding=progressMore.Padding=new(8,0,8,0);
        progressByItem.Checked += (_,_) => {if(!progressSyncing)SwitchProgressMode(ProgressBrowseMode.Item);};
        progressByItem.Unchecked += (_,_) => {if(!progressSyncing&&progressMode==ProgressBrowseMode.Item)SwitchProgressMode(ProgressBrowseMode.Quest);};
        progressByZone.Checked+=(_,_)=>{if(!progressSyncing)SwitchProgressMode(ProgressBrowseMode.Zone);};
        progressByZone.Unchecked+=(_,_)=>{if(!progressSyncing&&progressMode==ProgressBrowseMode.Zone)SwitchProgressMode(ProgressBrowseMode.Quest);};
        System.Windows.Automation.AutomationProperties.SetName(questSelection,"Quest");
        questSelection.SelectionChanged += (_,_) => { if(initializing||progressSyncing)return;Browse.Choice=ProgressChoice?.Key??"";questSelection.ToolTip=progressMode==ProgressBrowseMode.Item?(ProgressChoice?.Evidence??"No quest-related physical items match"):"Choose a quest";stepFingerprint=null;RefreshSteps(); };
        progressSearch.TextChanged += (_,_) => { if(initializing||progressSyncing)return;Browse.Query=progressSearch.Text;RefreshSteps(); };
        progressTracked.Click += (_,_) => SetProgressTracking(progressTracked.IsChecked==true);

    }
    private void RememberProgressBrowse()
    {
        Browse.Query=progressSearch.Text;Browse.Tracked=progressTracked.IsChecked==true;Browse.Choice=ProgressChoice?.Key??"";Browse.Node=selectedStep?.Id??"";
        foreach(var node in progressNodes.SelectMany(n=>n.Descendants()))Browse.Expanded[node.Node.Id]=node.IsExpanded;
    }
    private void SwitchProgressMode(ProgressBrowseMode mode)
    {
        if(progressSyncing||progressMode==mode)return;
        RememberProgressBrowse();progressRewardSlide?.Dismiss(true);progressMode=mode;progressSyncing=true;
        progressByItem.IsChecked=mode==ProgressBrowseMode.Item;progressByZone.IsChecked=mode==ProgressBrowseMode.Zone;progressSearch.Text=Browse.Query;progressTracked.IsChecked=Browse.Tracked;progressAll.IsChecked=!Browse.Tracked;
        progressSelectorLabel.Text=mode==ProgressBrowseMode.Item?"ITEM":mode==ProgressBrowseMode.Zone?"ZONE":"QUEST";progressSearchLabel.Text=mode==ProgressBrowseMode.Item?"FIND ITEM / QUEST":mode==ProgressBrowseMode.Zone?"FIND ZONE / QUEST":"FIND QUEST / REWARD";
        progressSearch.ToolTip=mode==ProgressBrowseMode.Item?"Search inventory items and their related quests":mode==ProgressBrowseMode.Zone?"Search zone names, aliases and related quests/items":"Search quests and rewards";
        System.Windows.Automation.AutomationProperties.SetName(questSelection,mode==ProgressBrowseMode.Item?"Inventory item":mode==ProgressBrowseMode.Zone?"Zone":"Quest");
        progressSyncing=false;progressQuestFilter="";stepFingerprint=null;progressTreeChoice="";RefreshSteps();
    }
    private void SetProgressTracking(bool tracked)
    {
        Browse.Tracked=tracked;progressTracked.IsChecked=tracked;progressAll.IsChecked=!tracked;RefreshSteps();
    }
    private void SelectProgressQuest(string name)
    {
        SwitchProgressMode(ProgressBrowseMode.Quest);
        var quest=Controller.Catalog.Quests.FirstOrDefault(q=>q.Id==name||q.Name==name);
        if(quest==null)return;
        progressSyncing=true;progressSearch.Clear();Browse.Query="";
        if(!QuestTracking.Selected(quest,Controller.Profile,Controller.Inventory,Controller.Catalog)){Browse.Tracked=false;progressTracked.IsChecked=false;progressAll.IsChecked=true;}
        Browse.Choice=quest.Id;progressSyncing=false;progressQuestFilter="";FilterProgressQuests();
        questSelection.SelectedItem=questSelection.Items.Cast<ProgressBrowseChoice>().FirstOrDefault(c=>c.Key==quest.Id);stepFingerprint=null;
    }
    private void FilterProgressQuests()
    {
        string character=Controller.Profile.Character+"|"+Controller.Profile.Server;
        if(progressCharacter!=character)
        {
            progressRewardSlide?.Dismiss(true);progressCharacter=character;progressIndexVersion="";progressQuestFilter="";progressTreeChoice="";stepFingerprint=null;
            foreach(var state in new[]{questBrowse,itemBrowse,zoneBrowse}){state.Query=state.Choice=state.Node="";state.Expanded.Clear();}
            questBrowse.Tracked=true;itemBrowse.Tracked=zoneBrowse.Tracked=false;
            progressSyncing=true;progressSearch.Clear();progressTracked.IsChecked=Browse.Tracked;progressAll.IsChecked=!Browse.Tracked;progressSyncing=false;
        }
        string indexVersion=$"{Controller.CatalogRevision}|{Controller.QuestRevision}|{Controller.Catalog.Quests.Count}|{Controller.Catalog.Items.Count}";
        if(progressIndexVersion!=indexVersion){progressIndexVersion=indexVersion;progressIndex=new(Controller.Catalog);}
        string key=$"{progressMode}|{progressSearch.Text}|{progressTracked.IsChecked}|{indexVersion}|{Controller.Inventory.Revision}|{Controller.Inventory.CapturedAt.Ticks}";
        if(progressQuestFilter==key)return;progressQuestFilter=key;
        var existing=questSelection.Items.OfType<ProgressBrowseChoice>().ToDictionary(c=>c.Key);
        var choices=progressIndex!.Choices(progressMode,Controller.Profile,Controller.Inventory,progressTracked.IsChecked==true,progressSearch.Text)
            .Select(c=>existing.TryGetValue(c.Key,out var old)&&old.Label==c.Label&&old.Evidence==c.Evidence&&old.Quests.SequenceEqual(c.Quests)?old:c).ToList();
        string selected=Browse.Choice.Length>0?Browse.Choice:progressMode==ProgressBrowseMode.Zone?Identity.ZoneKey(Controller.Profile.Zone):"";
        progressSyncing=true;questSelection.ItemsSource=choices;questSelection.SelectedItem=choices.FirstOrDefault(c=>c.Key==selected)??choices.FirstOrDefault();progressSyncing=false;
        Browse.Choice=ProgressChoice?.Key??"";
        questSelection.ToolTip=progressMode==ProgressBrowseMode.Item?(ProgressChoice?.Evidence??"No quest-related physical items match"):"Choose a quest";
    }
    private void UpdateProgressQuestContext()
    {
        var quest=ProgressQuest;
        bool valid=quest!=null;progressOpen.IsEnabled=valid;route.IsEnabled=valid;progressCompleteQuest.IsEnabled=valid;UpdateProgressExpansionAction();progressOptional.IsEnabled=quest?.Steps.Any(s=>s.Optional&&s.RouteGroup.Length==0)==true;progressOptional.IsChecked=valid&&Controller.Profile.Routes.GetValueOrDefault(quest!.Id,"Default")=="Alternative";progressOptional.ToolTip=progressOptional.IsEnabled?"Include optional steps for this quest":"This quest has no optional steps";progressCompleteStep.IsEnabled=valid&&selectedStep?.StepId.Length>0;
        progressOpen.ToolTip=valid?"Open this quest's walkthrough":"Select a quest or step first";
        progressCompleteQuest.ToolTip=valid?"Complete or reopen the selected quest":"Select a quest first";
        progressCompleteStep.ToolTip=progressCompleteStep.IsEnabled?"Complete or reopen the selected step":"Select a quest step first";
        if(quest==null){coverage.Text="";progressReward.Children.Clear();rewardActions.Clear();progressRewardQuest=progressRewardButtons="";return;}
        coverage.Text=quest.Coverage+$" · wiki revision {quest.Revision}";
        if(progressMode==ProgressBrowseMode.Item&&ProgressChoice is {} choice&&progressIndex!.AlternateRoutes(quest,Controller.Profile,choice.ItemKey) is {Length:>0} routes)
            coverage.Text+="\nSelected item is used on alternate route: "+routes+". Choose that route to view its steps.";
        bool was=initializing;initializing=true;route.SelectedIndex=Controller.Profile.Routes.GetValueOrDefault(quest.Id,"Default")=="Alternative"?1:0;initializing=was;
        progressTree.ToolTip=coverage.Text;RefreshProgressRewards(quest);
    }
}
