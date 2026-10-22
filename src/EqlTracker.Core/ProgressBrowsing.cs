namespace EqlTracker.Core;

internal enum ProgressBrowseMode { Quest, Item }

internal sealed record ProgressBrowseChoice(string Key, string Label, string ItemKey, IReadOnlyList<QuestDefinition> Quests, string Evidence)
{
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
