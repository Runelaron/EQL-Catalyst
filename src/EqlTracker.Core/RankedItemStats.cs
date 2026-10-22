namespace EqlTracker.Core;

public enum ItemStatEvidenceKind { Catalog, Estimated, ClientObserved }
public sealed record RankedStatObservation(string ItemKey, int Rank, double TierProgress, string Conditions,
    IReadOnlyDictionary<string,double> BaseChecks, IReadOnlyDictionary<string,double> Stats,
    double Weight, double Accuracy, double DamageBonus, string Source);
public sealed record RankedStatProjection(Dictionary<string,double> Stats, ItemStatEvidenceKind Kind,
    IReadOnlySet<string> UncertainStats, double? Weight, bool WeightIsBase, double? Accuracy,
    double? ObservedDamageBonus, string Evidence, IReadOnlyDictionary<string,ItemStatEvidenceKind> StatEvidence)
{
    public bool WeaponUncertain => UncertainStats.Contains("Damage") || UncertainStats.Contains("Ratio");
}

/// <summary>Tier-start projections. Observations never alter catalog bases or imply ownership/XP.</summary>
public static class RankedItemStats
{
    public const string Version = "EQL-2026-10-21/ranked-stats-v1";
    public const string UpgradeSource = "https://eqlwiki.com/index.php/Item_Upgrade_System";
    public const string ConflictSource = "https://eqltools.com/learn/upgrades";
    // Transcribed item facts only. Original screenshots and character information stay local.
    public static IReadOnlyList<RankedStatObservation> Observations { get; } = [
        new("revultant whip",4,0,"Tier 4, 0/16 XP; client item inspection, 2026-10-21",
            new Dictionary<string,double>{{"Damage",5},{"Delay",14},{"DEX",5},{"AGI",4},{"ResistDisease",5},{"ResistPoison",5}},
            new Dictionary<string,double>{{"Damage",7},{"Delay",14},{"DEX",9},{"AGI",8},{"ResistDisease",9},{"ResistPoison",9},{"ResistVoid",4}},
            .7,10,6,"User-provided client inspection, 2026-10-21"),
        new("evensong",5,0,"Tier 5, 0/32 XP; client item inspection, 2026-10-21",
            new Dictionary<string,double>{{"Damage",11},{"Delay",25}},
            new Dictionary<string,double>{{"Damage",16},{"Delay",25}},
            1.7,12.5,12,"User-provided client inspection, 2026-10-21")
    ];
    public static RankedStatProjection Resolve(ItemDefinition item, int rank, double tierProgress = 0)
    {
        var stats = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string,ItemStatEvidenceKind>(StringComparer.OrdinalIgnoreCase);
        var uncertain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool weapon = item.Stats.ContainsKey("Damage") || ItemEquipmentIcons.IsWeapon(item);
        foreach (var (stat,value) in item.Stats)
        {
            if (stat == "Ratio") continue; // A ratio requires both documented inputs.
            double next = value;
            if (rank > 0 && stat is not ("Delay" or "Haste"))
            {
                if (stat == "Damage") { next = Math.Floor(value * (1 + rank * .05)); uncertain.Add(stat); }
                else if (value > 0) next = Math.Max(Math.Floor(value * (1 + rank * .1)), value + rank);
                else if (value < 0) uncertain.Add(stat); // Negative-stat scaling is not established.
            }
            if (rank > 0 && stat == "Haste") uncertain.Add(stat);
            stats[stat] = next;
            sources[stat] = rank == 0 || stat == "Delay" ? ItemStatEvidenceKind.Catalog : ItemStatEvidenceKind.Estimated;
        }
        string key = item.Key.Length > 0 ? item.Key : Identity.Key(item.Name);
        var observation = Observations.FirstOrDefault(o => o.ItemKey == key && o.Rank == rank && o.TierProgress == tierProgress
            && o.BaseChecks.All(s => item.Stats.TryGetValue(s.Key,out double value) && value == s.Value));
        if (observation != null) foreach (var (stat,value) in observation.Stats) { stats[stat] = value; uncertain.Remove(stat);sources[stat]=ItemStatEvidenceKind.ClientObserved; }
        if (stats.TryGetValue("Damage",out double damage) && stats.TryGetValue("Delay",out double delay) && delay > 0)
        { stats["Ratio"] = damage / delay;sources["Ratio"]=sources["Damage"]; if (uncertain.Contains("Damage")) uncertain.Add("Ratio"); }
        else if (weapon) { uncertain.Add("Damage"); uncertain.Add("Ratio"); }
        string evidence = observation != null ? "Client-observed tier start · " + observation.Conditions + ". Current fractional XP is not recorded."
            : rank == 0 ? "Catalog base values · " + item.WikiUrl
            : "Tier-start estimate; fractional XP unrecorded. Weapon damage scaling and ranked haste remain unverified. " + UpgradeSource + " · " + ConflictSource;
        return new(stats,observation != null ? ItemStatEvidenceKind.ClientObserved : rank > 0 ? ItemStatEvidenceKind.Estimated : ItemStatEvidenceKind.Catalog,
            uncertain,observation?.Weight ?? item.Display.Weight,observation == null && rank > 0,
            observation?.Accuracy,observation?.DamageBonus,evidence,sources);
    }
}
