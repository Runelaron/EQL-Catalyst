using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private PetTheorycraftWorkspace? petTheory;
    private bool petTheoryChanging;
    private string petWorkspaceSelection = "current";
    private readonly CheckBox petTheoryCheck = new() { Content = "Theorycraft", IsChecked = false, VerticalAlignment = VerticalAlignment.Center, Margin = new(7, 0, 0, 4), ToolTip = "Provides access to items pets currently cannot use, for future patch notes or theorycrafting. Deselect to return to Current build." };
    private readonly TextBlock petTheoryNote = UI.Text("", 11, "Melee", true);
    private Button? petTheorySave;
    private void SetTheoryCheck(bool value) { petTheoryChanging = true; petTheoryCheck.IsChecked = value; petTheoryChanging = false; }
    private void TogglePetTheorycraft()
    {
        if (initializing || petTheoryChanging) return;
        if (petTheoryCheck.IsChecked == true)
        {
            if (petTheory == null && SelectedPetInventory is { } source)
            {
                petPersistTimer.Stop(); Controller.Save();
                petTheory = new(source); petWorkspaceSelection = petTheory.Working.Id;
                petChanges.IsExpanded = false;
                RefreshBuildChoices(petWorkspaceSelection);
            }
        }
        else if (ConfirmPetWorkspaceExit()) RefreshBuildChoices("current");
        else SetTheoryCheck(true);
        UpdateTheorycraftNote();
    }
    private bool ChangePetWorkspace(PetBuildSelection choice)
    {
        if (petTheory?.Working.Id == choice.Id) return true;
        if (petTheory != null && !ConfirmPetWorkspaceExit())
        {
            bool previous = initializing; initializing = true; petBuild.SelectedValue = petWorkspaceSelection; initializing = previous; return false;
        }
        bool was = initializing; initializing = true; petBuild.SelectedValue = choice.Id; initializing = was;
        var target = Controller.Profile.ActiveLoadout.PetBuilds.FirstOrDefault(b => b.Id == choice.Id);
        if (target?.IsTheorycraft == true) { petTheory = new(target); petChanges.IsExpanded = false; }
        SetTheoryCheck(petTheory != null); petWorkspaceSelection = choice.Id; UpdateTheorycraftNote(); return true;
    }
    internal bool ConfirmPetWorkspaceExit()
    {
        if (petTheory == null) return true;
        if (petTheory.Dirty)
        {
            var dialog = new TheorycraftExitDialog { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice == TheorycraftExitChoice.Cancel) return false;
            if (dialog.Choice == TheorycraftExitChoice.Save && !SavePetTheorycraft()) return false;
        }
        petTheory = null; petWorkspaceSelection = "current"; SetTheoryCheck(false); UpdateTheorycraftNote();
        bool previous = initializing; initializing = true; RefreshBuildChoices("current"); initializing = previous;
        if (!initializing) petSelectionChanged?.Invoke();
        petViewCancel?.Cancel(); petPreviewCancel?.Cancel(); petKey = ""; return true;
    }
    private bool SavePetTheorycraft()
    {
        if (petTheory == null) return false;
        string name = petTheory.Working.Name;
        if (petTheory.SavedId.Length == 0)
        {
            var form = new EntryDialog("Save theorycraft build", [("Build name", name)], "Saved as Theorycraft. It stays separate from Current and is never activated as verified equipment.") { Owner = this };
            if (form.ShowDialog() != true) return false;
            if (string.IsNullOrWhiteSpace(form.Values[0])) { PetMessage("Enter a name to save this Theorycraft build. The working copy is still unsaved.", true); return false; }
            name = form.Values[0];
        }
        petTheory.Working.TheorycraftRulesVersion = PetItemRules.Version;
        petTheory.Working.TheorycraftAssumptions = PetEquipmentInference.Build(petTheory.Working, Controller.Profile.ActiveLoadout, Controller.Catalog).Totals.Notes.Where(n => n.StartsWith("Hypothetical assumption")).ToList();
        var saved = petTheory.Save(name); var plans = Controller.Profile.ActiveLoadout.PetBuilds;
        int index = plans.FindIndex(b => b.Id == saved.Id); if (index < 0) plans.Add(saved); else plans[index] = saved;
        Controller.Save(); RefreshBuildChoices(saved.Id); UpdateTheorycraftNote(); PetMessage("Theorycraft build saved · Current unchanged."); return true;
    }
    private void UpdateTheorycraftNote()
    {
        petTheoryNote.Text = petTheory == null ? "" : "Theorycraft · " + (petTheory.Dirty ? "unsaved" : "saved") + " · " + petTheory.Working.Name;
        petTheoryNote.Visibility = petTheory == null ? Visibility.Collapsed : Visibility.Visible;
        if (petTheorySave != null) { petTheorySave.Visibility = petTheory == null ? Visibility.Collapsed : Visibility.Visible; petTheorySave.IsEnabled = petTheory?.Dirty == true; }
    }
}

internal enum TheorycraftExitChoice { Cancel, Save, Discard }
internal sealed class TheorycraftExitDialog : FrostDialog
{
    internal TheorycraftExitChoice Choice { get; private set; }
    internal TheorycraftExitDialog() : base("Unsaved theorycraft build", 480, 275)
    {
        Editor.Children.Add(UI.Text("Save this Theorycraft build before returning to Current? Discard removes only the unsaved working copy. Cancel keeps editing.", 13));
        Actions.Children.Add(UI.Button("Save theorycraft…", () => { Choice = TheorycraftExitChoice.Save; DialogResult = true; }));
        Actions.Children.Add(UI.Button("Discard", () => { Choice = TheorycraftExitChoice.Discard; DialogResult = true; }));
        Actions.Children.Add(UI.Button("Cancel", Close));
    }
}
