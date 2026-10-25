using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed record ZoneContentEvidence(string Name, string Description, string Access, string Source);
public sealed record PatchMechanicEvidence(string Name, string Description, double? Value = null, string Unit = "");

// Official content facts are bundled for offline use. Numerical server formulas
// are deliberately separate from the adopted ranked-item preview model.
public static class OctoberContent
{
    public const string Version = "EQL-2026-10-22/content-v17";
    public const string Announcement = "https://www.everquestlegends.com/news/eql-nektropos-castle";
    public const string PatchNotes = "https://www.everquestlegends.com/patch-notes/eql-update-notes-10-22-2026";
    public static readonly DateTime PatchDate = new(2026, 10, 22);
    public static readonly ZoneContentEvidence[] Zones = [
        new("Lake Nerius", "Outdoor zone and approach to Nektropos Castle; includes a trading center.", "Castle access begins with Retainer Ozval here; the questline includes the Plane of Hate. The Ayr'Haeth unlock quest also starts here; its complete steps and rewards are not documented in the announcement.", Announcement),
        new("Nektropos Castle", "Endgame dungeon in the Everling home, with more than 20 named creatures.", "Locked entrance: complete the documented access questline beginning with Retainer Ozval in Lake Nerius. Lord Ollix Everling continues the castle story; exact steps and two final reward identities remain undocumented.", Announcement),
        new("Nektropos Undercroft", "Hidden starting area beneath Nektropos Castle; home of the Ayr'Haeth.", "Documented as the new race's starting area; entry coordinates and alternate travel routes are unconfirmed.", Announcement)
    ];
    public static readonly PatchMechanicEvidence[] Mechanics = [
        new("Monster Summoning", "Summoned Monster pets are Warrior/Berserker; individual damage/cadence coefficients are unpublished."),
        new("Pet stamina", "Summoned pet HP depends on STA. The conversion, rounding, caps and level dependence are unpublished."),
        new("Pet casting stats", "Summoned pets gain mana from both INT and WIS. Conversion, rounding and caps are unpublished."),
        new("Weapon selection", "Pet weapon preference was corrected to follow the weapon-bonus system. Full selection arithmetic remains unpublished; older weapon-order observations require rechecking."),
        new("28-delay weapons", "The pet damage-bonus bug at exactly 28 delay was fixed. No numerical bonus formula was published."),
        new("Taunt on", "Pet threat generation increases by 75%; this is a threat modifier, not a DPS multiplier.", 75, "percent increase"),
        new("Taunt off", "Pet threat generation decreases by 50%; this is a threat modifier, not a DPS multiplier.", 50, "percent decrease"),
        new("Pet command hold", "Attack, Back off and similar commands temporarily hold reactions to other hostile actions for 6 seconds.", 6, "seconds"),
        new("Charmed pet taunt", "The toggle changes threat but does not grant a taunt action. Public zones and solo instances no longer retain the described pet-taunt immunity."),
        new("Backstab kits", "Backstab-capable NPCs, including pets, can also kick and bash. Activation rates remain unresolved."),
        new("Frenzy", "Base damage increased based on Primary weapon ratio; the coefficient is unpublished."),
        new("Rampage", "Rank 2 is available at level 40 for Warriors and Berserkers. Availability does not establish learned rank.", 40, "required level for rank 2"),
        new("Berserker Stance", "Swing speed increased by an unspecified amount; self-damage and avoidance/mitigation bonuses were removed."),
        new("Spell reduction stances", "Spell-damage reductions also affect DoTs; stacking and individual coefficients require their own evidence.")
    ];
    public static string RaceDescription => "Ayr'Haeth (Half Dark Elf) · home: Nektropos Undercroft. Cursed Heritage: 10% increased tap/rune effectiveness; 20% reduced base health regeneration outdoors during the day. Life Tracking: increased sensory range for living NPCs. Hide, Sneak and Infravision. Unlock quest begins in Lake Nerius. New race levels start at 1; existing races remain at 50. Individual race level and equipment restrictions require confirmation; racial effects are not applied to logged totals or pet predictions.";
    public static string MeasurementEra(DateTime at) => at==default ? "Patch date unknown" : at.Date<PatchDate ? "Before October 22 update" : at.Date==PatchDate ? "October 22 release day · installed patch unconfirmed" : "After October 22 · installed patch unconfirmed";
    public static ZoneContentEvidence? Zone(string name) => Zones.FirstOrDefault(z => Identity.ZoneKey(z.Name) == Identity.ZoneKey(name));
    public static string ZoneHelp(string name) => Zone(name) is { } z ? z.Description + "\n" + (PublicContentNotes.Zone(name).Count>0?string.Join("\n",PublicContentNotes.Zone(name)):z.Access) + "\nClassic filter: inferred current-era classification.\nOfficial introduction: " + z.Source : "Filter goals by zone.";
    public static void Apply(Catalog catalog)
    {
        foreach (var zone in Zones) if (!catalog.Zones.Any(z => Identity.ZoneKey(z) == Identity.ZoneKey(zone.Name))) catalog.Zones.Add(zone.Name);
        foreach (var (url, title, date) in new[] { (Announcement, "Nektropos content announcement", "2026-10-20"), (PatchNotes, "October 22 update notes", "2026-10-22") })
            if (!catalog.Documents.Any(d => d.Url == url)) catalog.Documents.Add(new() { Url = url, Title = title, Timestamp = date });
    }
}

public static class RaceNames
{
    private static readonly (string Name, string[] Aliases)[] races = [
        ("Human", ["HUM"]), ("Barbarian", ["BAR"]), ("Erudite", ["ERU"]), ("Wood Elf", ["ELF"]),
        ("High Elf", ["HIE"]), ("Dark Elf", ["DEF"]), ("Half Elf", ["HEF"]), ("Dwarf", ["DWF"]),
        ("Troll", ["TRL"]), ("Ogre", ["OGR"]), ("Halfling", ["HFL"]), ("Gnome", ["GNM"]),
        ("Iksar", ["IKS"]), ("Vah Shir", ["VAH"]), ("Froglok", ["FRG"]),
        ("Ayr'Haeth", ["Ayr’Haeth", "Half Dark Elf", "Half Dark Elves"])
    ];
    private static string Key(string name) => Regex.Replace(name.Trim().Replace('’', '\''), @"\s+", " ").ToLowerInvariant();
    public static IEnumerable<string> Choices => races.Select(r => r.Name).Order(StringComparer.OrdinalIgnoreCase);
    public static string Canonical(string name) => races.FirstOrDefault(r => Key(r.Name) == Key(name) || r.Aliases.Any(a => Key(a) == Key(name))).Name ?? name.Trim();
    public static bool Known(string name) => races.Any(r => r.Name == Canonical(name));
    public static bool Allows(IEnumerable<string> restrictions, string race)
    {
        var values = restrictions.Select(Key).ToList();
        if (values.Any(v => Regex.IsMatch(v, @"\ball\b"))) return true;
        if (string.IsNullOrWhiteSpace(race)) return false;
        string canonical = Canonical(race);
        // Match longest complete names first, so Dark Elf cannot match inside
        // Half Dark Elf even when a source stores several races in one field.
        string pattern = @"(?<![\p{L}'])" + "(?:" + string.Join('|', races.SelectMany(r => r.Aliases.Prepend(r.Name)).Select(Key).Distinct().OrderByDescending(n => n.Length).Select(Regex.Escape)) + @")(?![\p{L}'])";
        return values.Any(v => v == Key(canonical) || Regex.Matches(v, pattern).Any(m => Canonical(m.Value) == canonical));
    }
}
