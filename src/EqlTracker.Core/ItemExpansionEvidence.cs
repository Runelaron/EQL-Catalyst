using System.Text.RegularExpressions;
namespace EqlTracker.Core;

public sealed class ZoneExpansionEvidence
{
    public string Zone { get; set; } = "";
    public string Expansion { get; set; } = "Unknown";
    public string Source { get; set; } = "";
}
public static class ItemExpansionEvidence
{
    private static readonly Lazy<List<ZoneExpansionEvidence>> bundled = new(() =>
    {
        string file = Path.Combine(AppContext.BaseDirectory, "data", "zone-expansion-evidence.json");
        return File.Exists(file) ? Json.Read<List<ZoneExpansionEvidence>>(file) : [];
    });
    public static void Apply(ItemDefinition item, IEnumerable<ZoneExpansionEvidence>? zones = null)
    {
        if (item.IntroducedExpansion != "Unknown") return;
        var index = (zones ?? bundled.Value).GroupBy(z => Identity.ZoneKey(z.Zone)).ToDictionary(g => g.Key, g => g.ToList());
        var matches = new List<ZoneExpansionEvidence>();
        foreach (var source in item.Sources.Where(s => s.Zone.Length > 0))
        {
            string key = Identity.ZoneKey(Regex.Replace(source.Zone, @"\s*-\s*(?:Solo|Group|Raid)\b.*$", "", RegexOptions.IgnoreCase));
            if (index.TryGetValue(key, out var values) && values.Select(v => v.Expansion).Distinct().Count() == 1) matches.AddRange(values);
        }
        var first = matches.Where(m => ExpansionRules.Names.Contains(m.Expansion)).OrderBy(m => Array.IndexOf(ExpansionRules.Names, m.Expansion)).FirstOrDefault();
        if (first == null) return;
        item.ExpansionProvenance = new() { Inferred = true, Expansion = first.Expansion, Zones = matches.Where(m => m.Expansion == first.Expansion).Select(m => m.Zone).Distinct().ToList(), Source = first.Source };
    }
}
