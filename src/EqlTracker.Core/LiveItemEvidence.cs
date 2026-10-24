namespace EqlTracker.Core;

public static class CandidateRanks
{
    public static int Resolve(InventoryEntry? instance, int? preview) => preview is >= 0 and <= 10 ? preview.Value : instance?.Rank ?? 0;
}

public sealed class ItemRankObservation
{
    public string Id { get; set; } = "";
    public DateTime Time { get; set; }
    public string Name { get; set; } = "";
    public string Result { get; set; } = "";
    public string Status { get; set; } = "Unresolved physical copy";
}

public static class AcquiredQuestTracking
{
    // Ownership changes only select quests. They never edit completion or queue order.
    public static int Apply(Catalog catalog, CharacterProfile profile, InventorySnapshot inventory, IEnumerable<string>? acquired = null)
    {
        var keys = acquired?.Select(n => catalog.Find(n)?.Key ?? Identity.Key(n)).ToHashSet();
        int added = 0;
        foreach (var quest in catalog.Quests)
        {
            if (profile.QuestTracking.ContainsKey(quest.Id) || profile.CompletedQuests.Contains(quest.Id) ||
                !QuestTracking.Relevant(quest, profile.ActiveLoadout.Classes.Select(CastingPriority.Code)) || !ExpansionRules.Planned(quest, profile)) continue;
            var components = quest.Requirements.Concat(quest.Steps.SelectMany(s => s.Inputs)).Select(i => i.ItemKey).Distinct();
            if (components.Any(k => (keys == null || keys.Contains(k)) && catalog.FindKey(k) is {} item &&
                inventory.Evidence(item).Any(e => !e.IsAugment && !e.IsCollection && e.Count > 0)) && !QuestTracking.Fulfilled(quest, profile, inventory, catalog))
            { QuestTracking.Track(profile, quest, true); added++; }
        }
        return added;
    }
}

public sealed record AcquisitionFact(string Kind, string Name, string Zone, string Detail, string Url);
public static class ItemAcquisition
{
    public static IReadOnlyList<AcquisitionFact> For(ItemDefinition item, Catalog catalog, InventorySnapshot inventory)
    {
        var rows = new List<AcquisitionFact>();
        if (inventory.Evidence(item).Count > 0) rows.Add(new("Inventory", "Recorded possession", "", inventory.Ownership(item), ""));
        foreach (var quest in catalog.Quests.Where(q => q.RewardKey == item.Key || q.RewardAlternatives.Any(a => a.Contains(item.Key))))
            rows.Add(new("Quest", quest.Name, quest.StartZone.Length > 0 ? quest.StartZone : "Starting zone unknown", quest.StartNpc.Length > 0 ? "Starts with: " + quest.StartNpc : "Starting NPC unverified", quest.WikiUrl));
        foreach (var source in item.Sources.OrderByDescending(s=>s.Revision))
            rows.Add(new(source.Method.Length > 0 ? source.Method : "Source", source.Mob.Length > 0 ? source.Mob.Replace('_', ' ') : "Source unverified", source.Zone, source.Availability + (source.Notes.Length>0?" · "+source.Notes:"") + (source.Revision>0?" · revision "+source.Revision:""), source.WikiUrl.Length > 0 ? source.WikiUrl : item.WikiUrl));
        foreach(var recipe in item.CraftRecipes.Where(r=>r.Outputs.Any(o=>o.ItemKey==item.Key)))rows.Add(new("Craft",recipe.Name,recipe.Container.Length>0?recipe.Container:"Container undocumented",recipe.Instructions,recipe.WikiUrl));
        return rows.DistinctBy(r => (r.Kind, r.Name, Identity.ZoneKey(r.Zone))).ToList();
    }
}
