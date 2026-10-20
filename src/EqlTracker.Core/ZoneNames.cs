namespace EqlTracker.Core;

public static class ZoneNames
{
    private static readonly Dictionary<string,string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["northern karana"]="north karana", ["northern plains of karana"]="north karana", ["western karana"]="west karana", ["western plains of karana"]="west karana",
        ["eastern plains of karana"]="east karana", ["southern plains of karana"]="south karana", ["northern desert of ro"]="north ro", ["southern desert of ro"]="south ro",
        ["mistmoore castle"]="mistmoore", ["castle mistmoore"]="mistmoore", ["cazic-thule"]="cazic thule", ["cazic thule (zone)"]="cazic thule",
        ["runnyeye citadel"]="runnyeye", ["kerra isle"]="kerra island", ["lake rathe"]="lake rathetear", ["qeynos catacombs"]="qeynos aqueducts",
        ["permafrost keep"]="permafrost", ["sol a"]="solusek's eye", ["sol b"]="nagafen's lair", ["neriak - foreign quarter"]="neriak foreign quarter"
    };
    public static string Key(string normalized) => Aliases.GetValueOrDefault(normalized,normalized);
}
