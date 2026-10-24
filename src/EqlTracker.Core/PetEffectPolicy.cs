namespace EqlTracker.Core;

/// <summary>Catalog defaults are reference data; only selected/observed Pet effects are effective.</summary>
internal static class PetEffectPolicy
{
    internal const string Version = "pet-effects-v2-explicit";
    internal static bool Recognized(string kind) => kind is "Focus" or "Click" or "Worn" or "Proc" or "Ornamentation";
    internal static string Kind(ExaltationAssignment effect) => Recognized(effect.SocketType) ? effect.SocketType : ExaltationPlanner.EffectType(effect.Effect);
    internal static HashSet<string> Conflicts(IEnumerable<ExaltationAssignment> effects) => effects
        .Where(e => !string.IsNullOrWhiteSpace(e.Effect)).GroupBy(Kind)
        .Where(g => Recognized(g.Key) && g.Count() > 1).Select(g => g.Key).ToHashSet();
    internal static string Validate(IEnumerable<ExaltationAssignment> effects)
    {
        var conflicts = Conflicts(effects);
        return conflicts.Count == 0 ? "" : "Choose one " + string.Join(", ", conflicts.Order()) + " Exaltation per type in Assigned effects. Remove a duplicate or explicitly replace that type.";
    }
    internal static List<ExaltationAssignment> Observed(IEnumerable<InventoryEntry> augments) => augments
        .DistinctBy(a => (a.EffectIdentity, Identity.Key(a.Name), a.EffectText)).Select(a => new ExaltationAssignment
        { SocketType = a.Name.Contains("(Ornamentation)",StringComparison.OrdinalIgnoreCase) ? "Ornamentation" : ExaltationPlanner.EffectType(a.EffectText), SourceName = Identity.Canonical(a.Name), EffectId = a.EffectIdentity, Effect = a.EffectText.Length > 0 ? a.EffectText : a.Name, State = "Socketed" }).ToList();
    internal static IEnumerable<string> SelectedTexts(InventoryEntry entry, int? rank = null) => entry.Augments
        .Where(a => !string.IsNullOrWhiteSpace(a.EffectText)).GroupBy(a => ExaltationPlanner.EffectType(a.EffectText))
        .Where(g => Recognized(g.Key) && g.Count() == 1 && (rank ?? entry.Rank) >= ExaltationPlanner.Unlock(g.Key)).Select(g => g.Single().EffectText);
}
