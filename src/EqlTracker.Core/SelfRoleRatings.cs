using System.Globalization;

namespace EqlTracker.Core;

public enum CastingFamily { Unknown, Intelligence, Wisdom, Neutral }
public sealed record CastingPriority(bool Complete, int IntelligenceClasses, int WisdomClasses, int NeutralClasses, double IntelligenceShare, double WisdomShare, string Explanation)
{
    public const string Source = "https://eqltools.com/attributes";
    private static readonly Dictionary<string, CastingFamily> families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BRD"] = CastingFamily.Intelligence, ["ENC"] = CastingFamily.Intelligence, ["MAG"] = CastingFamily.Intelligence, ["NEC"] = CastingFamily.Intelligence, ["SHD"] = CastingFamily.Intelligence, ["WIZ"] = CastingFamily.Intelligence,
        ["BST"] = CastingFamily.Wisdom, ["CLR"] = CastingFamily.Wisdom, ["DRU"] = CastingFamily.Wisdom, ["PAL"] = CastingFamily.Wisdom, ["RNG"] = CastingFamily.Wisdom, ["SHM"] = CastingFamily.Wisdom,
        ["BER"] = CastingFamily.Neutral, ["MNK"] = CastingFamily.Neutral, ["ROG"] = CastingFamily.Neutral, ["WAR"] = CastingFamily.Neutral
    };
    private static readonly string[] names = ["Bard", "Enchanter", "Magician", "Necromancer", "Shadow Knight", "Wizard", "Beastlord", "Cleric", "Druid", "Paladin", "Ranger", "Shaman", "Berserker", "Monk", "Rogue", "Warrior"];
    private static readonly string[] codes = ["BRD", "ENC", "MAG", "NEC", "SHD", "WIZ", "BST", "CLR", "DRU", "PAL", "RNG", "SHM", "BER", "MNK", "ROG", "WAR"];
    public static string Code(string value)
    {
        string key = value.Trim().Replace(" ", "").Replace("-", "");
        if (key.Equals("SK", StringComparison.OrdinalIgnoreCase) || key.Equals("Shadowknight", StringComparison.OrdinalIgnoreCase)) return "SHD";
        for (int i = 0; i < names.Length; i++) if (key.Equals(names[i].Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) return codes[i];
        return key.ToUpperInvariant();
    }
    public static CastingFamily Family(string value) => families.GetValueOrDefault(Code(value), CastingFamily.Unknown);
    public static CastingPriority Resolve(IEnumerable<string> classes)
    {
        var codes = classes.Select(Code).ToArray();
        int intelligence = codes.Count(c => Family(c) == CastingFamily.Intelligence), wisdom = codes.Count(c => Family(c) == CastingFamily.Wisdom), neutral = codes.Count(c => Family(c) == CastingFamily.Neutral);
        if (codes.Length != 3 || codes.Distinct().Count() != 3 || codes.Any(c => Family(c) == CastingFamily.Unknown)) return new(false, intelligence, wisdom, neutral, 0, 0, "Confirm three distinct active classes. Missing or unknown classes are not neutral; automatic casting priorities are incomplete.");
        double i = intelligence >= 2 ? 1 : wisdom >= 2 ? 0 : intelligence == 1 && wisdom == 1 ? .5 : intelligence == 1 ? 1 : 0;
        double w = wisdom >= 2 ? 1 : intelligence >= 2 ? 0 : intelligence == 1 && wisdom == 1 ? .5 : wisdom == 1 ? 1 : 0;
        string explanation = $"Classes: {string.Join(" / ", codes)} · {intelligence} INT / {wisdom} WIS / {neutral} neutral. " + (i == .5 ? "One INT + one WIS + one neutral: split the casting priority equally." : i == 1 ? "INT is the casting priority; WIS changes remain visible but receive no casting-priority points." : w == 1 ? "WIS is the casting priority; INT changes remain visible but receive no casting-priority points." : "No casting class: no mana or casting-attribute priority.");
        return new(true, intelligence, wisdom, neutral, i, w, explanation + " This is the rating's priority rule, not a mana-pool formula.");
    }
}

public sealed record SelfRoleResolution(string Name, int Version, Dictionary<string, double> Weights, CastingPriority Casting, bool Automatic, bool Complete, List<string> Reasons)
{
    public string Identity => Name + "|" + Version + "|" + Casting.Explanation + "|" + Complete + "|" + string.Join(';', Weights.OrderBy(w => w.Key).Select(w => w.Key + "=" + w.Value.ToString("R", CultureInfo.InvariantCulture)));
    public string Exclusion(string stat) => stat is "INT" or "WIS" && Automatic ? "Outside this build's resolved casting priority; this does not claim it cannot affect in-game mana." : stat is "Damage" or "Delay" ? "Weapon damage and delay are represented through ratio, rather than counted again." : "This role assigns no preference points to this stat; the item change still matters outside this rating.";
}

public static class SelfRoleRatings
{
    public const int Version = 2;
    public static readonly string[] Goals = ["Damage", "Balanced", "Survival"];
    private static readonly string[] resists = ["ResistFire", "ResistCold", "ResistMagic", "ResistDisease", "ResistPoison"];
    private static readonly string[] retired = ["Tank", "Balanced Tank", "Melee", "Caster", "Legacy Tank"];
    public static bool IsRetired(string name) => retired.Contains(name, StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<string> Names(CharacterProfile profile) => Goals.Concat(profile.Weights.Keys.Where(n => !n.StartsWith("Pet", StringComparison.OrdinalIgnoreCase)).Concat(profile.SelfRoleOverrides.Keys).Where(n => !Goals.Contains(n) && !IsRetired(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n));
    public static void Apply(CharacterProfile profile)
    {
        var converted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loadout in profile.Loadouts)
        {
            string old = loadout.Preset;
            if (!IsRetired(old)) continue;
            string goal = old.Equals("Legacy Tank", StringComparison.OrdinalIgnoreCase) ? "Survival" : old.Equals("Tank", StringComparison.OrdinalIgnoreCase) || old.Equals("Balanced Tank", StringComparison.OrdinalIgnoreCase) ? "Balanced" : "Damage";
            string definition = retired.First(n => n.Equals(old, StringComparison.OrdinalIgnoreCase));
            var weights = profile.SelfRoleOverrides.FirstOrDefault(p => p.Key.Equals(old, StringComparison.OrdinalIgnoreCase)).Value ?? profile.Weights.FirstOrDefault(p => p.Key.Equals(old, StringComparison.OrdinalIgnoreCase)).Value;
            bool customized = weights != null && GearScorer.Defaults.TryGetValue(definition, out var defaults) && (weights.Any(w => defaults.GetValueOrDefault(w.Key) != w.Value) || defaults.Any(w => weights.GetValueOrDefault(w.Key) != w.Value));
            if (customized)
            {
                if (!converted.TryGetValue(old, out string? custom))
                {
                    custom = "Custom " + goal; int number = 2;
                    while (profile.SelfRoleOverrides.Keys.Concat(profile.Weights.Keys).Contains(custom, StringComparer.OrdinalIgnoreCase)) custom = "Custom " + goal + " " + number++;
                    profile.SelfRoleOverrides[custom] = new(weights!); converted[old] = custom;
                }
                goal = custom;
            }
            if (loadout.PreviousSelfRole.Length == 0) loadout.PreviousSelfRole = old;
            loadout.Preset = goal;
        }
        profile.SelfRoleVersion = Version;
    }
    public static SelfRoleResolution Resolve(CharacterProfile profile, LoadoutProfile loadout, string? selected = null)
    {
        string name = selected ?? loadout.Preset; var casting = CastingPriority.Resolve(loadout.Classes);
        if (profile.SelfRoleOverrides.TryGetValue(name, out var custom)) return new(name, Version, new(custom), casting, false, true, ["Custom Self weights. Automatic class scaling is replaced by your exact values; Pet weights are unchanged."]);
        if (!Goals.Contains(name)) return new(name, 0, new(profile.Weights.GetValueOrDefault(name) ?? GearScorer.Defaults["Legacy Tank"]), casting, false, true,
            [name == "Legacy Tank" ? "Legacy Tank preserves the original AC / HP / STA formula for compatibility." : "Previous or custom role: its saved weights are preserved, without automatic class scaling."]);
        bool damage = name == "Damage", balanced = name == "Balanced";
        var weights = new Dictionary<string, double> { ["AC"] = damage ? 1 : balanced ? 2 : 3, ["HP"] = damage ? .05 : balanced ? .1 : .15, ["STA"] = damage ? .2 : balanced ? .4 : .6, ["AGI"] = damage ? .2 : balanced ? .4 : .5 };
        double affinity(string code) => CastingPriority.Code(code) switch { "WAR" or "MNK" or "ROG" or "BER" or "PAL" or "SHD" or "RNG" or "BST" => 1, "BRD" => .75, "CLR" or "DRU" or "SHM" => .25, _ => .1 };
        double martial = casting.Complete ? loadout.Classes.Average(affinity) : 0;
        double casters = casting.Complete ? (casting.IntelligenceClasses + casting.WisdomClasses) / 3d : 0;
        bool support = loadout.Classes.Select(CastingPriority.Code).Any(c => c is "CLR" or "DRU" or "SHM" or "PAL" or "SHD" or "BST");
        double sustain = support ? 1.25 : 1;
        weights["Ratio"] = (damage ? 100 : balanced ? 50 : 15) * martial;
        weights["ATK"] = (damage ? 1 : balanced ? .5 : .15) * martial;
        weights["Haste"] = (damage ? 3 : balanced ? 1.5 : .5) * martial;
        weights["STR"] = (damage ? .5 : balanced ? .35 : .15) * martial + .05;
        weights["DEX"] = (damage ? .5 : balanced ? .35 : .15) * martial;
        weights["Mana"] = (damage ? .1 : balanced ? .1 : .08) * casters * sustain;
        weights["Endurance"] = (balanced ? .04 : .03) * martial + (balanced ? .03 : .02) * casters;
        double budget = (damage ? .5 : balanced ? .4 : .25) * casters * sustain;
        weights["INT"] = budget * casting.IntelligenceShare; weights["WIS"] = budget * casting.WisdomShare;
        foreach (string stat in resists) weights[stat] = damage ? .1 : balanced ? .15 : .25;
        var reasons = new List<string>
        {
            damage ? "Damage prioritizes weapon ratio, attack and haste where classes support melee, while retaining casting resources and secondary survival." : balanced ? "Balanced gives strong AC and health priorities alongside melee, casting resources and resistances." : "Survival puts AC, health, stamina, agility and resistances first, while retaining resources and some offense.",
            casting.Explanation,
            $"Martial preference factor {martial:0.###}: average of 1 for martial/hybrid classes, 0.75 Bard, 0.25 priest classes, 0.1 pure INT casters. Casting factor {casters:0.###}: mana-using classes / 3. These are editable preference design choices, not measured game multipliers.",
            support ? "Mana and casting-attribute priorities receive a 1.25 sustain preference for healing/lifetap-capable classes. This values spell resources even under Damage; it does not predict healing, lifetaps or Spellblade triggers." : "No healing/lifetap sustain preference is added.",
            "STR includes 0.05 carrying-utility preference plus its martial priority. No STR-to-damage, STA-to-HP, or INT/WIS-to-mana conversion is invented. Direct item mana, HP and documented raw Endurance are scored separately; no ability-use or endurance conversion is predicted. Weapon damage and delay contribute through ratio.",
            "Only confirmed limits adjust effective changes. AC remains strongly weighted in Balanced and Survival; effects, procs, stances and unverified interactions are separate."
        };
        return new(name, Version, weights, casting, true, casting.Complete, reasons);
    }
}
