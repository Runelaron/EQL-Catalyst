namespace EqlTracker.Core;

public enum PetInventoryDestination { Confirmed, Planned }
public static class PetInventoryEditing
{
    public static void EnsureIdentities(PetBuild build)
    {
        foreach (var group in build.Items.GroupBy(i => i.Id).ToList())
            foreach (var item in string.IsNullOrEmpty(group.Key) ? group : group.Skip(1)) item.Id = Guid.NewGuid().ToString("N");
    }
    private static void Remember(PetBuild build)
    { PetBuildEditing.Remember(build); }
    public static bool Add(PetBuild build, ItemDefinition definition, InventoryEntry? instance, int capacity, int? requestedPosition, bool confirmed, out PetInventoryItem? added, out string error)
    {
        added = null; error = ""; EnsureIdentities(build);
        if (instance?.IsCollection == true) instance = null;
        int position = requestedPosition is > 0 && requestedPosition <= capacity && build.Items.All(i => i.Position != requestedPosition) ? requestedPosition.Value : Enumerable.Range(1, capacity).FirstOrDefault(p => build.Items.All(i => i.Position != p));
        if (position == 0) { error = $"Inventory full · {capacity}/{capacity}. Remove an item or use another build."; return false; }
        var item = new PetInventoryItem { Id = Guid.NewGuid().ToString("N"), Position = position, Name = definition.Name, ItemKey = definition.Key, Slot = "Storage", Rank = instance == null ? 0 : Math.Max(instance.Rank, Identity.Rank(instance.Name)), ConfirmedAt = confirmed ? DateTime.Now : null };
        if (instance is { IsCollection: false })
        {
            item.TransferredOutEffects = instance.TransferredOutEffects.ToList();
            item.Exaltations = instance.Augments.Select(a => new ExaltationAssignment { EffectId = a.EffectIdentity, SourceName = Identity.Canonical(a.Name), Effect = a.EffectText.Length > 0 ? a.EffectText : a.Name, State = "Socketed" }).ToList();
        }
        Remember(build); build.Items.Add(item); added = item; return true;
    }
    public static bool Reorder(PetBuild build, string id, int targetPosition, out string error)
    {
        error = ""; EnsureIdentities(build); var moving = build.Items.FirstOrDefault(i => i.Id == id);
        if (moving == null || targetPosition < 1) { error = "Choose an inventory entry and destination position."; return false; }
        if (moving.Position == targetPosition) return true;
        Remember(build); int from = moving.Position;
        foreach (var item in build.Items.Where(i => i.Id != id))
            if (from < targetPosition && item.Position > from && item.Position <= targetPosition) item.Position--;
            else if (from > targetPosition && item.Position >= targetPosition && item.Position < from) item.Position++;
        moving.Position = targetPosition; return true;
    }
    public static bool SetRank(PetBuild build, string id, int rank, Catalog catalog, out string error)
    {
        error = ""; var item = build.Items.FirstOrDefault(i => i.Id == id);
        if (item == null || rank is < 0 or > 20) { error = "Use an item upgrade rank from 0 to 20."; return false; }
        foreach (var effect in item.Exaltations)
        {
            string type = ExaltationPlanner.EffectType(effect.Effect);
            var source = catalog.Find(effect.SourceName);
            if (source != null && effect.EffectId.Length > 0)
                type = source.Effects.Where(e => ExaltationPlanner.EffectId(source.Key, e) == effect.EffectId).Select(ExaltationPlanner.EffectType).FirstOrDefault() ?? type;
            if (type != "Unknown" && rank < ExaltationPlanner.Unlock(type)) { error = $"{effect.SourceName} requires destination +{ExaltationPlanner.Unlock(type)}. Edit the effect first."; return false; }
        }
        if (item.Rank == rank) return true;
        Remember(build); item.Rank = rank; if (item.ConfirmedAt.HasValue) item.ConfirmedAt = DateTime.Now; return true;
    }
    public static PetBuild Confirmed(LoadoutProfile loadout, Catalog catalog)
    {
        var inventory = loadout.ConfirmedPetInventory ??= new() { Name = "Current build", Preset = loadout.PetPreset, PetType = loadout.PetType, PetLevel = loadout.PetLevel, SummonSpell = loadout.PetSummonSpell, SummonRank = loadout.PetSummonRank, Focus = loadout.PetFocus,
            Items = loadout.PetEquipment.Select((entry, index) => new PetInventoryItem { Position = index + 1, Slot = entry.Location, Name = Identity.Canonical(entry.Name), Rank = Math.Max(entry.Rank, Identity.Rank(entry.Name)),
                ItemKey = catalog.Find(entry.Name, entry.GameId == 0 ? null : entry.GameId)?.Key ?? Identity.Key(entry.Name), ConfirmedAt = loadout.PetEquipmentConfirmedAt,
                TransferredOutEffects = entry.TransferredOutEffects.ToList(), Exaltations = entry.Augments.Select(a => new ExaltationAssignment { SourceName = Identity.Canonical(a.Name), Effect = a.EffectText.Length > 0 ? a.EffectText : a.Name, EffectId = a.EffectIdentity, State = "Socketed" }).ToList() }).ToList() };
        EnsureIdentities(inventory); return inventory;
    }
    public static int NextPosition(PetBuild build) { int position = 1; while (build.Items.Any(i => i.Position == position)) position++; return position; }
    public static bool Put(PetBuild build, PetInventoryItem value, int? originalPosition, bool replace, out string error)
    {
        error = "";
        if (value.Position < 1 || value.Rank is < 0 or > 20 || value.ItemKey.Length == 0) { error = "Use a positive position, a known item, and +rank 0–20."; return false; }
        var occupied = build.Items.FirstOrDefault(i => i.Position == value.Position && i.Position != originalPosition);
        if (occupied != null && !replace) { error = $"Position {value.Position} holds {occupied.Name}. Choose another position or explicitly enable replacement."; return false; }
        if (value.Id.Length == 0) value.Id = build.Items.FirstOrDefault(i => i.Position == originalPosition)?.Id ?? Guid.NewGuid().ToString("N");
        build.Items.RemoveAll(i => i.Position == originalPosition || i.Position == value.Position); build.Items.Add(value); return true;
    }
    public static void Confirm(LoadoutProfile loadout, PetBuild inventory, DateTime at)
    {
        inventory = System.Text.Json.JsonSerializer.Deserialize<PetBuild>(System.Text.Json.JsonSerializer.Serialize(inventory))!;
        EnsureIdentities(inventory);
        foreach (var item in inventory.Items) item.ConfirmedAt ??= at;
        loadout.ConfirmedPetInventory = inventory; if (inventory.Preset.Length > 0) loadout.PetPreset = inventory.Preset; loadout.PetLevel = inventory.PetLevel; loadout.PetEquipmentConfirmedAt = at;
        loadout.PetSummonSpell = inventory.SummonSpell; loadout.PetSummonRank = inventory.SummonRank; loadout.PetFocus = inventory.Focus;
        if (inventory.StatProfiles.Count > 0) loadout.PetStatProfiles = EquipmentPositions.Copy(inventory.StatProfiles);
        // Storage-only and capacity-inactive entries remain retained without becoming equipped baselines.
        loadout.PetEquipment = inventory.Items.Where(i => i.Position <= PetEquipmentPlanner.Capacity(loadout.Classes) && !i.Slot.Equals("Storage", StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Position).Select(i => i.Entry()).ToList();
    }
    public static bool Has(PetBuild? inventory, ItemDefinition item) => inventory?.Items.Any(i => i.ItemKey == item.Key || Identity.Key(i.Name) == item.Key) == true;
}
