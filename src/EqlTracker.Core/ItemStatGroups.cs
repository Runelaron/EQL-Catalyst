namespace EqlTracker.Core;
/// <summary>Presentation order only. Scoring and item values are unchanged.</summary>
public static class ItemStatGroups
{
    public static readonly string[] Categories=["AC","Resources","Weapons","Stats","Resists"];
    private static readonly string[] order=["AC","STR","DEX","STA","CHA","WIS","INT","AGI","ATK","Haste","Damage","Delay","Ratio","Backstab","HP","Mana","Vitality","Endurance","HPRegen","ManaRegen","Regen","ResistFire","ResistDisease","ResistCold","ResistMagic","ResistPoison","ResistCorruption"];
    public static string Category(string stat)=>stat=="AC"?"AC":stat.StartsWith("Resist",StringComparison.OrdinalIgnoreCase)?"Resists":stat is "HP" or "Mana" or "Vitality" or "Endurance" or "HPRegen" or "ManaRegen" or "Regen"?"Resources":stat is "Damage" or "Delay" or "Ratio" or "Backstab"?"Weapons":"Stats";
    public static int Order(string stat){int index=Array.IndexOf(order,stat);return index<0?int.MaxValue:index;}
}
