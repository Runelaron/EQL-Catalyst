using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private readonly ComboBox gearBuildChoice = new() { Width = 180, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly CheckBox gearTheoryCheck = new() { Content = "Theorycraft", Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Edit an isolated hypothetical equipment build. Save explicitly; deselect to return to Current." };
    private readonly CheckBox gearUpdateCurrent = new() { Content = "Auto Update" };
    private readonly TextBlock gearFindLabel = UI.Label("Find equipment");
    private readonly TextBlock gearEquipmentTitle = UI.Label("CURRENT EQUIPMENT");
    private readonly TextBlock gearTheoryNote = UI.Text("", 11, "Melee", true);
    private readonly DispatcherTimer gearSaveDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private GearTheorycraftWorkspace? gearTheory;
    private GearDraft? gearRemovedBuild;
    private Button? gearSaveTheory, gearUseCurrent;
    private Grid? gearInnerSplit, gearOuterSplit;
    private FrameworkElement? gearUseCurrentField;
    private bool gearBuildLoading;
    private long gearWorkspaceRevision;
    private string gearWorkspaceId = "current", gearOwnerName = "";
    private GearDraft? SelectedGearBuild => gearTheory?.Working ?? Controller.Profile.ActiveLoadout.GearBuilds.FirstOrDefault(b => b.Id == gearWorkspaceId);
    private void InitializeGearBuilds()
    {
        gearOwnerName = Controller.Profile.SelectedLoadout; GearBuildEditing.Migrate(Controller.Profile.ActiveLoadout);
        gearSaveDelay.Tick += (_, _) => { gearSaveDelay.Stop(); Controller.Save(); };
        Closed += (_, _) => { if (gearSaveDelay.IsEnabled) { gearSaveDelay.Stop(); if (!App.Exiting) Controller.Save(); } };
        gearBuildChoice.SelectionChanged += (_, _) => {
            if (initializing || gearBuildLoading || gearBuildChoice.SelectedItem is not GearBuildSelection choice || choice.Id == gearWorkspaceId) return;
            if (!ConfirmGearWorkspaceExit()) { RefreshGearBuildChoices(gearWorkspaceId); return; }
            var selected = Controller.Profile.ActiveLoadout.GearBuilds.FirstOrDefault(b => b.Id == choice.Id);
            if (selected?.IsTheorycraft == true) gearTheory = new(selected, true);
            RefreshGearBuildChoices(choice.Id); GearEdited();
        };
        gearTheoryCheck.Click += (_, _) => {
            if (gearBuildLoading) return;
            if (gearTheoryCheck.IsChecked == true) {
                var source = SelectedGearBuild ?? new GearDraft { Name = "Current build", Positions = EquipmentPositions.For(Controller.Profile.ActiveLoadout, false) };
                gearTheory = new(source); RefreshGearBuildChoices(gearTheory.Working.Id); GearEdited();
            } else if (ConfirmGearWorkspaceExit()) { RefreshGearBuildChoices("current"); GearEdited(); }
            else UpdateGearBuildControls();
        };
        string id = Controller.Profile.ActiveLoadout.SelectedGearBuildId;
        if (Controller.Profile.ActiveLoadout.GearBuilds.FirstOrDefault(b => b.Id == id)?.IsTheorycraft == true) id = "current";
        RefreshGearBuildChoices(id);
    }
    private void RefreshGearBuildChoices(string id)
    {
        gearBuildLoading = true; var owner = Controller.Profile.ActiveLoadout;
        var rows = new List<GearBuildSelection> { new("current", "Current build") };
        rows.AddRange(owner.GearBuilds.Select(b => new GearBuildSelection(b.Id, b.Name + (b.IsTheorycraft ? " · Theorycraft" : ""), b.IsTheorycraft)));
        if (gearTheory != null && rows.All(r => r.Id != gearTheory.Working.Id)) rows.Add(new(gearTheory.Working.Id, gearTheory.Working.Name + " · unsaved", true));
        if (!rows.Any(r => r.Id == id)) id = "current";
        gearWorkspaceId = id; gearBuildChoice.ItemsSource = rows; gearBuildChoice.SelectedValue = id;
        if (gearTheory == null) owner.SelectedGearBuildId = id;
        gearDestination.SelectedIndex = id == "current" ? 0 : 1;
        gearBuildLoading = false; UpdateGearBuildControls();
    }
    private void UpdateGearBuildControls()
    {
        gearBuildLoading = true; gearTheoryCheck.IsChecked = gearTheory != null; gearBuildLoading = false;
        gearEquipmentTitle.Text = SelectedGearBuild == null ? "CURRENT EQUIPMENT" : "EQUIPMENT · " + SelectedGearBuild.Name;
        gearTheoryNote.Text = gearTheory == null ? "" : "Theorycraft · " + (gearTheory.Dirty ? "unsaved" : "saved") + " · " + gearTheory.Working.Name;
        gearTheoryNote.Visibility = gearTheory == null ? Visibility.Collapsed : Visibility.Visible;
        if (gearSaveTheory != null) { gearSaveTheory.Visibility = gearTheory == null ? Visibility.Collapsed : Visibility.Visible; gearSaveTheory.IsEnabled = gearTheory?.Dirty == true; }
        if (gearUseCurrentField != null) gearUseCurrentField.Visibility = SelectedGearBuild != null && gearTheory==null ? Visibility.Visible : Visibility.Collapsed;
        if (gearUseCurrent != null) { gearUseCurrent.IsEnabled = SelectedGearBuild != null && gearTheory == null; gearUseCurrent.ToolTip = gearTheory != null ? "Theorycraft builds cannot replace Current." : "Use this ordinary build as Current, with Undo and no ownership changes."; }
    }
    private void GearEdited()
    {
        gearWorkspaceRevision++; gearFingerprint = null; gearPositionsKey = ""; gearSelectedKey = ""; gearPreviewCancel?.Cancel();
        if (gearTheory == null) { gearSaveDelay.Stop(); gearSaveDelay.Start(); }
        UpdateGearBuildControls(); if (!initializing) RefreshGear();
    }
    private string? GearBuildName(string title, string value)
    {
        var entry = new EntryDialog(title, [("Build name", value)]) { Owner = this };
        if (entry.ShowDialog() != true) return null;
        if (string.IsNullOrWhiteSpace(entry.Values[0])) { gearSearchStatus.Text = "Enter a build name."; return null; }
        return entry.Values[0].Trim();
    }
    private void NewGearBuild(bool empty)
    {
        if (!ConfirmGearWorkspaceExit()) return;
        string? name = GearBuildName(empty ? "New empty build" : "New build from current", empty ? "New build" : "Current · copy"); if (name == null) return;
        GearDraft created; lock (Controller.Sync) created = GearBuildEditing.Create(Controller.Profile.ActiveLoadout, name, empty); RefreshGearBuildChoices(created.Id); GearEdited();
    }
    private void DuplicateGearBuild()
    {
        var source = EquipmentPositions.Copy(SelectedGearBuild ?? new GearDraft { Name = "Current", Positions = EquipmentPositions.For(Controller.Profile.ActiveLoadout, false) });
        if (!ConfirmGearWorkspaceExit()) return;
        string? name = GearBuildName("Duplicate build", source.Name + " · copy"); if (name == null) return;
        GearDraft copy; lock (Controller.Sync) copy = GearBuildEditing.Create(Controller.Profile.ActiveLoadout, name, false, source);
        if (copy.IsTheorycraft) gearTheory = new(copy, true);
        RefreshGearBuildChoices(copy.Id); GearEdited();
    }
    private void RenameGearBuild()
    {
        if (SelectedGearBuild is not { } build) { gearSearchStatus.Text = "Current keeps its name. Create a named build to organize alternatives."; return; }
        string? name = GearBuildName("Rename build", build.Name); if (name == null) return; lock (Controller.Sync) { GearBuildEditing.Remember(Controller.Profile.ActiveLoadout, build); build.Name = name; } RefreshGearBuildChoices(build.Id); GearEdited();
    }
    private void RemoveGearBuild()
    {
        var owner = Controller.Profile.ActiveLoadout; string id = gearWorkspaceId;
        if (id == "current") { gearSearchStatus.Text = "Current cannot be removed."; return; }
        if (!ConfirmGearWorkspaceExit()) return;
        var saved = owner.GearBuilds.FirstOrDefault(b => b.Id == id); if (saved == null) { RefreshGearBuildChoices("current"); GearEdited(); return; }
        lock (Controller.Sync) { gearRemovedBuild = saved; owner.GearBuilds.Remove(saved); } RefreshGearBuildChoices("current"); GearEdited(); gearSearchStatus.Text = "Build removed · Undo restores it.";
    }
    internal bool ConfirmGearWorkspaceExit()
    {
        if (gearTheory == null) return true;
        if (gearTheory.Dirty) {
            var dialog = new TheorycraftExitDialog { Owner = this }; dialog.ShowDialog();
            if (dialog.Choice == TheorycraftExitChoice.Cancel || dialog.Choice == TheorycraftExitChoice.Save && !SaveGearTheorycraft()) return false;
        }
        gearTheory = null; RefreshGearBuildChoices("current"); gearViewCancel?.Cancel(); gearPreviewCancel?.Cancel(); gearFingerprint = null; return true;
    }
    private bool SaveGearTheorycraft()
    {
        if (gearTheory == null) return false; string name = gearTheory.Working.Name;
        if (gearTheory.SavedId.Length == 0) { var chosen = GearBuildName("Save theorycraft build", name); if (chosen == null) return false; name = chosen; }
        var saved = gearTheory.Save(name); var builds = Controller.Profile.ActiveLoadout.GearBuilds; int index = builds.FindIndex(b => b.Id == saved.Id); lock (Controller.Sync) { if (index < 0) builds.Add(saved); else builds[index] = saved; }
        Controller.Save(); RefreshGearBuildChoices(saved.Id); gearSearchStatus.Text = "Theorycraft build saved · Current unchanged."; return true;
    }
    private void ResetGearPanels()
    {
        if (gearOuterSplit != null) PaneLayout.Cancel(gearOuterSplit);
        Controller.Profile.PanelSplits.Remove("gear-workspace"); Controller.Profile.PanelSplits.Remove("gear-candidates");
        if (gearOuterSplit != null) { gearOuterSplit.ColumnDefinitions[0].Width = new(.33, GridUnitType.Star); gearOuterSplit.ColumnDefinitions[2].Width = new(.67, GridUnitType.Star); }
        if (gearInnerSplit != null) { gearInnerSplit.ColumnDefinitions[0].Width = new(.627, GridUnitType.Star); gearInnerSplit.ColumnDefinitions[2].Width = new(.373, GridUnitType.Star); } Controller.Save();
    }
    private static UIElement FloatingInspector(string label, UIElement content)
        => FocusedLayout.FloatingInspector(label,content);
    private void OpenGearComparison()
    {
        if (gearTable.SelectedItem is not GearRow row) return;
        var current = visibleGearPositions.FirstOrDefault(p => p.Id == row.PositionId)?.Item;
        var pair = ItemPairComparison.Create(current == null ? null : Controller.Catalog.FindKey(current.ConfirmedItemKey) ?? Controller.Catalog.Find(current.Name), current, row.Item, GearInspectedRank(row), GearInspectedInstance(row), Controller.Profile.ActiveLoadout, Controller.Catalog, false);
        var window = new FrostWindow(Controller, "Item comparison", "Current item / candidate / differences", 750, 550) { Owner = this }; var comparison = new ComparisonPanel(); comparison.ShowPair(pair); window.Body.Children.Add(UI.Scroll(comparison)); Native.ShowPanel(window);
    }
    private void OpenGearRoles() => new GearRolesDialog(Controller.Profile, preset.SelectedItem?.ToString() ?? "Balanced Tank") { Owner = this }.ShowDialog();
}
internal sealed class GearRolesDialog : FrostDialog
{
    internal GearRolesDialog(CharacterProfile profile, string selected) : base("Roles and rating formulas", 640, 480)
    {
        var content = new StackPanel();
        content.Children.Add(UI.Text("Roles express stat preferences, not predicted DPS, mitigation or proc rates. Both Self contexts use the same weights with their own confirmed totals and limits.", 12));
        foreach (var role in profile.Weights.Where(p => !p.Key.StartsWith("Pet", StringComparison.OrdinalIgnoreCase))) {
            var weights = UI.Stack(UI.Text(string.Join(" + ", role.Value.Where(w => w.Value != 0).Select(w => $"{w.Value:0.##} × Δ{w.Key}")), 12, "Mint"), UI.Text("Whole-loadout before/after; confirmed caps only, strongest worn haste, no duplicate AA/buff bonuses. Effects and procs are separate. The item rating's ⓘ explains its exact arithmetic in both contexts.", 11));
            content.Children.Add(new Expander { Header = role.Key, Content = weights, IsExpanded = role.Key == selected });
        }
        Editor.Children.Add(content); Actions.Children.Add(UI.Button("Close", Close));
    }
}
