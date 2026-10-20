namespace EqlTracker.Core;

public enum QuestNodeKind { Ingredient, Step, Quest }

public sealed class QuestNode
{
    public QuestNodeKind Kind { get; init; }
    public string Id { get; init; } = "";
    public string QuestId { get; init; } = "";
    public List<string> ItemKeys { get; init; } = [];
    public string StepId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; set; } = "Incomplete";
    public string Detail { get; init; } = "";
    public List<QuestNode> Children { get; init; } = [];
    public bool Fulfilled => Status is "Owned" or "Covered by reward" or "Completed";
}

public static class QuestHierarchy
{
    public static List<QuestNode> Build(QuestDefinition quest, Catalog catalog, CharacterProfile profile, InventorySnapshot inventory)
    {
        var steps = QuestRecipes.Steps(quest, profile);
        var consumed = steps.SelectMany(s => s.Inputs).Select(x => x.ItemKey).ToHashSet();
        var roots = steps.Where(s => s.Outputs.Any(o => !consumed.Contains(o.ItemKey))).ToList();
        if (roots.Count == 0) roots = steps.Where(s => !steps.Any(t => t.Prerequisites.Contains(s.Id))).ToList();
        var visited = new HashSet<string>();
        QuestNode Node(QuestStep step, string path, string covered, HashSet<string> ancestry, int required = 1)
        {
            visited.Add(step.Id);
            bool manual = profile.CompletedQuests.Contains(quest.Id) || profile.CompletedSteps.Contains(step.Id);
            int yield = Math.Max(1, step.Outputs.FirstOrDefault()?.Count ?? 1);
            int have = step.Outputs.Count > 0 ? step.Outputs.Min(o => catalog.FindKey(o.ItemKey) is { } item ? inventory.Count(item) : 0) : 0;
            bool owned = step.Outputs.Count > 0 && have >= required;
            int runs = (Math.Max(1, required - have) + yield - 1) / yield;
            string status = manual ? "Completed" : owned ? "Owned" : covered.Length > 0 ? covered : "Incomplete";
            var node = new QuestNode { Kind = QuestNodeKind.Step, Id = path + "/" + step.Id, QuestId = quest.Id, ItemKeys = (step.Outputs.Count > 0 ? step.Outputs : step.Inputs).Select(o => o.ItemKey).ToList(), StepId = step.Id, Name = (step.Outputs.Count > 0 ? string.Join(" + ", step.Outputs.Select(o => catalog.FindKey(o.ItemKey)?.Name ?? o.ItemKey)) : step.Name) + (required > 1 ? $" ×{required}" : ""), Status = status, Detail = $"Known ownership: {have} / {required}\n" + step.Instructions + (step.RandomReward ? "\nRandom reward: ingredients are per attempt." : "") + (step.Conditions.Count > 0 ? "\n" + string.Join("\n", step.Conditions) : "") };
            if (!ancestry.Add(step.Id)) return node;
            var shownProducers = new HashSet<string>();
            foreach (var input in step.Inputs)
            {
                var item = catalog.FindKey(input.ItemKey);
                var producer = steps.FirstOrDefault(s => s.Id != step.Id && !ancestry.Contains(s.Id) && s.Outputs.Any(o => o.ItemKey == input.ItemKey));
                int needed = input.Count * (input.Consumed && !step.RandomReward ? runs : 1);
                if (producer != null) { if (shownProducers.Add(producer.Id)) node.Children.Add(Node(producer, node.Id, manual ? "Completed" : owned ? "Covered by reward" : covered, new(ancestry), needed)); }
                else
                {
                    var acquisition = steps.FirstOrDefault(s => s.Outputs.Count == 0 && s.Inputs.Any(i => i.ItemKey == input.ItemKey));
                    node.Children.Add(new() { Id = node.Id + "/" + input.ItemKey, QuestId = quest.Id, ItemKeys = [input.ItemKey], StepId = acquisition?.Id ?? "", Name = (item?.Name ?? input.ItemKey) + $" ×{needed}" + (!input.Consumed ? " · retained tool" : ""), Status = manual ? "Completed" : owned ? "Covered by reward" : covered.Length > 0 ? covered : acquisition != null && profile.CompletedSteps.Contains(acquisition.Id) ? "Completed" : item != null && inventory.Count(item) >= needed ? "Owned" : "Incomplete", Detail = (acquisition?.Instructions ?? "Collect this ingredient") + "\n" + (item == null ? "Metadata pending" : inventory.Ownership(item)) });
                }
            }
            foreach (var prerequisite in step.Prerequisites.Where(id => !ancestry.Contains(id)))
                if (steps.FirstOrDefault(s => s.Id == prerequisite) is { } producer && !step.Inputs.Any(i => producer.Outputs.Any(o => o.ItemKey == i.ItemKey))) node.Children.Add(Node(producer, node.Id, manual ? "Completed" : owned ? "Covered by reward" : covered, new(ancestry)));
            if (quest.RecipeVersion == 0 && node.Children.Count == 0 && step.Outputs.Count > 0 && !owned && !manual && covered.Length == 0 && step.Instructions.Contains("these", StringComparison.OrdinalIgnoreCase)) node.Status = "Relationship incomplete";
            return node;
        }
        string rootCovered = quest.RecipeVersion > 0 && QuestTracking.CoveredCount(quest, profile, inventory, catalog) >= Math.Max(1, profile.QuestQuantities.GetValueOrDefault(quest.Id, 1)) ? "Covered by reward" : "";
        var result = roots.Select(s => Node(s, quest.Id, rootCovered, [], quest.RecipeVersion > 0 ? Math.Max(1, profile.QuestQuantities.GetValueOrDefault(quest.Id, 1)) : 1)).ToList();
        var represented = steps.Where(s => visited.Contains(s.Id)).SelectMany(s => s.Inputs).Select(i => i.ItemKey).ToHashSet();
        foreach (var step in steps.Where(s => !visited.Contains(s.Id)).ToList())
            if (!visited.Contains(step.Id) && !(step.Outputs.Count == 0 && step.Inputs.Count > 0 && step.Inputs.All(i => represented.Contains(i.ItemKey)))) result.Add(Node(step, quest.Id, "", []));
        return result;
    }
}
