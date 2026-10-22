namespace EqlTracker.Core;

public static class PetLayoutDefaults
{
    public const int Version = 1;
    public static bool Apply(CharacterProfile profile)
    {
        const string adjustment = "pet-compact-columns";
        if (profile.LayoutAdjustments.GetValueOrDefault(adjustment) >= Version) return false;
        foreach (string key in profile.ColumnWidths.Keys.Where(k => k.StartsWith("tracker.pet.candidates:", StringComparison.Ordinal) || k.StartsWith("tracker.pet.inventory:", StringComparison.Ordinal)).ToArray()) profile.ColumnWidths.Remove(key);
        profile.LayoutAdjustments[adjustment] = Version;
        return true;
    }
}
