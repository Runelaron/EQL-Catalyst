namespace EqlTracker.Core;

public sealed class ItemDisplayMetadata
{
    public string Type { get; set; } = "";
    public string Skill { get; set; } = "";
    public List<string> Flags { get; set; } = [];
    public double? Weight { get; set; }
    public string Size { get; set; } = "";
    public int? Range { get; set; }
    public string Source { get; set; } = "";
    public long Revision { get; set; }
    public string TypeEvidence { get; set; } = "";
    public WeaponShape WeaponShape { get; set; }
    public string WeaponShapeEvidence { get; set; } = "";
}

public static class PetItemRules
{
    public const int MetadataVersion = 9;
    public const string Version = "EQL-2026-10-21/pet-use-v2";
    public static bool Fits(ItemDefinition item, EquipmentPosition position, bool theorycraft = false) =>
        EquipmentPositions.Fits(item, position, true) && (theorycraft || item.Stats.GetValueOrDefault("Damage") <= 0 || position.Slot is "Primary" or "Secondary");
    public static bool CombatSlot(string slot, bool theorycraft = false) => slot is "Primary" or "Secondary" || theorycraft && slot == "Range";
    public static string Unsupported(ItemDefinition item)
    {
        if (item.Display.Skill.Equals("Archery", StringComparison.OrdinalIgnoreCase) || item.Display.Type.Equals("Bow", StringComparison.OrdinalIgnoreCase)) return "Pets cannot currently use bows";
        // Old cached records already contain sourced RANGE/DMG/Delay, even without Skill.
        if (item.Slots.Any(s => EquipmentPositions.Normalize(s) is "Range" or "Ammo") && item.Stats.GetValueOrDefault("Damage") > 0 && !item.Slots.Any(s => EquipmentPositions.Normalize(s) is "Primary" or "Secondary")) return "Ranged weapons are not supported pet equipment";
        return "";
    }
    public static string Type(ItemDefinition item)
    {
        if (item.Display.Skill.Length > 0) return CanonicalType(item.Display.Skill);
        string documented = CanonicalType(item.Display.Type);
        var slots = item.Slots.Select(EquipmentPositions.Normalize).ToList();
        var armor = ItemEquipmentIcons.ArmorSlots(item);
        if (armor.Count > 0 && !ItemEquipmentIcons.IsWeapon(item) && documented is not ("Shield" or "Held item")) return string.Join(" / ", armor.Select(s => ItemEquipmentIcons.ArmorLabel(s)));
        if (documented.Length > 0 && !documented.Equals("Armor", StringComparison.OrdinalIgnoreCase)) return documented;
        if (slots.Contains("Ammo")) return "Ammunition";
        if (slots.Any(s => s is "Primary" or "Secondary" or "Range") && (item.MetadataComplete || item.Display.Source.Length > 0))
            return item.Stats.GetValueOrDefault("Damage") > 0 ? slots.Contains("Range") ? "Ranged weapon" : "Melee weapon" : "Held item";
        return documented.Equals("Armor", StringComparison.OrdinalIgnoreCase) ? "Armor · slot unverified" : "Unknown";
    }
    public static string CanonicalType(string type) => type.Trim().TrimEnd('/') .Trim().ToUpperInvariant() switch { "ARCHERY" => "Bow", "THROWINGV1" or "THROWINGV2" or "THROWING" => "Throwing", "SHIELD" => "Shield", "ARMOR" => "Armor", "1H SLASH" or "1H SLASHING" => "1H Slashing", "2H SLASH" or "2H SLASHING" => "2H Slashing", "1H BLUNT" or "1H CRUSHING" => "1H Blunt", "2H BLUNT" or "2H CRUSHING" => "2H Blunt", "PIERCING" => "Piercing", "1H PIERCING" => "1H Piercing", "2H PIERCING" => "2H Piercing", "HAND TO HAND" => "Hand to Hand", _ => type.Trim().TrimEnd('/').Trim() };
    public static bool ShowCandidate(ItemDefinition item, LoadoutProfile context, Catalog catalog, bool theorycraft)
    {
        if (theorycraft) return true;
        if (Unsupported(item).Length > 0) return false;
        string eligibility = GearScorer.Eligibility(item, context, true, null, catalog);
        return eligibility == "Eligible" || eligibility.StartsWith("Unknown") || eligibility.StartsWith("Confirm pet level") || eligibility.StartsWith("Verify");
    }
    public static bool CanAssume(string reason) => reason is "Pets cannot currently use bows" or "Ranged weapons are not supported pet equipment" or "Requires another class / pet" || reason.StartsWith("Requires level ");
    public sealed class WikiFixture
    {
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public long Revision { get; set; }
    }
    public static void ApplyDisplayEvidence(Catalog catalog, string path)
    {
        if (!File.Exists(path)) return;
        foreach (var fixture in Json.Read<List<WikiFixture>>(path))
        {
            var parsed = WikiClient.ParseItem(fixture.Title, fixture.Text, fixture.Revision);
            var existing = catalog.Find(parsed.Name) ?? catalog.Find(fixture.Title);
            if (existing == null) { catalog.Items.Add(parsed); catalog.InvalidateIndexes(); }
            else if (existing.Display.Revision <= parsed.Display.Revision) { existing.Display = parsed.Display; if (existing.Revision <= parsed.Revision && parsed.Stats.TryGetValue("Endurance", out var endurance)) existing.Stats["Endurance"] = endurance; }
        }
    }
}

public sealed class PetTheorycraftWorkspace
{
    public PetBuild Working { get; private set; }
    public string SavedId { get; private set; }
    private string saved;
    public long Revision { get; private set; }
    public bool Dirty => saved != Serialize(Working);
    private static string Serialize(PetBuild build) => System.Text.Json.JsonSerializer.Serialize(build, Json.Options);
    public PetTheorycraftWorkspace(PetBuild source)
    {
        Working = EquipmentPositions.Copy(source); SavedId = source.IsTheorycraft ? source.Id : "";
        if (!source.IsTheorycraft) { Working.Id = Guid.NewGuid().ToString("N"); Working.Name = "Theorycraft · " + source.Name; }
        Working.IsTheorycraft = true; Working.EditHistory = []; Working.Undo = [];
        saved = SavedId.Length > 0 ? Serialize(Working) : "";
    }
    public void Changed() => Revision++;
    public PetBuild Save(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a build name.");
        Working.Name = name.Trim(); Working.IsTheorycraft = true; SavedId = Working.Id; Revision++;
        saved = Serialize(Working); return EquipmentPositions.Copy(Working);
    }
}
