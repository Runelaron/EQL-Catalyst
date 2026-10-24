namespace EqlTracker.Core;

public enum TrackerTabStyle { Human, Barbarian, Erudite, WoodElf, HighElf, DarkElf, HalfElf, Dwarf, Troll, Ogre, Halfling, Gnome, Iksar, Froglok }
public sealed record TrackerTabChoice(TrackerTabStyle Id, string Name);
public static class TrackerPresentation
{
    public static readonly TrackerTabChoice[] Tabs = [new(TrackerTabStyle.Human,"Human"), new(TrackerTabStyle.Barbarian,"Barbarian"), new(TrackerTabStyle.Erudite,"Erudite"), new(TrackerTabStyle.WoodElf,"Wood Elf"), new(TrackerTabStyle.HighElf,"High Elf"), new(TrackerTabStyle.DarkElf,"Dark Elf"), new(TrackerTabStyle.HalfElf,"Half Elf"), new(TrackerTabStyle.Dwarf,"Cut Stone - Dwarf"), new(TrackerTabStyle.Troll,"Troll"), new(TrackerTabStyle.Ogre,"Ogre"), new(TrackerTabStyle.Halfling,"Halfling"), new(TrackerTabStyle.Gnome,"Gnome"), new(TrackerTabStyle.Iksar,"Iksar"), new(TrackerTabStyle.Froglok,"Froglok")];
    public static string ThemeFamily(string theme) => theme.Equals("Midnight", StringComparison.OrdinalIgnoreCase) ? "Frost" : ThemeCatalog.Resolve(theme).Family;
    public static string Secondary(CharacterProfile profile, string theme) => profile.SecondaryTextByTheme.GetValueOrDefault(ThemeFamily(theme), profile.SecondaryText);
    public static void Migrate(CharacterProfile profile)
    {
        if (profile.OverlayMeter == null)
        {
            var previous = profile.Meter;
            profile.OverlayMeter = new() { Actor = previous.Actor is "Pet" or "Player" or "Combined" ? previous.Actor : "Pet", Sort = previous.Sort is "Damage" or "Hits" or "Average" or "DPS" ? previous.Sort : "Damage", Scope = previous.Scope == "Session" ? "Session" : "Fight" };
        }
        profile.OverlayFooterDisplay ??= DpsDisplayChoice.From(profile.OverlayMeter);
        profile.SecondaryTextByTheme.TryAdd("Frost", "Candlelight Cream");
        profile.SecondaryTextByTheme.TryAdd("Vellum", "Warm Gold");
        profile.SecondaryTextByTheme.TryAdd("Shadowman", "Ash Silver");
        if (!Enum.IsDefined(profile.TrackerTabs)) profile.TrackerTabs = TrackerTabStyle.Dwarf;
    }
}
