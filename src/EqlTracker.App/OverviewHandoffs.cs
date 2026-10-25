using EqlTracker.Core;
namespace EqlTracker.App;

public partial class App
{
    internal void OpenOverview(){var bar=EnsureWorkspaceToolbar();if(bar.Open("Overview")&&!NativeSetup.GameRunning())bar.FocusWhenShown("Overview");}
    internal void OpenOverviewCombat(string session)
    {var bar=EnsureWorkspaceToolbar();if(!bar.Open("CombatLab"))return;Controller.SetLabSession(session);dps!.ShowView(CombatLabMode.Damage,CombatLabTab.Metrics);dps!.Refresh();bar.FocusWhenShown("CombatLab");}
    internal void OpenOverviewItem(string name)
    {var bar=EnsureWorkspaceToolbar();if(!bar.Open("Tracker"))return;quests!.ShowView(TrackerTab.Gear,name);bar.FocusWhenShown("Tracker");}
    internal void OpenOverviewProgress(JournalActivity row)
    {
        var bar=EnsureWorkspaceToolbar();if(!bar.Open("Tracker"))return;
        var quest=Controller.Catalog.Quests.FirstOrDefault(q=>row.Name.Contains(q.Name,StringComparison.OrdinalIgnoreCase)||q.Steps.Any(s=>row.Name.Contains(s.Id,StringComparison.Ordinal)));
        quests!.ShowView(TrackerTab.Progress,quest?.Name);bar.FocusWhenShown("Tracker");
    }
}
