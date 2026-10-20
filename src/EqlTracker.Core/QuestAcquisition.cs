namespace EqlTracker.Core;

/// <summary>Selection-local instructions, independent of outstanding demand or ownership.</summary>
public sealed record QuestAcquisitionDetail(QuestNodeKind Kind, IReadOnlyList<string> ItemKeys,
    IReadOnlyList<QuestStep> Steps, IReadOnlyList<QuestStep> Prerequisites);

public static class QuestAcquisition
{
    public static QuestAcquisitionDetail Describe(QuestNode node, QuestDefinition? quest, CharacterProfile profile)
    {
        var active = quest == null ? [] : QuestRecipes.Steps(quest, profile);
        var steps = node.Kind == QuestNodeKind.Quest ? active : active.Where(s => s.Id == node.StepId).ToList();
        var items = node.ItemKeys.Count > 0 ? node.ItemKeys : node.Kind == QuestNodeKind.Quest && quest?.RewardKey.Length > 0 ? [quest.RewardKey] : new List<string>();
        // Only the selected step's explicit prerequisites apply here. Its consuming parent
        // and unrelated quest sequencing must never become ingredient collection gates.
        var prerequisites = node.Kind == QuestNodeKind.Quest ? [] : active.Where(s => steps.Any(step => step.Prerequisites.Contains(s.Id))
            && !steps.Any(step => step.Inputs.Any(i => s.Outputs.Any(o => o.ItemKey == i.ItemKey)))).ToList();
        return new(node.Kind, items, steps, prerequisites);
    }
}
