namespace EqlTracker.Core;

public static class StatProfilePrefill
{
    public static int MergeAAs(ICollection<GearAaModifier> rows,StatEvidenceState evidence,bool pet,ISet<string>? manualEdits=null)
    {
        int changes=0;var relevant=GearMechanicsEvidence.RelevantAAs(pet).Select(a=>Identity.Key(a.Name)).ToHashSet();
        foreach(var observed in evidence.AAs.Values.OrderBy(a=>a.Name))
        {
            string key=Identity.Key(observed.Name);if(pet && !relevant.Contains(key) || manualEdits?.Contains(key)==true)continue;
            var row=rows.FirstOrDefault(a=>Identity.Key(a.Name)==key);
            if(row==null){row=new(){Name=observed.Name,Target=pet?"Pet":"Self",Kind=AaModifierKind.Information};rows.Add(row);}
            if(row.RankEvidence.StartsWith("Manual",StringComparison.Ordinal) || row.RankObservedAt==null && row.Rank!=null || row.RankObservedAt>=observed.At)continue;
            if(row.Rank!=observed.Rank && row.Kind!=AaModifierKind.Information)row.Confirmed=false;
            row.Rank=observed.Rank;row.RankObservedAt=observed.At;
            row.RankEvidence=(observed.Rank==null?"Learned · rank unknown":"Observed rank "+observed.Rank)+$" · {observed.At:MMM/dd HH:mm}";
            // A purchase proves rank/ownership, not an EQL numeric stat formula.
            if(row.Kind==AaModifierKind.Information)row.Source=$"Game log {observed.LogId} · {observed.Evidence}";
            changes++;
        }
        return changes;
    }
    public static Dictionary<string,double> WornContributions(Catalog catalog,IEnumerable<EquipmentPosition> positions)
    {
        var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in positions.Where(p=>p.Item!=null))
        {
            var entry=p.Item!;var item=catalog.FindKey(entry.ConfirmedItemKey) ?? catalog.Find(entry.Name,entry.GameId>0?entry.GameId:null);if(item==null)continue;
            foreach(var (stat,value) in GearScorer.EffectiveStats(item,entry.Rank))
            {
                if(stat is "Damage" or "Delay" or "Ratio" or "Backstab" && p.Slot is not ("Primary" or "Secondary" or "Range"))continue;
                result[stat]=stat=="Haste"?Math.Max(result.GetValueOrDefault(stat),value):result.GetValueOrDefault(stat)+value;
            }
        }
        return result;
    }
}
