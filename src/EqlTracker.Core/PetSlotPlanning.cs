namespace EqlTracker.Core;

/// <summary>Planner configuration belongs to a position, never establishes game ownership.</summary>
public sealed class PetSlotConfiguration
{
    public int Position { get; set; }
    public string ObservedItemId { get; set; } = "";
    public List<ExaltationAssignment> Effects { get; set; } = [];
}

public static class PetSlotPlanning
{
    public const int Version = 1;
    public static void Migrate(PetBuild build, Catalog? catalog = null)
    {
        if (build.SlotConfigurationVersion >= Version) return;
        PetInventoryEditing.EnsureIdentities(build);
        foreach (var item in build.Items)
            if (item.Exaltations.Count > 0 && !build.SlotConfigurations.Any(s => s.Position == item.Position))
                build.SlotConfigurations.Add(new() { Position = item.Position, ObservedItemId = item.Id, Effects = EquipmentPositions.Copy(item.Exaltations) });
        var hands = catalog == null ? [] : build.Items.Where(i => IsHandItem(i, catalog)).OrderBy(i => i.Position).ToList();
        build.DualWield = build.Items.Any(i => i.Slot.Equals("Secondary", StringComparison.OrdinalIgnoreCase)) || hands.Count > 1 && catalog!.FindKey(hands[0].ItemKey)?.TwoHanded != true;
        build.SlotConfigurationVersion = Version;
    }
    public static PetSlotConfiguration Slot(PetBuild build, int position)
    {
        Migrate(build);
        var slot = build.SlotConfigurations.FirstOrDefault(s => s.Position == position);
        if (slot == null) { slot = new() { Position = position }; build.SlotConfigurations.Add(slot); }
        return slot;
    }
    public static void SetEffects(PetBuild build, int position, IEnumerable<ExaltationAssignment> effects, string itemId)
    {
        var slot = Slot(build, position); slot.Effects = EquipmentPositions.Copy(effects.ToList()); slot.ObservedItemId = itemId;
        Sync(build);
    }
    public static void Sync(PetBuild build)
    {
        Migrate(build);
        foreach (var item in build.Items)
        {
            var slot = build.SlotConfigurations.FirstOrDefault(s => s.Position == item.Position);
            item.Exaltations = slot == null ? [] : EquipmentPositions.Copy(slot.Effects);
            if (slot != null && slot.ObservedItemId != item.Id)
                foreach (var effect in item.Exaltations) { effect.State = "Planned"; effect.DestinationKey = item.ItemKey; effect.DestinationRank = item.Rank; }
        }
    }
    public static void Toggle(PetBuild build, bool enabled, Catalog catalog, int capacity)
    {
        Migrate(build); if (build.DualWield == enabled) return;
        PetBuildEditing.Remember(build, enabled ? "Enable Dual wield" : "Disable Dual wield");
        if (!enabled)
        {
            var item = build.Items.FirstOrDefault(i => i.Position == 2);
            if (item != null && IsHandItem(item, catalog)) { build.DormantSecondary = EquipmentPositions.Copy(item); build.Items.Remove(item); }
            var effects = build.SlotConfigurations.FirstOrDefault(s => s.Position == 2);
            if (effects != null) { build.DormantSecondaryConfiguration = EquipmentPositions.Copy(effects); build.SlotConfigurations.Remove(effects); }
        }
        build.DualWield = enabled;
        if (enabled) RestoreSecondary(build, capacity);
        Sync(build);
    }
    private static void RestoreSecondary(PetBuild build, int capacity)
    {
        if (!build.DualWield || capacity < 2 || build.Items.Any(i => i.Position == 2)) return;
        if (build.DormantSecondary != null)
        {
            if (!build.Items.Any(i => i.Id == build.DormantSecondary.Id)) { var item = EquipmentPositions.Copy(build.DormantSecondary); item.Position = 2; build.Items.Add(item); }
            build.DormantSecondary = null;
        }
        if (build.DormantSecondaryConfiguration != null && !build.SlotConfigurations.Any(s => s.Position == 2 && s.Effects.Count > 0))
        { build.SlotConfigurations.RemoveAll(s => s.Position == 2); build.SlotConfigurations.Add(build.DormantSecondaryConfiguration); build.DormantSecondaryConfiguration = null; }
    }
    public static bool IsHandItem(PetInventoryItem item, Catalog catalog) => (catalog.FindKey(item.ItemKey) ?? catalog.Find(item.Name))?.Slots.Any(s => EquipmentPositions.Normalize(s) is "Primary" or "Secondary") == true;
    public static void CompactArmorAfterRemoval(PetBuild build, Catalog catalog, int capacity)
    {
        Migrate(build);
        var armor = build.Items.Where(i => i.Position <= capacity && !IsHandItem(i, catalog)).OrderBy(i => i.Position).ToList();
        var occupied = build.Items.Except(armor).Select(i => i.Position).ToHashSet();
        var available = Enumerable.Range(build.DualWield ? 3 : 2, Math.Max(0, capacity - (build.DualWield ? 2 : 1))).Where(p => !occupied.Contains(p)).ToList();
        if (armor.Count > available.Count) return;
        for (int i = 0; i < armor.Count; i++) armor[i].Position = available[i];
        RestoreSecondary(build, capacity); Sync(build);
    }
    public static List<string> Warnings(PetBuild build, Catalog catalog, int capacity)
    {
        var notes = new List<string>();
        if (!build.DualWield && build.DormantSecondary != null) notes.Add("Secondary saved inactive: " + build.DormantSecondary.Name + " +" + build.DormantSecondary.Rank + ". Enable Dual wield to restore it.");
        if (build.DualWield && build.Items.Any(i => i.Position == 2 && !IsHandItem(i, catalog))) notes.Add("Too many armor pieces · free one armor position to reserve Secondary. No item has been removed.");
        if (build.Items.Any(i => i.Position == 1 && !IsHandItem(i, catalog))) notes.Add("Primary position contains armor · move it before assigning a weapon.");
        var weapons = build.Items.Where(i => i.Position <= capacity && IsHandItem(i, catalog)).ToList();
        if (weapons.Count > (build.DualWield ? 2 : 1)) notes.Add("Competing hand items · inventory order can change which weapons or shield are equipped.");
        if (weapons.Any(i => catalog.FindKey(i.ItemKey)?.TwoHanded == true) && weapons.Count > 1) notes.Add("Conditional · two-handed and other hand items compete. A sum of weapon ratios does not establish the selected set or DPS.");
        foreach (var group in build.Items.Where(i => i.Position <= capacity && !IsHandItem(i, catalog)).GroupBy(i => i.ItemKey))
        {
            var item = catalog.FindKey(group.Key); if (item == null) continue;
            int limit = item.Slots.Select(EquipmentPositions.Normalize).Distinct().Sum(s => s is "Ear" or "Wrist" or "Finger" ? 2 : 1);
            if (group.Count() > limit || group.Count() > 1 && item.Display.Flags.Contains("LORE EQUIPPED")) notes.Add(item.Name + ": duplicate armor exceeds its wearable limits.");
        }
        return notes;
    }
    public static List<ExaltationAssignment> ActiveEffects(PetBuild build, PetInventoryItem item, ItemDefinition definition, string intended, LoadoutProfile owner, Catalog catalog, List<string>? notes = null)
    {
        var slot = build.SlotConfigurations.FirstOrDefault(s => s.Position == item.Position);
        var effects = slot?.Effects ?? item.Exaltations;
        var context = new LoadoutProfile { Classes = owner.Classes.ToList(), Level = owner.Level, PetType = build.PetType, PetLevel = build.PetLevel, Race = owner.Race, Deity = owner.Deity }; var result = new List<ExaltationAssignment>();
        foreach (var effect in effects)
        {
            var source = catalog.Find(effect.SourceName);
            var option = source == null ? null : new ExaltationOption(effect.EffectId, source, ExaltationPlanner.EffectType(effect.Effect), effect.Effect, ExaltationPlanner.Unlock(ExaltationPlanner.EffectType(effect.Effect)), false, false);
            string reason = option == null ? "Effect source metadata pending" : ExaltationPlanner.Validate(option, definition, item.Rank, intended, context, true, catalog);
            if (reason == "Compatible" && source?.EffectActivationLevel is { } activation && build.PetLevel is { } level && level < activation) reason = "Requires pet level " + activation;
            if (reason != "Compatible") { notes?.Add("Inactive · " + effect.SourceName + ": " + reason); continue; }
            var copy = EquipmentPositions.Copy(effect);
            if (slot != null && slot.ObservedItemId != item.Id) copy.State = "Planned";
            result.Add(copy);
        }
        return result;
    }
}
