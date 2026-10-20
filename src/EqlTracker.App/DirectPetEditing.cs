using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private static T? Ancestor<T>(object? source) where T : DependencyObject
    {
        for (var node = source as DependencyObject; node != null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)) if (node is T match) return match;
        return null;
    }
    private DataObject CatalogData(PetSearchRow row)
    { var instance = PetInspectedInstance(row); var data = new DataObject("EQL.CatalogItem", row.Item.Key); data.SetData("EQL.PetCatalog", new PetCatalogDrag(row.Item.Key, instance == null ? null : EquipmentPositions.Copy(instance))); return data; }
    private void PetMessage(string message, bool error = false)
    { petFeedback.Text = message; petFeedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "Violet" : "Mint"); petFeedback.ToolTip = message; }
    private void CommitPetInventory()
    {
        // Membership/order is a report, not evidence of the game's automatic equipment choice.
        if (PetDestination == PetInventoryDestination.Confirmed && SelectedPetInventory is { } inventory)
            Controller.Profile.ActiveLoadout.ConfirmedPetInventory = EquipmentPositions.Copy(inventory);
        PetEdited();
    }
    private void QuickAddPet(ItemDefinition item, InventoryEntry? instance, int? position)
    {
        if (SelectedPetInventory is not { } build) return;
        if (PetInventoryEditing.Add(build, item, instance, PetEquipmentPlanner.Capacity(Controller.Profile.ActiveLoadout.Classes), position, PetDestination == PetInventoryDestination.Confirmed, out var added, out string error))
        { petSelectedId = added!.Id; CommitPetInventory(); PetMessage($"Added {added.Name} to position {added.Position} · Undo available."); }
        else PetMessage(error, true);
    }
    private void PlanPetReplacement(GearRow row, int? requestedPosition)
    {
        if (CurrentBuild is not { } build) return;
        int capacity = PetEquipmentPlanner.Capacity(Controller.Profile.ActiveLoadout.Classes);
        int position = requestedPosition ?? Enumerable.Range(1, capacity).FirstOrDefault(p => build.Items.All(i => i.Position != p));
        if (position < 1 || position > capacity) { PetMessage("Choose a replacement position or free inventory space in this plan.", true); return; }
        var existing = build.Items.FirstOrDefault(i => i.Position == position);
        var value = new PetInventoryItem { Id = Guid.NewGuid().ToString("N"), Position = position, Name = row.Item.Name, ItemKey = row.Item.Key, Rank = row.Rank,
            Slot = existing is { Slot: not "Storage" } ? existing.Slot : EquipmentPositions.Normalize(row.Item.Slots.FirstOrDefault() ?? "Storage") };
        if (row.Instance != null) value.Exaltations = row.Instance.Augments.Select(a => new ExaltationAssignment { SourceName = Identity.Canonical(a.Name), EffectId = a.EffectIdentity, Effect = a.EffectText, State = "Planned" }).ToList();
        PetBuildEditing.Remember(build, "Planned replacement");
        if (!PetInventoryEditing.Put(build, value, existing?.Position, true, out string error)) { PetMessage(error, true); return; }
        petSelectedId = value.Id; PetEdited();
        PetMessage("Planned replacement saved · confirmed equipment unchanged · Undo available.");
    }
    internal bool CommitPetRank(string id, int rank)
    {
        if (SelectedPetInventory is not { } build) return false;
        if (!PetInventoryEditing.SetRank(build, id, rank, Controller.Catalog, out string error)) { PetMessage(error, true); return false; }
        petSelectedId = id;
        // Flush after the grid has completed its cell edit, never replace ItemsSource mid-edit.
        Dispatcher.BeginInvoke(() => { if (PetDestination == PetInventoryDestination.Confirmed && build.Items.FirstOrDefault(i => i.Id == id) is { EquipmentVerified: true }) PetInventoryEditing.Confirm(Controller.Profile.ActiveLoadout, build, DateTime.Now); else if (PetDestination == PetInventoryDestination.Confirmed) Controller.Profile.ActiveLoadout.ConfirmedPetInventory = EquipmentPositions.Copy(build); PetEdited(); });
        PetMessage(petTheory != null ? $"+Rank {rank} changed · Save theorycraft explicitly · Undo available." : $"+Rank {rank} saved · Undo available."); return true;
    }
    internal void AddPetCandidateForCheck(string key, int? position = null)
    { if (Controller.Catalog.FindKey(key) is { } item) QuickAddPet(item, null, position); }
    internal void ReorderPetForCheck(string id, int position)
    { if (SelectedPetInventory is { } build && PetInventoryEditing.Reorder(build, id, position, out string error)) { petSelectedId = id; CommitPetInventory(); } }
}
