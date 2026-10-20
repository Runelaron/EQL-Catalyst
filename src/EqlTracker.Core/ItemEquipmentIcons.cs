namespace EqlTracker.Core;

public enum WeaponShape { Unknown, Sword, Axe, Hammer, Mace, Dagger, Spear, Staff, Bow, Fist, Claw }
public sealed record EquipmentIconDescriptor(string Role, string Label, string Evidence);

// Presentation evidence only: never changes equipment eligibility or scoring.
public static class ItemEquipmentIcons
{
    private static readonly Dictionary<string, (string Label, string Role)> armor = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Head"]=("Head armor","item-head"), ["Face"]=("Face armor","item-face"), ["Shoulders"]=("Shoulder armor","item-shoulders"),
        ["Arms"]=("Arm armor","item-arms"), ["Wrist"]=("Wrist armor","item-wrists"), ["Chest"]=("Chest armor","item-armor"),
        ["Hands"]=("Gloves","item-gloves"), ["Legs"]=("Leg armor","item-legs"), ["Feet"]=("Boots","item-boots"),
        ["Back"]=("Cloak","item-cloak"), ["Waist"]=("Belt","item-belt"), ["Finger"]=("Ring","item-ring"), ["Ear"]=("Earring","item-earring"), ["Neck"]=("Necklace","item-necklace")
    };
    public static string ArmorLabel(string slot) => armor.GetValueOrDefault(EquipmentPositions.Normalize(slot)).Label ?? slot;
    public static IReadOnlyList<string> ArmorSlots(ItemDefinition item) => item.Slots.Select(EquipmentPositions.Normalize).Where(armor.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s,StringComparer.OrdinalIgnoreCase).ToArray();
    public static string Skill(ItemDefinition item) => PetItemRules.CanonicalType(item.Display.Skill.Length > 0 ? item.Display.Skill : item.Display.Type);
    public static bool IsWeapon(ItemDefinition item) => Skill(item) is "1H Slashing" or "2H Slashing" or "1H Blunt" or "2H Blunt" or "Piercing" or "1H Piercing" or "2H Piercing" or "Bow" or "Throwing" or "Hand to Hand" or "Melee weapon" or "Ranged weapon" || item.Display.WeaponShape != WeaponShape.Unknown && item.Display.WeaponShapeEvidence.Length > 0;
    public static IReadOnlyList<EquipmentIconDescriptor> For(ItemDefinition item)
    {
        string evidence = item.Display.TypeEvidence.Length > 0 ? item.Display.TypeEvidence : "Documented wearable slots and weapon facts";
        if (!IsWeapon(item) && !PetItemRules.CanonicalType(item.Display.Type).Equals("Shield",StringComparison.OrdinalIgnoreCase) && ArmorSlots(item) is { Count: > 0 } slots)
            return slots.Select(s=>new EquipmentIconDescriptor(armor[s].Role,armor[s].Label,"Slot: " + s + " · " + evidence)).ToArray();
        string type=PetItemRules.Type(item), skill=Skill(item), role;
        bool two=skill.StartsWith("2H ",StringComparison.Ordinal) || item.TwoHanded;
        var shape=item.Display.WeaponShape;
        bool shapeVerified=shape!=WeaponShape.Unknown && item.Display.WeaponShapeEvidence.Length>0;
        if (shapeVerified)
        {
            role=shape switch { WeaponShape.Sword=>two?"item-greatsword":"glyph-sword", WeaponShape.Axe=>two?"item-axe-2h":"item-axe-1h", WeaponShape.Hammer=>two?"item-hammer-2h":"item-hammer-1h", WeaponShape.Mace=>two?"item-mace-2h":"item-mace-1h", WeaponShape.Dagger=>"glyph-dagger", WeaponShape.Spear=>"glyph-spear", WeaponShape.Staff=>"item-staff", WeaponShape.Bow=>"glyph-bow", WeaponShape.Fist=>"glyph-fist", WeaponShape.Claw=>"glyph-claw", _=>"glyph-unknown" };
            // Do not invent handedness when the page supplies only a shape.
            if (shape is WeaponShape.Axe or WeaponShape.Hammer or WeaponShape.Mace && !two && !skill.StartsWith("1H ",StringComparison.Ordinal)) role="glyph-unknown";
            string label=shape==WeaponShape.Sword && two?"Greatsword":shape.ToString();
            return [new(role,type + " · " + label,item.Display.WeaponShapeEvidence)];
        }
        role=type switch { "2H Slashing"=>"item-greatsword", "1H Slashing"=>"glyph-sword", "1H Blunt"=>"item-mace-1h", "2H Blunt"=>"item-mace-2h", "Piercing" or "1H Piercing"=>"glyph-dagger", "2H Piercing"=>"glyph-spear", "Bow"=>"glyph-bow", "Shield"=>"glyph-shield", "Hand to Hand"=>"glyph-fist", "Throwing" or "Ammunition"=>"item-ammunition", "Held item"=>"item-held", "Quest item"=>"item-scroll", "Tradeskill item"=>"item-bag", "Potion"=>"item-potion", _=>"glyph-unknown" };
        return [new(role,type,evidence + (IsWeapon(item)?" · category silhouette; specific weapon shape unverified":""))];
    }
    public static void ReadShape(ItemDefinition item, string documentedShape, string source)
    {
        // Only explicit structured shape/type fields are supplied here, never item names.
        var words=documentedShape.Trim().ToUpperInvariant();
        var shape=words switch { "SWORD" or "GREATSWORD" or "LONGSWORD"=>WeaponShape.Sword, "AXE" or "BATTLEAXE" or "GREATAXE"=>WeaponShape.Axe, "HAMMER" or "WARHAMMER" or "MAUL"=>WeaponShape.Hammer, "MACE" or "GREATMACE"=>WeaponShape.Mace, "DAGGER"=>WeaponShape.Dagger, "SPEAR"=>WeaponShape.Spear, "STAFF" or "QUARTERSTAFF"=>WeaponShape.Staff, "BOW"=>WeaponShape.Bow, "FIST" or "HAND TO HAND"=>WeaponShape.Fist, "CLAW" or "CLAWS"=>WeaponShape.Claw, _=>WeaponShape.Unknown };
        if(shape!=WeaponShape.Unknown){item.Display.WeaponShape=shape;item.Display.WeaponShapeEvidence=source;}
    }
}
