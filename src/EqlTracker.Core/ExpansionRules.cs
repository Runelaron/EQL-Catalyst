namespace EqlTracker.Core;

public static class ExpansionRules
{
    public static readonly string[] Names = ["Classic", "Kunark", "Velious"];
    public static string Current(CharacterProfile profile) => Names.Contains(profile.CurrentExpansion) ? profile.CurrentExpansion : Names.Contains(profile.Era) ? profile.Era : "Classic";
    public static bool Available(string source, string current) => source == "Classic" || Array.IndexOf(Names, source) >= 0 && Array.IndexOf(Names, source) <= Array.IndexOf(Names, current);
    public static bool Planned(QuestDefinition quest, CharacterProfile profile) => quest.Expansion.Length == 0 || Available(quest.Expansion, profile.PlanThrough);
    public static string ShortZone(string zone, double width)
    {
        if (width >= 310) return zone;
        string shortName = zone.Replace("East Freeport", "E. Freeport").Replace("West Freeport", "W. Freeport").Replace("North Freeport", "N. Freeport").Replace("Temple of Cazic-Thule", "Cazic-Thule").Replace("Western Plains of Karana", "W. Karana").Replace("Eastern Plains of Karana", "E. Karana").Replace("Northern Plains of Karana", "N. Karana").Replace("Southern Plains of Karana", "S. Karana");
        if (width < 180) shortName = shortName.Replace("E. Freeport", "EFP").Replace("W. Freeport", "WFP").Replace("N. Freeport", "NFP").Replace("Cazic-Thule", "CT").Replace("Permafrost", "Perma").Replace("The Plane of Fear", "Fear").Replace("Plane of Fear", "Fear").Replace("The Plane of Hate", "Hate").Replace("Plane of Hate", "Hate").Replace("The Plane of Sky", "Sky").Replace("Plane of Sky", "Sky");
        return shortName;
    }
}
