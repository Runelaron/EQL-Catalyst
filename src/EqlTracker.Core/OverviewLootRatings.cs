namespace EqlTracker.Core;

internal sealed record OverviewLootRating(double? Unbuffed,double? Buffed,string Slot,string Explanation)
{
    internal string Display=>"U "+Number(Unbuffed)+" / B "+Number(Buffed);
    private static string Number(double? value)=>value?.ToString("+0.0;-0.0;0.0")??"—";
}
internal sealed class OverviewLootRatings
{
    internal const string Version="overview-loot-ratings-v1";
    private readonly LoadoutEvaluationSession evaluation;
    private readonly List<EquipmentPosition> positions;
    private readonly string role;
    internal OverviewLootRatings(Catalog catalog,LoadoutProfile owner,SelfRoleResolution resolved)
    {
        positions=EquipmentPositions.Copy(EquipmentPositions.For(owner,false));role=resolved.Name;
        evaluation=new(catalog,owner,false,new(resolved.Weights),positions,positions,false,resolved);
    }
    internal static bool Equipable(ItemDefinition item)=>item.Slots.Any(s=>EquipmentPositions.IsSlot(s));
    internal OverviewLootRating Evaluate(ItemDefinition item,int? rank)
    {
        if(!rank.HasValue)return new(null,null,"","Recorded upgrade rank is unknown.");
        // A historical receipt is not proof of a particular current physical copy
        // or its sockets. Never borrow effects from an inventory item by name.
        var entry=new InventoryEntry{Name=item.Name,ConfirmedItemKey=item.Key,Count=1};
        var comparison=PetEquipmentInference.Best(item,rank.Value,entry,evaluation,positions);
        if(comparison==null)return new(null,null,"","No compatible Current equipment position is available.");
        var result=comparison.Evaluation;
        bool allowed=comparison.CanCompare;
        return new(allowed?result?.Unbuffed.Delta:null,allowed?result?.Buffed.Delta:null,comparison.Slot,
            "Current player gear now · "+role+" preference points, not predicted DPS.\nU: unbuffed; B: configured usual buffs. Both replace "+comparison.Slot+" · "+comparison.Replaces+".\n"+
            comparison.Eligibility+" · "+comparison.Confidence+"\n"+string.Join("\n",result?.Assumptions??[])+
            "\nRecorded +"+rank+". Receipt-specific Exaltations are not assumed. Acquisition history does not prove present ownership.");
    }
}
