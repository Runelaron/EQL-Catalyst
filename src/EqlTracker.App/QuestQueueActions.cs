using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private readonly List<(WeakReference<Button> Button,string Id)> queueActions=[];
    private Button QueueAction(QuestDefinition? quest,string? itemKey=null)
    {
        var goal=new QueuedGoal{Kind=itemKey==null?"Quest":"Item",QuestId=quest?.Id??"",ItemKey=itemKey??quest?.RewardKey??"",Name=itemKey==null?quest?.Name??"":Controller.Catalog.FindKey(itemKey)?.Name??itemKey};
        var button=UI.CommitButton("Queue",()=>
        {
            if(quest==null)return;
            if(Controller.Profile.GoalQueue.Any(g=>g.Id==goal.Id)){Controller.Profile.GoalQueue.RemoveAll(g=>g.Id==goal.Id);Changed();}
            else if(itemKey==null)PinQuest(quest);else PinItem(quest,itemKey);
            RefreshQueueActions();
        });
        button.IsEnabled=quest!=null;queueActions.Add((new(button),goal.Id));RefreshQueueActions();return button;
    }
    private void RefreshQueueActions()
    {
        for(int i=queueActions.Count-1;i>=0;i--)
        {
            var entry=queueActions[i];if(!entry.Button.TryGetTarget(out var button)){queueActions.RemoveAt(i);continue;}
            bool queued=Controller.Profile.GoalQueue.Any(g=>g.Id==entry.Id);UI.SetPressed(button,queued);
            string help=button.IsEnabled?(queued?"Remove this pin from Queue; keep tracking and progress":"Prioritize this goal in Queue"):"Quest identity is not documented";
            string? previous=button.ToolTip is ToolTip {Content:TextBlock text}?text.Text:button.ToolTip as string;
            if(previous!=help)button.ToolTip=help;
            System.Windows.Automation.AutomationProperties.SetHelpText(button,help);
        }
    }
}
