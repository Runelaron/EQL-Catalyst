namespace EqlTracker.Core;

public static class QuestPresentation
{
    public static string Normalize(string value) => string.Join(" ",System.Text.RegularExpressions.Regex.Matches(value,@"[\p{L}\p{N}]+").Select(m=>m.Value)).ToUpperInvariant();
    public static (string Heading,string Instruction) Instruction(QuestDefinition? quest,QuestStep step)
    {
        string heading=StepHeading(quest,step), body=step.Instructions.Trim();
        string normalizedHeading=Normalize(heading), normalizedBody=Normalize(body);
        // Some imported headings are truncated copies of the full instruction, even mid-word.
        bool repeatedPrefix = (normalizedHeading.Length >= 40 || normalizedHeading.Split(' ').Length >= 6 || heading.EndsWith("…") || heading.EndsWith("...")) && normalizedBody.StartsWith(normalizedHeading, StringComparison.Ordinal);
        if (heading=="Step instructions" || normalizedHeading==normalizedBody || repeatedPrefix) heading="";
        return (heading,body);
    }
    public static IEnumerable<string> RewardKeys(QuestDefinition quest) => quest.RewardAlternatives.SelectMany(g => g).Prepend(quest.RewardKey).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    public static bool Matches(QuestDefinition quest, Catalog catalog, string query)
    {
        query = query.Trim();
        return (quest.Name + " " + quest.SetName + " " + quest.Class).Contains(query, StringComparison.OrdinalIgnoreCase)
            || RewardKeys(quest).Any(k => catalog.FindKey(k) is { } item && item.Aliases.Prepend(item.Name).Any(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
    public static string StepHeading(QuestDefinition? quest, QuestStep step)
    {
        string name = step.Name.Trim();
        if (quest != null && name.StartsWith(quest.Name, StringComparison.OrdinalIgnoreCase)) name = name[quest.Name.Length..].TrimStart(' ', ':', '-', '·', '/');
        return name.Length == 0 || name.Equals("See walkthrough", StringComparison.OrdinalIgnoreCase) ? "Step instructions" : name;
    }
}
