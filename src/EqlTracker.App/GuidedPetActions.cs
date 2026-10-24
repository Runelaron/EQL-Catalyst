using System.Windows;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private void OpenPetComparisonPanel()
    {
        if (petResults.SelectedItem is not PetSearchRow row) { PetMessage("Select a candidate to compare.", true); return; }
        var current = (petItems.SelectedItem as PetItemRow)?.Item;
        var definition = current == null ? null : Controller.Catalog.FindKey(current.ItemKey) ?? Controller.Catalog.Find(current.Name);
        var context = SelectedPetInventory is { } build ? GuidedPetBuilds.Context(Controller.Profile.ActiveLoadout, build) : Controller.Profile.ActiveLoadout;
        var pair = ItemPairComparison.Create(definition, current?.Entry(), row.Item, PetInspectedRank(row), PetInspectedInstance(row), context, Controller.Catalog);
        var panel = new FrostWindow(Controller, "Pet item comparison", "Ranked item differences · separate from upgrade recommendations", 720, 530) { Owner = this };
        panel.Body.Children.Add(UI.Scroll(new PetPairComparisonView(pair,()=>{
            if(petResults.SelectedItem is not PetSearchRow active)return pair;var baseline=(petItems.SelectedItem as PetItemRow)?.Item;var metadata=baseline==null?null:Controller.Catalog.FindKey(baseline.ItemKey);var loadout=SelectedPetInventory is {} selected?GuidedPetBuilds.Context(Controller.Profile.ActiveLoadout,selected):Controller.Profile.ActiveLoadout;
            return ItemPairComparison.Create(metadata,baseline?.Entry(),Controller.Catalog.FindKey(active.Item.Key)??active.Item,PetInspectedRank(active),PetInspectedInstance(active),loadout,Controller.Catalog);
        },()=>petPreview.Revision+"|"+petKey+"|"+(petResults.SelectedItem as PetSearchRow)?.Item.Key))); Native.ShowPanel(panel);
    }
    private void CloneConfirmedPet()
    {
        if (!ConfirmPetWorkspaceExit()) return;
        var source = PetInventoryEditing.Confirmed(Controller.Profile.ActiveLoadout, Controller.Catalog);
        var draft = GuidedPetBuilds.Clone(source, "From current · " + DateTime.Now.ToString("HH:mm"));
        BeginPetDraft(draft,true);PetEdited();OpenPetDetails();
    }
    private void DuplicatePetBuild()
    {
        if (petTheory != null) { SavePetTheorycraft(); return; }
        if (SelectedPetInventory is not { } build) return;
        string key=petDraftKey;if(!ConfirmPetWorkspaceExit())return;var source=SavedPetSource(key)??SavedPetSource("current")!;var copy=GuidedPetBuilds.Clone(source,source.Name+" copy");BeginPetDraft(copy,true);PetEdited();
    }
    private void RenamePetBuild()
    {
        if (PetDestination != PetInventoryDestination.Planned || CurrentBuild is not { } build) { PetMessage("Choose a planned build to rename.",true); return; }
        var form = new EntryDialog("Rename pet build", [("Build name", build.Name)], "Renaming affects only this saved plan.") { Owner = this };
        if (form.ShowDialog() != true || form.Values[0].Length == 0) return; build.Name = form.Values[0]; RefreshBuildChoices(build.Id); PetEdited();
    }
    private void UndoPetBuild()
    {
        if(petDraft?.Dirty!=true)return;bool unsavedNew=petDraft.IsNew;petDraft.Undo();
        if(unsavedNew){petDraft=null;petDraftKey="";RefreshBuildChoices("current");}
        LoadPetContext();PetEdited();PetMessage("Unsaved changes discarded.");
    }
    private void OpenPetStats()
    {
        if (SelectedPetInventory is not { } build) return;
        var owner = Controller.Profile.ActiveLoadout; var context = GuidedPetBuilds.Context(owner, build);
        var confirmed = PetInventoryEditing.Confirmed(owner, Controller.Catalog); var positions = GuidedPetBuilds.Positions(confirmed, owner, Controller.Catalog);
        var dialog = new StatProfileDialog(Controller, context, true, positions, () => PetEdited(), build) { Owner = this }; Native.ShowPanel(dialog);
    }
}
