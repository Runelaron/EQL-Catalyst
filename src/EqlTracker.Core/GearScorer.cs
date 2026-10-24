namespace EqlTracker.Core;

public sealed record ScoreContribution(string Stat, double Current, double Candidate, double Weight)
{
    public string StatLabel => Stat switch { "ResistFire" => "FR", "ResistCold" => "CR", "ResistMagic" => "MR", "ResistPoison" => "PR", "ResistDisease" => "DR", "ResistCorruption" => "Corr", "Damage" => "DMG", _ => Stat };
    public double Change => Candidate - Current;
    public double Points => Change * Weight;
    public string Explanation => Weight == 0 ? $"{Change:+0.##;-0.##;0} {Stat} · Not scored by this preset." : $"{Change:+0.##;-0.##;0} {Stat} contributes {Points:+0.##;-0.##;0} preference points ({Weight:0.##} per {Stat}).";
    public string Scoring => Weight == 0 ? "Not scored by this preset" : $"{Points:+0.##;-0.##;0} points";
}
public sealed record GearComparison(string Candidate, string Slot, string Replaces, double Score, double BaselineScore, double Delta, string Contributions, string Effects, string Eligibility, string Confidence)
{
    public LoadoutComparison? Evaluation { get; init; }
    public string Verdict => Evaluation?.Status == "Conditional" || Confidence.StartsWith("Conditional", StringComparison.Ordinal) ? "Conditional estimate" : Eligibility != "Eligible" ? Eligibility
        : Confidence.Contains("pet equipment has not been confirmed", StringComparison.Ordinal) ? "Confirm pet equipment"
        : Confidence.Contains("Self equipment baseline is unavailable", StringComparison.Ordinal) ? "Import Self equipment"
        : Confidence.StartsWith("Unknown", StringComparison.Ordinal) ? "Metadata pending"
        : Delta > 0 ? "Potential upgrade" : Delta < 0 ? "Lower stat score" : "Equal stat score";
    public double? ItemScore { get; init; }
    public List<ScoreContribution> Breakdown { get; init; } = [];
    public bool CanCompare => Eligibility == "Eligible" && Evaluation?.Status != "Conditional" && !Confidence.StartsWith("Conditional", StringComparison.Ordinal) && !Confidence.StartsWith("Unknown", StringComparison.Ordinal);
}
public static class GearScorer
{
    private static readonly Dictionary<string, string> Locations = new(StringComparer.OrdinalIgnoreCase) { ["PRIMARY"] = "Primary", ["SECONDARY"] = "Secondary", ["HEAD"] = "Head", ["FACE"] = "Face", ["EAR"] = "Ear", ["NECK"] = "Neck", ["SHOULDERS"] = "Shoulders", ["ARMS"] = "Arms", ["BACK"] = "Back", ["WRIST"] = "Wrist", ["CHEST"] = "Chest", ["HANDS"] = "Hands", ["FINGER"] = "Finger", ["LEGS"] = "Legs", ["FEET"] = "Feet", ["WAIST"] = "Waist", ["RANGE"] = "Range", ["AMMO"] = "Ammo" };
    public static readonly Dictionary<string, Dictionary<string, double>> Defaults = new()
    {
        ["Tank"] = new() { ["AC"] = 3, ["HP"] = .1, ["STA"] = .5 },
        ["Balanced Tank"] = new() { ["AC"] = 3, ["HP"] = .1, ["STA"] = .5, ["DEX"] = .5, ["AGI"] = .5 },
        ["Legacy Tank"] = new() { ["AC"] = 3, ["HP"] = .1, ["STA"] = .5 },
        ["Melee"] = new() { ["Ratio"] = 100, ["ATK"] = 1, ["STR"] = .5, ["DEX"] = .5, ["Haste"] = 3 },
        ["Pet DPS"] = new() { ["Ratio"] = 100, ["ATK"] = 1, ["STR"] = .5, ["DEX"] = .5, ["Haste"] = 3, ["AC"] = .25, ["HP"] = .01, ["STA"] = .1 },
        ["Pet damage"] = new() { ["Ratio"] = 100, ["ATK"] = 1, ["STR"] = .5, ["DEX"] = .5, ["Haste"] = 3 },
        ["Caster"] = new() { ["Mana"] = .1, ["INT"] = .5, ["HP"] = .05 }
    };
    public static List<string> EligibleClasses(LoadoutProfile profile, bool pet)
    {
        var result = profile.Classes.Select(CastingPriority.Code).ToList();
        if (pet) { result.Add("WAR"); result.AddRange(PetDefinition.For(profile.PetType).Classes); }
        return result.Distinct().ToList();
    }
    public static string Eligibility(ItemDefinition item, LoadoutProfile loadout, bool pet, IEnumerable<InventoryEntry>? augments, Catalog catalog)
    {
        if (pet && PetItemRules.Unsupported(item) is { Length: > 0 } unsupported) return unsupported;
        if (!item.MetadataComplete) return "Unknown item restrictions";
        if (item.Classes.Count == 0) return "Unknown item restrictions";
        var classes = item.Classes.Select(c => c == "ALL" ? c : CastingPriority.Code(c)).ToHashSet(); var slots = item.Slots.ToHashSet();
        foreach (var augment in augments ?? [])
        {
            var metadata = catalog.Find(augment.Name, augment.GameId == 0 ? null : augment.GameId);
            if (metadata == null || metadata.Classes.Count == 0) return "Unknown Exaltation restrictions";
            if (!metadata.Classes.Contains("ALL")) { if (classes.Contains("ALL")) classes = metadata.Classes.ToHashSet(); else classes.IntersectWith(metadata.Classes); }
            if (metadata.Slots.Count > 0) slots.IntersectWith(metadata.Slots);
        }
        if (!classes.Contains("ALL") && !classes.Overlaps(EligibleClasses(loadout, pet))) return "Requires another class / pet";
        if (slots.Count == 0) return "No compatible slot";
        if (pet && item.RequiredLevel > 0 && !loadout.PetLevel.HasValue) return "Confirm pet level (requires " + item.RequiredLevel + ")";
        if (item.RequiredLevel > (pet ? loadout.PetLevel ?? 0 : loadout.Level)) return $"Requires level {item.RequiredLevel}";
        if (!pet && item.Races.Count > 0 && !RaceNames.Allows(item.Races, loadout.Race)) return "Verify race restriction";
        if (item.Deity.Length > 0 && !item.Deity.Equals(loadout.Deity, StringComparison.OrdinalIgnoreCase)) return "Verify deity restriction";
        return "Eligible";
    }
    public static Dictionary<string, double> EffectiveStats(ItemDefinition item, int rank)
    {
        return RankedItemStats.Resolve(item, rank).Stats;
    }
    public static double Score(Dictionary<string, double> stats, Dictionary<string, double> weights) => weights.Sum(x => stats.GetValueOrDefault(x.Key) * x.Value);
    public static GearComparison Compare(ItemDefinition candidate, int rank, string slot, LoadoutProfile loadout, bool pet, Catalog catalog, Dictionary<string, double> weights, List<InventoryEntry>? candidateAugments = null, List<string>? transferredOut = null)
    {
        var equipment = pet ? loadout.PetEquipment : loadout.Equipment;
        var location = Locations.GetValueOrDefault(slot, slot);
        var baseline = equipment.Where(x => x.Location.Equals(location, StringComparison.OrdinalIgnoreCase)).ToList();
        if (baseline.Count > 1) baseline = baseline.OrderBy(e => catalog.Find(e.Name, e.GameId == 0 ? null : e.GameId) is { } definition ? Score(EffectiveStats(definition, e.Rank), weights) : double.PositiveInfinity).Take(1).ToList();
        if (candidate.TwoHanded && slot == "PRIMARY") baseline.AddRange(equipment.Where(x => x.Location.Equals("Secondary", StringComparison.OrdinalIgnoreCase)));
        if (slot == "SECONDARY")
        {
            var main = equipment.FirstOrDefault(x => x.Location.Equals("Primary", StringComparison.OrdinalIgnoreCase));
            if (main != null && catalog.Find(main.Name, main.GameId == 0 ? null : main.GameId)?.TwoHanded == true) baseline.Add(main);
        }
        var missing = new List<string>();
        if (!candidate.MetadataComplete) missing.Add("item slot or class restrictions are missing");
        if (equipment.Count == 0) missing.Add(pet ? "pet equipment has not been confirmed" : "Self equipment baseline is unavailable");
        if (equipment.Count > 0 && baseline.Count == 0) missing.Add("equipped slot baseline is unconfirmed");
        var ownStats = EffectiveStats(candidate, rank); var baseStats = new Dictionary<string, double>(); bool complete = candidate.MetadataComplete && baseline.Count > 0;
        foreach (var entry in baseline)
        {
            var definition = catalog.Find(entry.Name, entry.GameId == 0 ? null : entry.GameId);
            if (definition == null || !definition.MetadataComplete) { complete = false; missing.Add("baseline metadata missing for " + entry.Name); continue; }
            foreach (var pair in EffectiveStats(definition, entry.Rank)) baseStats[pair.Key] = baseStats.GetValueOrDefault(pair.Key) + pair.Value;
        }
        var score = Score(ownStats, weights); var baseScore = Score(baseStats, weights);
        var breakdown = weights.Keys.Concat(baseStats.Keys).Concat(ownStats.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Select(stat => new ScoreContribution(stat, baseStats.GetValueOrDefault(stat), ownStats.GetValueOrDefault(stat), weights.GetValueOrDefault(stat))).ToList();
        string detail = string.Join("\n", breakdown.Where(b => b.Change != 0).Select(b => b.Explanation));
        IEnumerable<string> SocketEffects(IEnumerable<InventoryEntry> augments) => augments.SelectMany<InventoryEntry, string>(a => a.EffectText.Length > 0 ? new[] { a.EffectText } : catalog.Find(a.Name, a.GameId == 0 ? null : a.GameId) is { Effects.Count: 1 } source ? source.Effects : new[] { "Unclassified socketed effect: " + a.Name });
        var oldEffects = baseline.SelectMany(e => (catalog.Find(e.Name, e.GameId == 0 ? null : e.GameId)?.Effects ?? []).Except(e.TransferredOutEffects).Concat(SocketEffects(e.Augments))).Distinct().ToList();
        var newEffects = candidate.Effects.Except(transferredOut ?? []).Concat(SocketEffects(candidateAugments ?? [])).Distinct().ToList();
        var confidence = !complete ? "Unknown: " + string.Join("; ", missing) : rank > 0 || candidateAugments?.Count > 0 || baseline.Any(e => e.Rank > 0 || e.Augments.Count > 0) ? "Estimated tier floor; XP and transferred effects need confirmation" : "Base item stats; effects / stacking are separate";
        bool disputed = RankedItemStats.Resolve(candidate,rank).UncertainStats.Any(s=>weights.GetValueOrDefault(s)!=0) || baseline.Any(e=>catalog.Find(e.Name,e.GameId==0?null:e.GameId) is {} d && RankedItemStats.Resolve(d,e.Rank).UncertainStats.Any(s=>weights.GetValueOrDefault(s)!=0));
        if (complete && disputed) confidence="Conditional ranked-stat estimate; weapon scaling / ranked haste unverified. " + confidence;
        var eligibility = Eligibility(candidate, loadout, pet, candidateAugments, catalog);
        if (candidateAugments?.Any(a => catalog.Find(a.Name, a.GameId == 0 ? null : a.GameId) is { } metadata && metadata.Slots.Count > 0 && !metadata.Slots.Contains(slot)) == true) eligibility = "Slot restricted by Exaltation";
        if (!candidate.Slots.Contains(slot)) eligibility = "Incompatible slot";
        return new(candidate.Name + (rank > 0 ? $" +{rank}" : ""), slot, baseline.Count == 0 ? "Empty / unconfirmed slot" : string.Join(" + ", baseline.Select(x => x.Name)), score, baseScore, score - baseScore, detail, "Effects gained: " + string.Join(", ", newEffects.Except(oldEffects)) + "\nEffects lost: " + string.Join(", ", oldEffects.Except(newEffects)), eligibility, confidence) { Breakdown = breakdown, ItemScore = candidate.MetadataComplete ? score : null };
    }
}
