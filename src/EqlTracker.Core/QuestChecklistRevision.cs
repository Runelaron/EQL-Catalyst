using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal sealed record ReviewedQuestStep(string StableKey,QuestStep Step,string PriorId="");
internal static class QuestChecklistRevision
{
    // Authoring supplies reviewed semantic mappings. Wording hashes and list
    // positions cannot transfer a player's completion to a different action.
    internal static List<QuestStep> Merge(string questId,IEnumerable<QuestStep> previous,IEnumerable<ReviewedQuestStep> reviewed)
    {
        var old=previous.ToDictionary(s=>s.Id,StringComparer.Ordinal);var used=new HashSet<string>(StringComparer.Ordinal);var result=new List<QuestStep>();
        foreach(var entry in reviewed)
        {
            if(!Regex.IsMatch(entry.StableKey,@"^[a-z0-9-]+$"))throw new InvalidDataException("A reviewed quest action needs a stable semantic key.");
            string id=questId+":"+entry.StableKey;
            if(entry.PriorId.Length>0)
            {
                if(!old.ContainsKey(entry.PriorId)&&!old.ContainsKey(id))throw new InvalidDataException("Reviewed prior quest action is unavailable: "+entry.PriorId);
                if(old.ContainsKey(entry.PriorId))id=entry.PriorId;
            }
            if(!used.Add(id))throw new InvalidDataException("Duplicate reviewed quest action: "+id);
            var step=EquipmentPositions.Copy(entry.Step);step.Id=id;result.Add(step);
        }
        foreach(var original in old.Values.Where(s=>!used.Contains(s.Id)))
        {
            var step=EquipmentPositions.Copy(original);step.Optional=true;step.Inputs=[];step.Outputs=[];
            if(!step.Name.StartsWith("Earlier checklist: ",StringComparison.Ordinal))
            {
                step.Name="Earlier checklist: "+step.Name;
                step.Instructions="Retained historical checklist marker. Its saved state does not complete newly documented actions.\n"+step.Instructions;
            }
            result.Add(step);
        }
        return result;
    }
}
