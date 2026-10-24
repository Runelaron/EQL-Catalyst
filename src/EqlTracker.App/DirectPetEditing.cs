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
    { petFeedback.Text = message; petFeedback.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible; petFeedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "Violet" : "Mint"); petFeedback.ToolTip = message; }
    private void CommitPetInventory()
    {
        // Membership/order is a report, not evidence of the game's automatic equipment choice.
        PetEdited();
    }
    private void QuickAddPet(ItemDefinition item, InventoryEntry? instance, int? position)
    {
        if (SelectedPetInventory is not { } build) return;
        int target=position??PetInventoryEditing.NextPosition(build);
        if (PetPositionEditing.Replace(build,item,instance,target,PetEquipmentPlanner.Capacity(Controller.Profile.ActiveLoadout.Classes),out string error,Controller.Profile.ActiveLoadout,Controller.Catalog))
        { petSelectedId = build.Items.First(i=>i.Position==target).Id; CommitPetInventory(); PetMessage($"Position {target} updated in draft; Exaltations retained."); }
        else PetMessage(error, true);
    }
    private void PlanPetReplacement(GearRow row, int? requestedPosition)
    {
        QuickAddPet(row.Item,row.Instance,requestedPosition);
    }

    internal bool CommitPetRank(string id, int rank)
    {
        if (SelectedPetInventory is not { } build) return false;
        if (!PetInventoryEditing.SetRank(build, id, rank, Controller.Catalog, out string error)) { PetMessage(error, true); return false; }
        petSelectedId = id;
        // Flush after the grid has completed its cell edit, never replace ItemsSource mid-edit.
        Dispatcher.BeginInvoke(() => PetEdited());
        PetMessage(petTheory != null ? $"+Rank {rank} changed · Save theorycraft explicitly · Undo available." : $"Rank {rank} changed in draft · Save to commit."); return true;
    }
    internal void AddPetCandidateForCheck(string key, int? position = null)
    { if (Controller.Catalog.FindKey(key) is { } item) QuickAddPet(item, null, position); }
    internal void ReorderPetForCheck(string id, int position)
    { if (SelectedPetInventory is { } build && PetInventoryEditing.Reorder(build, id, position, out string error)) { petSelectedId = id; CommitPetInventory(); } }
}
