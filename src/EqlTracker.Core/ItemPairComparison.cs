namespace EqlTracker.Core;

public sealed record ItemPairStat(string Stat, double? Current, double? Candidate)
{ public double? Change => Current.HasValue && Candidate.HasValue ? Candidate - Current : null; }
public sealed record ItemPairSide(string Name, int Rank, IReadOnlyDictionary<string, double> Stats, IReadOnlyList<string> Effects, string Restrictions)
{ public ItemDefinition? Definition { get; init; } public InventoryEntry? Instance { get; init; } }
public sealed record ItemPairComparison(ItemPairSide Current, ItemPairSide Candidate, IReadOnlyList<ItemPairStat> Differences, string Warning)
{
    public static ItemPairComparison Create(ItemDefinition? current, InventoryEntry? currentInstance, ItemDefinition candidate, int rank, InventoryEntry? instance, LoadoutProfile context, Catalog catalog, bool pet = true)
    {
        int currentRank = currentInstance?.Rank ?? 0;
        var before = current == null ? new Dictionary<string, double>() : GearScorer.EffectiveStats(current, currentRank);
        var after = GearScorer.EffectiveStats(candidate, rank);
        var changes = before.Keys.Union(after.Keys).OrderBy(s => s).Select(s => new ItemPairStat(s, before.TryGetValue(s, out double a) ? a : current?.MetadataComplete == true ? 0 : null, after.TryGetValue(s, out double b) ? b : candidate.MetadataComplete ? 0 : null)).ToList();
        string eligibility = GearScorer.Eligibility(candidate, context, pet, instance?.Augments, catalog);
        foreach (var effect in instance?.Augments ?? [])
        {
            string type = ExaltationPlanner.EffectType(effect.EffectText);
            if (type != "Unknown" && rank < ExaltationPlanner.Unlock(type)) eligibility = "Exaltation requires destination +" + ExaltationPlanner.Unlock(type);
        }
        string warning = current == null ? "Current item metadata is unavailable. Select an inventory item to compare." : !candidate.MetadataComplete || !current.MetadataComplete ? "Some metadata is missing; unknown values are shown as —." : eligibility != "Eligible" ? eligibility + " · raw item differences only; this is not an upgrade recommendation." : !current.Slots.Select(EquipmentPositions.Normalize).Intersect(candidate.Slots.Select(EquipmentPositions.Normalize)).Any() ? "These items have no shared wearable slot · raw differences only." : "";
        ItemPairSide Side(ItemDefinition? item, int tier, InventoryEntry? physical) => new(item?.Name ?? currentInstance?.Name ?? "No current item selected", tier, item == null ? new Dictionary<string, double>() : GearScorer.EffectiveStats(item, tier), item == null ? [] : ItemEffects.Active(item, physical?.TransferredOutEffects).Concat(physical?.Augments.Select(a => a.EffectText.Length > 0 ? a.EffectText : a.Name) ?? []).ToArray(), item == null ? "Metadata unknown" : ItemPresentation.Slots(item.Slots) + " · " + string.Join(" / ", item.Classes));
        return new(Side(current, currentRank, currentInstance) with { Definition = current, Instance = currentInstance }, Side(candidate, rank, instance) with { Definition = candidate, Instance = instance }, changes, warning);
    }
}
