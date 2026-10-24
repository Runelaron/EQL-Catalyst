using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal static class WikiContentQuality
{
    internal const string Version="wiki-quality-v1";
    internal const string IncompleteTemplate="Unfilled source template; equipment fields undocumented";
    private static readonly HashSet<string> placeholders=new(StringComparer.OrdinalIgnoreCase){"item name","npc name","zone name","quest name","vendor name","effect name","faction name","quest item","quest giver name","related quest","camp name","zone line","none","unknown","item_name","npc_name"};
    internal static bool Placeholder(string value)=>placeholders.Contains(value.Replace('_',' ').Trim())||value.Trim() is "?" or "???";
    internal static bool TemplateBlock(string block)=>Regex.IsMatch(block,@"Skill:\s*1H Blunt\s*/\s*2H Blunt\s*/\s*1H Slashing",RegexOptions.IgnoreCase)||block.Contains("STR: +?")&&block.Contains("WT: 0.0")&&Regex.IsMatch(block,@"Slot:.*PRIMARY.*SECONDARY.*RANGE.*AMMO.*ANY",RegexOptions.IgnoreCase);
    internal static bool TemplateRecord(ItemDefinition item)=>item.Display.TypeEvidence==IncompleteTemplate;
}
