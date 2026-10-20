namespace EqlTracker.Core;

public sealed class QuestCondition
{
    public string Kind { get; set; } = "Instruction";
    public string Name { get; set; } = "";
    public decimal? Amount { get; set; }
    public string Unit { get; set; } = "";
    public string Note { get; set; } = "";
    public override string ToString() => Kind + ": " + Name + (Amount.HasValue ? $" · {Amount} {Unit}" : "") + (Note.Length > 0 ? " · " + Note : "");
}

/// <summary>Expands curated reward demand; ownership and projected craft surplus are separate.</summary>
public static class QuestRecipes
{
    public static string InventoryIdentity(InventoryEntry entry) => entry.Location + "|" + entry.GameId + "|" + Identity.Key(entry.Name);
    public static void ApplyIdentities(CharacterProfile profile, InventorySnapshot inventory)
    {
        foreach (var entry in inventory.Entries) entry.ConfirmedItemKey = entry.Count == 1 ? profile.ItemIdentityConfirmations.GetValueOrDefault(InventoryIdentity(entry), "") : "";
        inventory.Invalidate();
    }
    public static List<QuestStep> Steps(QuestDefinition quest, CharacterProfile profile) => quest.Steps.Where(s => !s.Optional || s.Route == profile.Routes.GetValueOrDefault(quest.Id + (s.RouteGroup.Length > 0 ? "|" + s.RouteGroup : ""), "Default")).ToList();
    public sealed record Demand(string Key, int Needed, int Owned, QuestStep? Parent);
    public static List<Demand> Expand(QuestDefinition quest, CharacterProfile profile, Catalog catalog, Dictionary<string, int> stock, InventorySnapshot? inventory = null)
    {
        var steps = Steps(quest, profile);
        var producers = steps.SelectMany(s => s.Outputs.Select(o => (o.ItemKey, Step: s))).GroupBy(x => x.ItemKey).ToDictionary(g => g.Key, g => g.First().Step);
        var rows = new Dictionary<string, Demand>(); var surplus = new Dictionary<string, int>(); var tools = new HashSet<string>();
        void Add(string key, int needed, int owned, QuestStep? parent)
        { if (rows.TryGetValue(key, out var old)) rows[key] = old with { Needed = old.Needed + needed, Owned = old.Owned + owned }; else rows[key] = new(key, needed, owned, parent); }
        void Need(string key, int quantity, QuestStep? parent, HashSet<string> path, bool consumed = true)
        {
            if (quantity <= 0 || !consumed && !tools.Add(key)) return;
            if (!path.Add(key)) throw new InvalidDataException("Recipe cycle: " + quest.Id + "/" + key);
            int owned = Math.Min(quantity, stock.GetValueOrDefault(key));
            if (consumed) stock[key] = stock.GetValueOrDefault(key) - owned;
            int pending = quantity - owned, planned = Math.Min(pending, surplus.GetValueOrDefault(key)); surplus[key] = surplus.GetValueOrDefault(key) - planned; pending -= planned;
            producers.TryGetValue(key, out var producer);
            if (producer != null && profile.CompletedSteps.Contains(producer.Id)) return;
            if (producer == null || producer.Inputs.Count == 0)
            { Add(key, quantity - planned, owned, parent ?? producer); return; }
            // Retain owned intermediates as satisfied rows, but never count projected products as stock.
            if (owned > 0) Add(key, owned, owned, parent);
            if (pending == 0) return;
            int yield = Math.Max(1, producer.Outputs.First(o => o.ItemKey == key).Count), attempts = (pending + yield - 1) / yield;
            // A random reward is one attempt's shopping list, not a promise of one reward per attempt.
            if (producer.RandomReward) attempts = 1;
            else
            {
                foreach (var output in producer.Outputs) surplus[output.ItemKey] = surplus.GetValueOrDefault(output.ItemKey) + attempts * output.Count;
                surplus[key] -= pending;
                if (!consumed) surplus[key] += pending;
            }
            foreach (var input in producer.Inputs) Need(input.ItemKey, input.Count * (input.Consumed ? attempts : 1), producer, new(path), input.Consumed);
            foreach (var id in producer.Prerequisites)
                if (steps.FirstOrDefault(s => s.Id == id) is { } prerequisite && !prerequisite.Outputs.Any(o => producer.Inputs.Any(i => i.ItemKey == o.ItemKey)))
                    foreach (var output in prerequisite.Outputs) Need(output.ItemKey, output.Count, prerequisite, new(path), false);
        }
        int copies = Math.Max(0, Math.Max(1, profile.QuestQuantities.GetValueOrDefault(quest.Id, 1)) - (inventory == null ? 0 : QuestTracking.CoveredCount(quest, profile, inventory, catalog)));
        Need(quest.RewardKey, copies, null, []);
        return rows.Values.Where(x => x.Key != quest.RewardKey).ToList();
    }
}
