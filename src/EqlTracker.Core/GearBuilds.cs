using System.Security.Cryptography;
using System.Text;

namespace EqlTracker.Core;

public enum EquipmentKnowledge { Unknown, Empty, Occupied }
public sealed class EquipmentPosition
{
    [System.Text.Json.Serialization.JsonIgnore] public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Slot { get; set; } = "";
    public int Index { get; set; } = 1;
    public EquipmentKnowledge Knowledge { get; set; }
    public InventoryEntry? Item { get; set; }
    public string Label => (Slot == "Range" ? "Ranged" : Slot) + (EquipmentPositions.Paired(Slot) ? " " + Index : "");
    public string Name => Item?.Name ?? (Knowledge == EquipmentKnowledge.Empty ? "Empty · confirmed" : "Unknown · confirm equipment");
    public override string ToString() => Id == "*" ? "All slots" : Label + " · " + Name;
}
public static class EquipmentPositions
{
    public static readonly string[] Names = ["Head", "Face", "Ear", "Neck", "Shoulders", "Back", "Arms", "Wrist", "Hands", "Finger", "Chest", "Legs", "Feet", "Waist", "Primary", "Secondary", "Range", "Ammo", "Any Slot"];
    public static string Normalize(string value) => value.Trim().ToUpperInvariant() switch { "FINGERS" or "FINGER" => "Finger", "RANGED" or "RANGE" => "Range", "SHOULDER" => "Shoulders", "ANY SLOT" or "ANY" => "Any Slot", _ => Names.FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? value.Trim() };
    public static bool Paired(string slot) => Normalize(slot) is "Ear" or "Wrist" or "Finger" or "Any Slot";
    public static bool IsSlot(string value) => Names.Contains(Normalize(value));
    public static string Key(string slot, int index = 1) => Normalize(slot).ToUpperInvariant() + ":" + index;
    public static List<EquipmentPosition> FromEntries(IEnumerable<InventoryEntry> entries, bool fillUnknown = true)
    {
        var counts = new Dictionary<string, int>(); var result = new List<EquipmentPosition>();
        foreach (var entry in entries.Where(InventoryParser.IsEquipped))
        {
            string slot = Normalize(entry.Location); if (!IsSlot(slot)) continue;
            int index = counts[slot] = counts.GetValueOrDefault(slot) + 1;
            result.Add(new() { Id = Key(slot, index), Slot = slot, Index = index, Knowledge = EquipmentKnowledge.Occupied, Item = entry });
        }
        if (fillUnknown) Complete(result); return result;
    }
    public static void Complete(List<EquipmentPosition> positions)
    {
        foreach (string slot in Names) for (int index = 1; index <= (Paired(slot) ? 2 : 1); index++)
            if (!positions.Any(s => s.Id == Key(slot, index))) positions.Add(new() { Id = Key(slot, index), Slot = slot, Index = index });
    }
    public static T Copy<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;
    public static string Fingerprint(IEnumerable<EquipmentPosition> positions) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', positions.Where(s => s.Knowledge != EquipmentKnowledge.Unknown).OrderBy(s => s.Id).Select(s => s.Id + ":" + s.Knowledge + ":" + (s.Item == null ? "" : System.Text.Json.JsonSerializer.Serialize(s.Item)))))));
    public static List<EquipmentPosition> For(LoadoutProfile loadout, bool pet)
    {
        if (pet) return FromEntries(loadout.PetEquipment);
        var result = loadout.EquipmentPositions.Count > 0 ? Copy(loadout.EquipmentPositions) : FromEntries(loadout.Equipment);
        Complete(result); return result;
    }
    public static bool Fits(ItemDefinition item, EquipmentPosition position, bool pet) => position.Slot == "Any Slot" && !pet ? item.Slots.Count > 0 : item.Slots.Any(s => Normalize(s) == position.Slot);
    public static List<EquipmentPosition> Replace(IReadOnlyList<EquipmentPosition> before, string id, InventoryEntry entry, Catalog catalog)
    {
        var after = before.Select(s => new EquipmentPosition { Id = s.Id, Slot = s.Slot, Index = s.Index, Knowledge = s.Knowledge, Item = s.Item }).ToList(); var selected = after.Single(s => s.Id == id); selected.Item = entry; selected.Knowledge = EquipmentKnowledge.Occupied;
        var definition = catalog.FindKey(entry.ConfirmedItemKey) ?? catalog.Find(entry.Name, entry.GameId == 0 ? null : entry.GameId);
        if (selected.Slot == "Primary" && definition?.TwoHanded == true) Clear("Secondary");
        if (selected.Slot == "Secondary" && before.FirstOrDefault(s => s.Slot == "Primary")?.Item is { } primary && (catalog.FindKey(primary.ConfirmedItemKey) ?? catalog.Find(primary.Name, primary.GameId == 0 ? null : primary.GameId))?.TwoHanded == true) Clear("Primary");
        return after;
        void Clear(string slot) { foreach (var other in after.Where(s => s.Slot == slot)) { other.Item = null; other.Knowledge = EquipmentKnowledge.Empty; } }
    }
}

public sealed class GearDraft
{
    public List<GearBuildEdit> EditHistory { get; set; } = [];
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsTheorycraft { get; set; }
    public List<string> Assumptions { get; set; } = [];
    public string Name { get; set; } = "Equipment draft";
    public List<EquipmentPosition> Positions { get; set; } = [];
    public List<List<EquipmentPosition>> Undo { get; set; } = [];
}
public sealed class GearBuildEdit
{
    public string Name { get; set; } = "";
    public List<string> Assumptions { get; set; } = [];
}
public sealed class StatObservation
{
    public double Value { get; set; }
    public bool UncappedTotalKnown { get; set; }
    public double? Overflow { get; set; }
}
public enum StatLimitKind { AttributeHardCap, WornAcSoftCap, HasteLimit, ProcLimit }
public sealed class StatLimit
{
    public string Stat { get; set; } = "";
    public StatLimitKind Kind { get; set; }
    public double Value { get; set; }
    public double AboveCapWeight { get; set; }
    public bool Confirmed { get; set; }
    public bool IncludesAAs { get; set; } = true;
    public string Source { get; set; } = "User override";
}
public enum AaModifierKind { Information, FlatStat, HardCapIncrease, SoftCapPercent }
public sealed class GearAaModifier
{
    public int? VerifiedMaxRank { get; set; }
    public Dictionary<int, double> VerifiedRankBonuses { get; set; } = [];
    public string RankRuleSource { get; set; } = "";
    public bool ValueManuallyOverridden { get; set; }
    public DateTime? RankObservedAt { get; set; }
    public string RankEvidence { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
    public string Target { get; set; } = "Self";
    public AaModifierKind Kind { get; set; }
    public string Stat { get; set; } = "";
    public double Value { get; set; }
    public bool Confirmed { get; set; }
    public string Source { get; set; } = "";
}
public sealed class GearStatProfile
{
    public DateTime? GameEvidenceReviewedAt { get; set; }
    public string Name { get; set; } = "Unbuffed";
    public DateTime? ConfirmedAt { get; set; }
    public int Level { get; set; }
    public List<string> Classes { get; set; } = [];
    public string PetSignature { get; set; } = "";
    public string EquipmentFingerprint { get; set; } = "";
    public Dictionary<string, StatObservation> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<StatLimit> Limits { get; set; } = [];
    public List<GearAaModifier> AAs { get; set; } = [];
    public bool AAsIncludedInTotals { get; set; } = true;
    public bool AAListComplete { get; set; }
    public bool BuffsIncludedInTotals { get; set; } = true;
    public string Buffs { get; set; } = "";
    public string Source { get; set; } = "Confirmed in-game totals";
}
public static class GearRatingMigration
{
    public static void Apply(CharacterProfile profile)
    {
        // Historical log estimates are inventory stock, never evidence of a worn slot.
        foreach (var loadout in profile.Loadouts) loadout.Equipment.RemoveAll(e => e.Location.Equals("Observed loot", StringComparison.OrdinalIgnoreCase));
        var legacy = GearScorer.Defaults["Tank"];
        profile.Weights.TryAdd("Legacy Tank", new(legacy));
        profile.Weights.TryAdd("Balanced Tank", new(legacy) { ["DEX"] = .5, ["AGI"] = .5 });
        profile.Weights.TryAdd("Pet DPS", new(GearScorer.Defaults["Pet DPS"]));
        if (profile.PetRatingVersion < 1)
        {
            foreach (var loadout in profile.Loadouts)
                if (loadout.PetPreset is "Melee" or "Pet damage" && profile.Weights.TryGetValue(loadout.PetPreset, out var weights) && weights.Count == GearScorer.Defaults["Pet damage"].Count && weights.All(x => GearScorer.Defaults["Pet damage"].GetValueOrDefault(x.Key) == x.Value)) loadout.PetPreset = "Pet DPS";
            profile.PetRatingVersion = 1;
        }
        if (profile.GearRatingVersion > 0) return;
        if (profile.Weights.TryGetValue("Tank", out var old) && old.Count == legacy.Count && old.All(x => legacy.GetValueOrDefault(x.Key) == x.Value))
        {
            profile.Weights.Remove("Tank");
            foreach (var loadout in profile.Loadouts) { if (loadout.Preset == "Tank") loadout.Preset = "Balanced Tank"; if (loadout.PetPreset == "Tank") loadout.PetPreset = "Balanced Tank"; }
        }
        profile.GearRatingVersion = 1;
    }
}

public static class GuidedPetBuilds
{
    public static List<EquipmentPosition> Positions(PetBuild build, LoadoutProfile owner, Catalog catalog) => PetEquipmentInference.Build(build, owner, catalog).Positions;
    public static List<EquipmentPosition> Positions(PetBuild build, LoadoutProfile owner)
    {
        var result = new List<EquipmentPosition>(); var groups = new Dictionary<string, int>();
        for (int position = 1; position <= PetEquipmentPlanner.Capacity(owner.Classes); position++)
        {
            var item = build.Items.FirstOrDefault(i => i.Position == position); string slot = EquipmentPositions.Normalize(item?.Slot ?? "Storage");
            int ordinal = groups[slot] = groups.GetValueOrDefault(slot) + 1;
            var entry = item?.Entry();
            if (entry != null)
                foreach (var effect in item!.Exaltations.Where(a => a.State == "Planned")) entry.Augments.Add(new() { Name = effect.SourceName + " (Exaltation)", EffectIdentity = effect.EffectId, EffectText = effect.Effect });
            result.Add(new() { Id = "PET:" + position, Slot = slot, Index = ordinal, Item = slot == "Storage" ? null : entry, Knowledge = item == null || slot == "Storage" ? EquipmentKnowledge.Empty : EquipmentKnowledge.Occupied });
        }
        return result;
    }
    public static LoadoutProfile Context(LoadoutProfile owner, PetBuild build)
    {
        var copy = EquipmentPositions.Copy(owner); copy.PetType = build.PetType; copy.PetLevel = build.PetLevel;
        if (build.Preset.Length > 0) copy.PetPreset = build.Preset;
        copy.PetSummonSpell = build.SummonSpell; copy.PetSummonRank = build.SummonRank; copy.PetFocus = build.Focus;
        if (build.StatProfiles.Count > 0) copy.PetStatProfiles = EquipmentPositions.Copy(build.StatProfiles); return copy;
    }
    public static PetBuild Clone(PetBuild build, string name)
    { var result = EquipmentPositions.Copy(build); result.Id = Guid.NewGuid().ToString("N"); result.Name = name; result.Undo.Clear(); result.EditHistory.Clear(); return result; }
    public static List<string> Warnings(PetBuild build, LoadoutProfile owner, Catalog catalog)
    {
        var warnings = build.Items.Select(i => (Item: i, State: PetEquipmentPlanner.Validate(build, i, owner, catalog)))
            .Where(x => x.State != "Eligible" && !x.State.StartsWith("Inactive") && !x.State.StartsWith("Stored")).Select(x => x.Item.Name + ": " + x.State).ToList();
        if (build.PetLevel > owner.Level) warnings.Add("Pet level exceeds the owner's current level (October 13 EQL limit).");
        if(build.Items.Any(i=>!i.EquipmentVerified))warnings.Add("Intended slots: automatic pet selection is not verified. Confirm actual equipment in game; character exports do not identify pet items.");
        return warnings;
    }
    public static List<string> Availability(PetBuild plan,PetBuild confirmed,InventorySnapshot inventory,Catalog catalog)
    {
        var warnings=new List<string>();
        foreach(var group in plan.Items.GroupBy(i=>i.ItemKey))
        {
            if(catalog.FindKey(group.Key) is not {} item)continue;
            int stock=inventory.Evidence(item).Where(e=>!e.IsAugment && !e.IsCollection && !InventoryParser.IsEquipped(e)).Sum(e=>e.Count??0)+confirmed.Items.Count(i=>i.ItemKey==group.Key);
            if(group.Count()>stock)warnings.Add($"{item.Name}: plan needs {group.Count()}, {stock} available from character storage / manual pet inventory; obtain or transfer {group.Count()-stock}. Collection evidence does not prove copies.");
            int highest=inventory.Evidence(item).Where(e=>!e.IsAugment && !e.IsCollection).Select(e=>e.Rank).Concat(confirmed.Items.Where(i=>i.ItemKey==group.Key).Select(i=>i.Rank)).DefaultIfEmpty(-1).Max();
            if(highest>=0 && group.Any(i=>i.Rank>highest))warnings.Add($"{item.Name}: owned source is at most +{highest}; this plan needs further item upgrades. Merge quantities / XP are not inferred.");
        }
        return warnings;
    }
}
