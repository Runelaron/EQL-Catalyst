namespace EqlTracker.Core;
internal enum ProgressBrowseMode { Quest, Item, Zone }
internal sealed record ProgressBrowseChoice(string Key, string Label, string ItemKey, IReadOnlyList<QuestDefinition> Quests, string Evidence)
{
    internal string ZoneKey { get; init; } = "";
    public override string ToString() => Label;
}
/// <summary>Documented associations only. Browsing never creates ownership or quest state.</summary>
internal sealed class ProgressBrowseIndex
{
    private readonly Catalog catalog;
    private readonly Dictionary<string, List<QuestDefinition>> byItem = new(StringComparer.OrdinalIgnoreCase);
    internal ProgressBrowseIndex(Catalog catalog)
    {
        this.catalog = catalog;
        foreach (var quest in catalog.Quests)
            foreach (string key in Keys(quest).Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byItem.TryGetValue(key, out var quests)) byItem[key] = quests = [];
                quests.Add(quest);
            }
    }
    private readonly Dictionary<string,List<QuestDefinition>> byZone=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> zoneLabels=new(StringComparer.OrdinalIgnoreCase);
    private IEnumerable<string> ItemZones(string key)=>(catalog.FindKey(Canonical(key))?.Sources??[]).Select(s=>s.Zone);
    private static bool ValidZone(string zone)=>!string.IsNullOrWhiteSpace(zone)&&zone is not ("Unknown" or "Unverified" or "Various" or "N/A");
    private System.Text.RegularExpressions.Regex? zonePattern;
    private readonly Dictionary<string,string> textZones=new(StringComparer.OrdinalIgnoreCase);
    private bool zonesBuilt;
    private void BuildZones()
    {
        foreach(string zone in catalog.Items.SelectMany(i=>i.Sources).Select(s=>s.Zone).Concat(catalog.Quests.Select(q=>q.StartZone)).Where(ValidZone))zoneLabels.TryAdd(Identity.ZoneKey(zone),zone.Trim());
        foreach(var pair in zoneLabels)
            foreach(string alias in ZoneAliases(pair.Key).Append(pair.Value).Append(pair.Key).Where(a=>a.Length>=4))textZones.TryAdd(alias,pair.Key);
        foreach(string alias in catalog.Items.SelectMany(i=>i.Sources).Select(s=>s.Zone).Concat(catalog.Quests.Select(q=>q.StartZone)).Where(ValidZone))textZones.TryAdd(alias,Identity.ZoneKey(alias));
        zonePattern=textZones.Count==0?null:new(@"(?<![\p{L}])(?:"+string.Join('|',textZones.Keys.OrderByDescending(a=>a.Length).Select(System.Text.RegularExpressions.Regex.Escape))+@")(?![\p{L}])",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant|System.Text.RegularExpressions.RegexOptions.Compiled);
        zonesBuilt=true;
        foreach(var q in catalog.Quests)
        {
            var zones=Keys(q).SelectMany(ItemZones).Concat(new[]{q.StartZone})
                .Concat(q.Steps.SelectMany(s=>TextZones(s.Instructions+" "+s.Name+" "+string.Join(' ',s.Conditions.Select(c=>c.ToString()))))).Where(ValidZone).Select(Identity.ZoneKey).Distinct();
            foreach(string zone in zones){if(!byZone.TryGetValue(zone,out var quests))byZone[zone]=quests=[];quests.Add(q);}
        }
    }
    private IEnumerable<string> TextZones(string text)=>zonePattern==null?[]:zonePattern.Matches(text).Select(m=>zoneLabels[textZones[m.Value]]).Distinct(StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<string> ZoneAliases(string key)=>ZoneNames.BrowsingAliases(key);
    internal bool IsZone(QuestNode node,string zone)
    {
        if(node.ItemKeys.SelectMany(ItemZones).Any(z=>Identity.ZoneKey(z)==zone))return true;
        var q=catalog.Quests.FirstOrDefault(q=>q.Id==node.QuestId);
        if(node.Kind==QuestNodeKind.Quest)return q!=null&&Identity.ZoneKey(q.StartZone)==zone;
        var step=q?.Steps.FirstOrDefault(s=>s.Id==node.StepId);
        return step!=null&&TextZones(step.Instructions+" "+step.Name+" "+string.Join(' ',step.Conditions.Select(c=>c.ToString()))).Any(z=>Identity.ZoneKey(z)==zone);
    }
    private string Canonical(string key) => (catalog.FindKey(key) ?? catalog.Find(key))?.Key ?? key;
    internal static IEnumerable<string> Keys(QuestDefinition quest) => QuestPresentation.RewardKeys(quest)
        .Concat(quest.Requirements.Select(i => i.ItemKey))
        .Concat(quest.Steps.SelectMany(s => s.Inputs.Concat(s.Outputs)).Select(i => i.ItemKey))
        .Where(k => k.Length > 0);
    internal IReadOnlyList<ProgressBrowseChoice> Choices(ProgressBrowseMode mode, CharacterProfile profile, InventorySnapshot inventory, bool tracked, string query)
    {
        bool Included(QuestDefinition q) => !tracked || QuestTracking.Selected(q, profile, inventory, catalog);
        if (mode == ProgressBrowseMode.Quest)
            return catalog.Quests.Where(q => Included(q) && QuestPresentation.Matches(q, catalog, query))
                .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).Select(q => new ProgressBrowseChoice(q.Id, q.Name, "", new[] { q }, q.Coverage)).ToList();
        if(mode==ProgressBrowseMode.Zone)
        {
            if(!zonesBuilt)BuildZones();string search=query.Trim();
            return byZone.Select(pair=>new{Key=pair.Key,Quests=pair.Value.Where(Included).OrderBy(q=>q.Name,StringComparer.OrdinalIgnoreCase).ToArray()})
                .Where(p=>p.Quests.Length>0&&(zoneLabels[p.Key].Contains(search,StringComparison.OrdinalIgnoreCase)||ZoneAliases(p.Key).Any(a=>a.Contains(search,StringComparison.OrdinalIgnoreCase))||p.Quests.Any(q=>QuestPresentation.Matches(q,catalog,search))))
                .Select(p=>new ProgressBrowseChoice(p.Key,zoneLabels[p.Key],"",p.Quests,"Documented quest locations · "+p.Quests.Length+" related quests"){ZoneKey=p.Key}).OrderBy(c=>c.Label,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        var choices = new List<ProgressBrowseChoice>();
        foreach (var pair in byItem)
        {
            var item = catalog.FindKey(pair.Key);
            if (item == null || !inventory.Evidence(item).Any(e => !e.IsAugment && !e.IsCollection && e.Count > 0)) continue;
            var quests = pair.Value.Where(Included).OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (quests.Length == 0 || !item.Aliases.Prepend(item.Name).Any(n => n.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)) && !quests.Any(q => QuestPresentation.Matches(q, catalog, query))) continue;
            choices.Add(new(item.Key, item.Name, item.Key, quests, inventory.Ownership(item)));
        }
        return choices.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
    }
    internal bool IsItem(QuestNode node, string key) => node.ItemKeys.Any(k => Canonical(k).Equals(key, StringComparison.OrdinalIgnoreCase));
    internal string AlternateRoutes(QuestDefinition quest, CharacterProfile profile, string key)
    {
        var active = QuestRecipes.Steps(quest, profile);
        if (QuestPresentation.RewardKeys(quest).Any(k => Canonical(k) == key) || active.Any(s => s.Inputs.Concat(s.Outputs).Any(i => Canonical(i.ItemKey) == key))) return "";
        return string.Join(", ", quest.Steps.Where(s => s.Inputs.Concat(s.Outputs).Any(i => Canonical(i.ItemKey) == key)).Select(s => s.Route).Distinct());
    }
}
