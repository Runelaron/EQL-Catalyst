using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    private HashSet<string> recordedSteps=[],recordedQuests=[];
    private Dictionary<string,string> recordedPetPlans=[];
    private Dictionary<string,string> PetPlanKeys()=>Profile.Loadouts.ToDictionary(l=>l.Name,l=>PetComparisonEngine.Key(l.PetBuilds.OrderBy(b=>b.Id).Select(b=>PetWorkspaceDraft.Key(b,true)).ToArray()));
    private void InitializeJournalChanges()
    { recordedSteps=Profile.CompletedSteps.ToHashSet();recordedQuests=Profile.CompletedQuests.ToHashSet();recordedPetPlans=PetPlanKeys(); }
    private string ProgressName(string id)
    {var quest=Catalog.Quests.FirstOrDefault(q=>q.Steps.Any(s=>s.Id==id));var step=quest?.Steps.FirstOrDefault(s=>s.Id==id);return quest==null?id:quest.Name+" · "+step?.Name;}
    private void ObserveJournalChanges()
    {
        foreach(string step in Profile.CompletedSteps.Except(recordedSteps))Journal(new(){Id="checklist:"+Guid.NewGuid().ToString("N"),At=DateTime.Now,Kind="Progress",Name="Checked · "+ProgressName(step),Source="Saved checklist state change"});
        foreach(string step in recordedSteps.Except(Profile.CompletedSteps))Journal(new(){Id="checklist:"+Guid.NewGuid().ToString("N"),At=DateTime.Now,Kind="Progress",Name="Unchecked · "+ProgressName(step),Source="Saved checklist state change"});
        foreach(string quest in Profile.CompletedQuests.Except(recordedQuests))Journal(new(){Id="quest:"+Guid.NewGuid().ToString("N"),At=DateTime.Now,Kind="Progress",Name="Completed · "+(Catalog.Quests.FirstOrDefault(q=>q.Id==quest)?.Name??quest),Source="Saved quest completion state"});
        var plans=PetPlanKeys();foreach(var (owner,key) in plans)if(recordedPetPlans.TryGetValue(owner,out string? prior)&&prior!=key)Journal(new(){Id="planned-build:"+Guid.NewGuid().ToString("N"),At=DateTime.Now,Kind="Planned setup",Name="Saved pet build edited · "+owner,Source="Catalyst plan only; no equipment change is inferred"});
        recordedSteps=Profile.CompletedSteps.ToHashSet();recordedQuests=Profile.CompletedQuests.ToHashSet();recordedPetPlans=plans;
    }
}
