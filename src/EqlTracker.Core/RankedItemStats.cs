namespace EqlTracker.Core;

public enum ItemStatEvidenceKind { Catalog, Estimated, ClientObserved }
internal enum StatRuleConfidence { Catalog, DocumentedRule, Observed, Unresolved, AdoptedModel }
internal sealed record RankedStatBasis(int CatalogRank, int ScenarioRank, double TierProgress, string Version);
internal sealed record StatRuleEvidence(string Stat, StatRuleConfidence Confidence, string Reason, string Source);
public sealed record RankedStatObservation(string ItemKey, int Rank, double TierProgress, string Conditions,
    IReadOnlyDictionary<string,double> BaseChecks, IReadOnlyDictionary<string,double> Stats,
    double Weight, double Accuracy, double DamageBonus, string Source);
public sealed record RankedStatProjection(Dictionary<string,double> Stats, ItemStatEvidenceKind Kind,
    IReadOnlySet<string> UncertainStats, double? Weight, bool WeightIsBase, double? Accuracy,
    double? ObservedDamageBonus, string Evidence, IReadOnlyDictionary<string,ItemStatEvidenceKind> StatEvidence)
{
    public bool WeaponUncertain => UncertainStats.Contains("Damage") || UncertainStats.Contains("Ratio");
    internal RankedStatBasis Basis { get; init; } = new(0,0,0,RankedItemStats.Version);
    internal IReadOnlyList<StatRuleEvidence> Rules { get; init; } = [];
}

/// <summary>Tier-start projections. Observations never alter catalog bases or imply ownership/XP.</summary>
public static class RankedItemStats
{
    public const string Version = "EQL-2026-10-22/ranked-stats-v3-10pct";
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
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        if (!double.IsFinite(tierProgress) || tierProgress < 0 || tierProgress >= 1) throw new ArgumentOutOfRangeException(nameof(tierProgress));
        var stats = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string,ItemStatEvidenceKind>(StringComparer.OrdinalIgnoreCase);
        var uncertain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool weapon = item.Stats.ContainsKey("Damage") || ItemEquipmentIcons.IsWeapon(item);
        foreach (var (stat,value) in item.Stats)
        {
            if (stat == "Ratio") continue; // A ratio requires both documented inputs.
            double next = value;
            if ((rank > 0 || tierProgress > 0) && stat is not ("Delay" or "Haste"))
            {
                if (stat == "Damage") next = Math.Floor(value * (1 + (rank + tierProgress) * .1));
                else if (value > 0) next = Math.Max(Math.Floor(value * (1 + (rank + tierProgress) * .1)), value + rank);
                else if (value < 0) uncertain.Add(stat); // Negative-stat scaling is not established.
            }
            if ((rank > 0 || tierProgress > 0) && stat == "Haste") uncertain.Add(stat);
            stats[stat] = next;
            sources[stat] = rank == 0 && tierProgress == 0 || stat == "Delay" ? ItemStatEvidenceKind.Catalog : ItemStatEvidenceKind.Estimated;
        }
        string key = item.Key.Length > 0 ? item.Key : Identity.Key(item.Name);
        var observation = Observations.FirstOrDefault(o => o.ItemKey == key && o.Rank == rank && o.TierProgress == tierProgress
            && o.BaseChecks.All(s => item.Stats.TryGetValue(s.Key,out double value) && value == s.Value));
        if (observation != null) foreach (var (stat,value) in observation.Stats) { stats[stat] = value; uncertain.Remove(stat);sources[stat]=ItemStatEvidenceKind.ClientObserved; }
        if (stats.TryGetValue("Damage",out double damage) && stats.TryGetValue("Delay",out double delay) && delay > 0)
        { stats["Ratio"] = damage / delay;sources["Ratio"]=sources["Damage"]; if (uncertain.Contains("Damage")) uncertain.Add("Ratio"); }
        else if (weapon) { uncertain.Add("Damage"); uncertain.Add("Ratio"); }
        bool baseValues = rank == 0 && tierProgress == 0;
        string evidence = observation != null ? "Client-observed tier start · " + observation.Conditions + ". Current fractional XP is not recorded."
            : baseValues ? "Catalog +0 values (not ownership evidence) · " + item.WikiUrl
            : "Catalog baseline +0; scenario +" + rank + (tierProgress > 0 ? " with " + tierProgress.ToString("P0") + " tier progress" : " tier-start projection; current XP not recorded") + ". " +
                (weapon ? "Adopted weapon model: base damage × (1 + 0.10 × rank), rounded down; no ordinary-stat minimum. Delay stays fixed. " : "") +
                "Positive ordinary stats: cumulative 10%, rounded down, minimum +1 per completed tier. " +
                (uncertain.Count == 0 ? "" : "Unresolved ranked fields: " + string.Join(", ",uncertain) + ". ") + UpgradeSource + " · " + ConflictSource;
        string Reason(string stat) => !stats.ContainsKey(stat) ? "Required damage/delay input missing; no value invented" : sources.GetValueOrDefault(stat) == ItemStatEvidenceKind.ClientObserved ? observation!.Conditions :
            uncertain.Contains(stat) ? stat is "Damage" or "Ratio" ? "Required damage/delay input missing; no value invented" : stat == "Haste" ? "Catalog haste retained; ranked haste scaling not established" : "Negative-stat scaling not established; catalog value retained" :
            stat == "Delay" ? "Documented delay does not decrease" : baseValues ? "Catalog +0" : stat is "Damage" or "Ratio" ? "Adopted 10% weapon-damage model; floor; no minimum increase. Published 5% and 10% rules disagree; exact client observations take precedence." : "10% cumulative per tier; floor; minimum +1 at completed tier boundaries";
        var rules = stats.Keys.Concat(uncertain).Distinct().Select(stat => new StatRuleEvidence(stat,
            sources.GetValueOrDefault(stat) == ItemStatEvidenceKind.ClientObserved ? StatRuleConfidence.Observed : uncertain.Contains(stat) ? StatRuleConfidence.Unresolved : baseValues || stat == "Delay" ? StatRuleConfidence.Catalog : stat is "Damage" or "Ratio" ? StatRuleConfidence.AdoptedModel : StatRuleConfidence.DocumentedRule,
            Reason(stat), uncertain.Contains(stat) || stat is "Damage" or "Ratio" ? ConflictSource : UpgradeSource)).ToArray();
        return new(stats,observation != null ? ItemStatEvidenceKind.ClientObserved : !baseValues ? ItemStatEvidenceKind.Estimated : ItemStatEvidenceKind.Catalog,
            uncertain,observation?.Weight ?? item.Display.Weight,observation == null && (rank > 0 || tierProgress > 0) && item.Display.Weight.HasValue,
            observation?.Accuracy ?? (weapon ? rank * 2.5 : null),observation?.DamageBonus,evidence,sources) { Basis=new(0,rank,tierProgress,Version), Rules=rules };
    }
}
