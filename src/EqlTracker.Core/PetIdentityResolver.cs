namespace EqlTracker.Core;

public static class PetIdentityResolver
{
    public static PetIdentity? At(CharacterProfile profile, DateTime at, string actor = "") => profile.PetIdentities.Where(p => p.From <= at && (!p.Until.HasValue || at < p.Until) && (actor.Length == 0 || p.Name.Equals(actor, StringComparison.OrdinalIgnoreCase))).MaxBy(p => p.From);
    public static string CurrentFamily(CharacterProfile profile)
    {
        if (profile.PetElementOverride.Length > 0) return profile.PetElementOverride;
        // An unresolved newer owned pet must block an older known family's icon.
        var current = profile.PetIdentities.Where(p => !p.Until.HasValue).MaxBy(p => p.From);
        var identity = current ?? profile.PetIdentities.MaxBy(p => p.From);
        return identity?.Element is { Length: > 0 } family ? family : "Unknown";
    }
    public static string DisplayFamily(CharacterProfile profile, IEnumerable<Encounter> fights, bool historical)
    {
        if (!historical) return CurrentFamily(profile);
        var families = fights.Select(f => At(profile, f.Start, f.PetName)?.Element is { Length: > 0 } known ? known : f.PetType).Distinct().ToArray();
        return families.Length == 1 && families[0].Length > 0 ? families[0] : "Unknown";
    }
    public static string SessionEvidence(CharacterProfile profile, IEnumerable<Encounter> fights) => fights.Select(f => At(profile, f.Start, f.PetName)?.Element is { Length: > 0 } known ? known : f.PetType).Distinct().Count() > 1 ? "Mixed-family session; icon shows current or most recent confirmed owned pet" : "Current or most recent confirmed owned pet";
}
