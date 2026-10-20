using System.Text.Json;
using System.Text.Json.Nodes;
using EqlTracker.Core;

internal static class ItemDisplayRefresh
{
    internal static async Task Run(string root, bool refresh = true)
    {
        var paths = new[] { "data/equipment.json", "data/catalog.json" }.Select(p => Path.Combine(root, p)).ToArray();
        var docs = paths.Select(p => JsonNode.Parse(File.ReadAllText(p))!).ToArray();
        var entries = docs.SelectMany(d => d["items"]!.AsArray()).Where(n => n != null).Cast<JsonNode>().ToList();
        var requested = entries.Select(n => JsonSerializer.Deserialize<ItemDefinition>(n.ToJsonString(), Json.Options)!).Where(i => PetItemRules.Type(i) == "Unknown" || i.MetadataVersion < PetItemRules.MetadataVersion && i.Slots.Any(s => EquipmentPositions.Normalize(s) is "Primary" or "Secondary" or "Range" or "Ammo")).DistinctBy(i => i.Key).ToList();
        using var wiki = new WikiClient(); int done = 0, sourced = 0;
        foreach (var batch in (refresh ? requested : []).Chunk(20))
        {
            var parsed = await wiki.FetchItemsAsync(batch.Select(i => i.Name), CancellationToken.None);
            foreach (var item in parsed)
            foreach (var node in entries.Where(n => n["key"]?.GetValue<string>() == item.Key || n["name"]?.GetValue<string>() == item.Name))
            {
                node["display"] = JsonSerializer.SerializeToNode(item.Display, Json.Options);
                node["metadataVersion"] = PetItemRules.MetadataVersion; sourced++;
            }
            for (int i = 0; i < paths.Length; i++) File.WriteAllText(paths[i], docs[i].ToJsonString(new() { WriteIndented = true }));
            done += batch.Length; Console.WriteLine($"Display evidence {done}/{requested.Count}; {sourced} records enriched");
        }
        var types = entries.Select(n => JsonSerializer.Deserialize<ItemDefinition>(n.ToJsonString(), Json.Options)!).GroupBy(PetItemRules.Type).ToDictionary(g => g.Key, g => g.Count());
        Json.Write(Path.Combine(root, "data/item-type-audit.json"), new { ParserVersion = PetItemRules.MetadataVersion, Requested = refresh ? requested.Count : 0, EnrichedRecords = sourced, SourcedDisplayRecords = entries.Count(n => JsonSerializer.Deserialize<ItemDefinition>(n.ToJsonString(), Json.Options)!.Display.Source.Length > 0), Types = types, RemainingUnknown = entries.Select(n => JsonSerializer.Deserialize<ItemDefinition>(n.ToJsonString(), Json.Options)!).Where(i => PetItemRules.Type(i) == "Unknown").Select(i => new { i.Key, i.Name, i.WikiUrl, i.MetadataComplete }).ToList(), Scope = "Display metadata only; existing stats, IDs, aliases, restrictions and expansion evidence preserved. Ordinary background sync continues remaining legacy refreshes. Unresolved pages remain Unknown rather than being classified from names." });
    }
}
