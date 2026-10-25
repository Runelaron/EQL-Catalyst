namespace EqlTracker.Core;

// Cached and fetched records can overlap, including redirect aliases in one batch.
// Fold by the stable key and refresh indexes even when the item count is unchanged.
public static class CatalogItemMerge
{
    public static void Apply(Catalog catalog, IEnumerable<ItemDefinition> incoming)
    {
        var batch = incoming.ToList();
        foreach (var item in batch) { ItemEffects.Normalize(item); ItemExpansionEvidence.Apply(item); }
        var records = catalog.Items.Concat(batch).ToList();
        var known = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (var item in records)
        {
            if (!known.TryGetValue(item.Key, out var old)) { known[item.Key] = item; continue; }
            if (ReferenceEquals(item, old)) continue;
            bool newer = (item.Revision, item.MetadataVersion, item.MetadataComplete).CompareTo((old.Revision, old.MetadataVersion, old.MetadataComplete)) >= 0;
            var selected = EquipmentPositions.Copy(newer ? item : old); var other = newer ? old : item;
            selected.GameId ??= other.GameId;
            if(!WikiContentQuality.TemplateRecord(selected))
            {
                bool newerWiki=selected.MetadataVersion>=12&&selected.Revision>other.Revision&&selected.WikiUrl.StartsWith("https://eqlwiki.com/",StringComparison.OrdinalIgnoreCase)&&other.WikiUrl.StartsWith("https://eqlwiki.com/",StringComparison.OrdinalIgnoreCase);
                if(!newerWiki)foreach(var stat in other.Stats) selected.Stats.TryAdd(stat.Key,stat.Value);
                selected.Effects = selected.Effects.Concat(other.Effects).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (!WikiContentQuality.TemplateRecord(selected)&&selected.Display.Source.Length == 0 && other.Display.Source.Length > 0) selected.Display = EquipmentPositions.Copy(other.Display);
            if (selected.Display.WeaponShape == WeaponShape.Unknown && other.Display.WeaponShape != WeaponShape.Unknown && other.Display.WeaponShapeEvidence.Length > 0)
            { selected.Display.WeaponShape = other.Display.WeaponShape; selected.Display.WeaponShapeEvidence = other.Display.WeaponShapeEvidence; }
            selected.Aliases = selected.Aliases.Concat(other.Aliases).Distinct().ToList();
            selected.RequiresSourceIdentity |= other.RequiresSourceIdentity;
            selected.RequiresExplicitIdentity |= other.RequiresExplicitIdentity;
            if (other.RequiresSourceIdentity) selected.GameId = other.GameId;
            if (selected.IntroducedExpansion == "Unknown" && other.IntroducedExpansion != "Unknown")
            { selected.IntroducedExpansion = other.IntroducedExpansion; selected.ExpansionEvidence = other.ExpansionEvidence; }
            var sources = old.Sources.Concat(item.Sources).ToList();
            bool curated = sources.Any(s => s.EvidenceId.StartsWith("classic-", StringComparison.Ordinal));
            selected.Sources = sources.Where(s => !WikiContentQuality.Placeholder(s.Mob)&&!WikiContentQuality.Placeholder(s.Zone)).Where(s => !curated || s.EvidenceId.StartsWith("classic-", StringComparison.Ordinal) || s.EvidenceId.StartsWith("acquisition-", StringComparison.Ordinal) || s.Availability == "Observed drop")
                .Where(s=>selected.AvailabilityEvidence!=WikiContentQuality.QuestRewardConflict||s.Method!="Drop"||s.Availability.Equals("Observed drop",StringComparison.OrdinalIgnoreCase))
                .DistinctBy(s => (s.Zone, s.Mob, s.Method, s.EvidenceId, s.Availability, s.Notes)).ToList();
            ItemExpansionEvidence.Apply(selected);
            if (selected.CraftRecipes.Count == 0 && other.CraftRecipes.Count > 0) selected.CraftRecipes = other.CraftRecipes;
            known[item.Key] = selected;
        }
        catalog.Items = known.Values.ToList();
        catalog.InvalidateIndexes();
    }
}
