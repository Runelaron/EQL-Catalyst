namespace EqlTracker.Core;

public static class CatalogMigration
{
    public static Catalog Merge(Catalog saved, Catalog bundled)
    {
        if (saved.SchemaVersion >= bundled.SchemaVersion) return saved;
        foreach (var item in bundled.Items)
        {
            var old = saved.Items.FirstOrDefault(x => x.Key == item.Key);
            if (old == null) { saved.Items.Add(item); continue; }
            old.GameId ??= item.GameId;
            old.Aliases = old.Aliases.Concat(item.Aliases).Distinct().ToList();
            if (item.Sources.Any(s => s.EvidenceId.StartsWith("classic-", StringComparison.Ordinal)))
            {
                old.Sources = item.Sources.Concat(old.Sources.Where(s => s.Availability == "Observed drop")).DistinctBy(s => (s.Zone, s.Mob, s.Method, s.EvidenceId)).ToList();
                old.RequiresSourceIdentity = item.RequiresSourceIdentity;
                old.RequiresExplicitIdentity = item.RequiresExplicitIdentity;
                if (item.RequiresSourceIdentity) old.GameId = item.GameId;
                old.Name = item.Name;
            }
            if (item.IntroducedExpansion != "Unknown") { old.IntroducedExpansion = item.IntroducedExpansion; old.ExpansionEvidence = item.ExpansionEvidence; }
            foreach (var source in item.Sources.Where(s => !old.Sources.Any(o => o.Zone == s.Zone && o.Mob == s.Mob))) old.Sources.Add(source);
        }
        saved.Quests = bundled.Quests; saved.Zones = bundled.Zones;
        saved.Documents = saved.Documents.Concat(bundled.Documents).DistinctBy(x => (x.Title, x.Revision)).ToList();
        saved.SchemaVersion = bundled.SchemaVersion;
        saved.InvalidateIndexes();
        return saved;
    }
}
