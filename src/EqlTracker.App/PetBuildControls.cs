using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private bool petContextLoading;
    private long petEditRevision;
    private Action? petSelectionChanged;
    private readonly TextBlock petContextNote = UI.Text("", 11, "Mint");
    private readonly DispatcherTimer petPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly PetContextCatalog petContextCatalog = LoadPetContextCatalog();
    private static PetContextCatalog LoadPetContextCatalog() { try { return PetContextCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "pet-context.json")); } catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return new(); } }
    private static ComboBox EditablePetNumber(IEnumerable<string> choices, string text, double width)
    { var combo = UI.Select(choices, text, width); combo.IsEditable = true; combo.IsTextSearchEnabled = false; combo.Text = text; return combo; }
    private void ConfigurePetContextEditing()
    {
        petPersistTimer.Tick += (_, _) => { petPersistTimer.Stop(); Controller.Save(); };
        Closed += (_, _) => { if (petPersistTimer.IsEnabled) { petPersistTimer.Stop(); Controller.Save(); } };
        foreach (var input in new[] { petLevel, petSummonTier, petFocusName })
        {
            DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox)).AddValueChanged(input, (_, _) => ApplyPetContext());
        }
        petSpell.DisplayMemberPath = "Label"; petSpell.SelectedValuePath = "Id";
        petSpell.SelectionChanged += (_, _) =>
        {
            if (petContextLoading || initializing || petSpell.SelectedItem is not PetSummonOption selected) return;
            petContextLoading = true; petSpell.Text = selected.Label; petContextLoading = false;
            Dispatcher.BeginInvoke(() => ApplyPetContext(), DispatcherPriority.Input);
        };
        petSpell.LostKeyboardFocus += (_, _) => ApplyPetContext();
        petElement.SelectionChanged += (_, _) => ApplyPetContext(true);
        petSpell.ToolTip = "Cached summons for this pet type, ordered by required caster level (50 and below). Spell-required level is distinct from pet level. Auto uses the documented base pet level plus rank. Selection does not imply spell ownership; custom names are retained.";
        petFocusName.ToolTip = "Matching documented focus items. Applicability and disputed benefits appear below; no unsupported numerical bonus is added.";
        petLevel.ToolTip = "Auto calculates the documented base summoned level plus rank, capped at character level. A numeric entry overrides Auto; Unknown is distinct from zero. Custom levels above 50 are hypothetical and allowed only in plans.";
        petSummonTier.ToolTip = "Summon spell upgrade rank. Choose 0–10 or enter a hypothetical rank in a planned build; this does not set item ranks.";
    }
    private void SchedulePetPersistence() { petPersistTimer.Stop(); petPersistTimer.Start(); }
    private void PetEdited(bool debounce = false)
    {
        petEditRevision++; petKey = ""; gearFingerprint = null; libraryKey = exaltKey = "";
        if (petTheory != null) { petTheory.Changed(); UpdateTheorycraftNote(); PetMessage("Unsaved theorycraft changes · Save explicitly · Undo available."); }
        else if (debounce) SchedulePetPersistence(); else Controller.Save();
        if (SelectedTab == TrackerTab.Pet && !initializing) RefreshPet();
    }
    private void LoadPetContext()
    {
        if (SelectedPetInventory is not { } build) return;
        RefreshAutomaticPetLevel(build);
        petContextLoading = true;
        try
        {
            if (!petElement.Items.Contains(build.PetType)) petElement.Items.Add(build.PetType);
            if (petRoleChoice != null) petRoleChoice.SelectedItem = SelectedPetPreset;
            petElement.SelectedItem = build.PetType; petLevel.Text = build.LevelMode == PetLevelMode.Automatic || build.LevelMode == PetLevelMode.Unspecified && !build.PetLevel.HasValue ? "Auto" : build.PetLevel?.ToString() ?? "Unknown"; petSummonTier.Text = build.SummonRank.ToString();
            var spells = petContextCatalog.Summons(build.PetType); petSpell.ItemsSource = spells; var resolved = petContextCatalog.Find(build.PetType, build.SummonSpell, build.SummonClass); petSpell.SelectedItem = resolved; petSpell.Text = resolved?.Label ?? build.SummonSpell;
            var focuses = petContextCatalog.Focuses.Where(f => f.PetType == build.PetType).ToList(); petFocusName.ItemsSource = new[] { "None" }.Concat(focuses.Select(f => f.Name)).ToList(); petFocusName.Text = build.Focus.Length > 0 ? build.Focus : "None";
            if (petFocusField != null) petFocusField.Visibility = focuses.Count > 0 || build.Focus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            petContextNote.Text = (build.LevelMode == PetLevelMode.Automatic ? build.LevelEvidence + "\n" : "") + (PetBuildEditing.Hypothetical(build.PetLevel, build.SummonRank) ? "Hypothetical · " : "") + petContextCatalog.FocusEvidence(build) + (focuses.Count == 0 ? " No focus options documented for this type in the cached coverage." : "");
            petContextNote.ToolTip = "Cached Pet Guide revision 198219 and Magician Focus Items. Applicability evidence is informational; disputed benefits are excluded from ratings.";
        }
        finally { petContextLoading = false; }
    }
    private void ApplyPetContext(bool typeChanged = false)
    {
        if (initializing || petContextLoading || SelectedPetInventory is not { } build) return;
        string rawLevel = petLevel.Text.Trim(); bool automatic = rawLevel == "Auto"; int? level = null;
        if (!automatic && rawLevel.Length > 0 && !rawLevel.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) { if (!int.TryParse(rawLevel, out int number)) { PetMessage("Level must be Unknown or a whole number. Previous value retained.", true); return; } level = number; }
        if (!int.TryParse(petSummonTier.Text, out int rank)) { PetMessage("Spell rank must be a whole number. Previous value retained.", true); return; }
        string type = petElement.SelectedItem?.ToString() ?? "Unknown";
        var selectedSpell = petContextCatalog.Find(type, petSpell.Text);
        string spell = selectedSpell?.Name ?? petSpell.Text.Trim();
        string focus = petFocusName.Text.Trim() == "None" ? "" : petFocusName.Text.Trim();
        var mode = automatic ? PetLevelMode.Automatic : PetLevelMode.Manual;
        var scenario = new PetBuild { PetType = type, SummonSpell = spell, SummonClass = selectedSpell?.CasterClass ?? build.SummonClass, SummonRank = rank, Focus = focus };
        var estimate = petContextCatalog.Estimate(scenario, Controller.Profile.ActiveLoadout.Level);
        if (automatic) level = estimate.Level;
        var previous = (build.PetType, build.PetLevel, build.SummonSpell, build.SummonRank, build.Focus, build.SummonClass, build.LevelMode);
        if (!PetBuildEditing.ApplyContext(build, type, level, spell, rank, focus, PetDestination == PetInventoryDestination.Confirmed, out string error, selectedSpell?.CasterClass ?? build.SummonClass, mode, automatic ? estimate.Explanation : "Manual level override")) { PetMessage(error + " Previous value retained.", true); return; }
        if (previous == (build.PetType, build.PetLevel, build.SummonSpell, build.SummonRank, build.Focus, build.SummonClass, build.LevelMode)) return;
        if (PetDestination == PetInventoryDestination.Confirmed) { var owner = Controller.Profile.ActiveLoadout; owner.PetLevel = build.PetLevel; owner.PetSummonSpell = build.SummonSpell; owner.PetSummonRank = build.SummonRank; owner.PetFocus = build.Focus; }
        petContextNote.Text = (automatic ? estimate.Explanation + "\n" : "") + (PetBuildEditing.Hypothetical(level, rank) ? "Hypothetical · " : "") + petContextCatalog.FocusEvidence(build);
        if (typeChanged) LoadPetContext();
        PetEdited(true); PetMessage(petTheory != null ? "Theorycraft context updated · Save explicitly · Undo available." : "Context updated · saved automatically · Undo available.");
    }
    private void RefreshAutomaticPetLevel(PetBuild build)
    {
        if (build.LevelMode != PetLevelMode.Automatic && !(build.LevelMode == PetLevelMode.Unspecified && !build.PetLevel.HasValue)) return;
        var estimate = petContextCatalog.Estimate(build, Controller.Profile.ActiveLoadout.Level);
        bool changed = build.PetLevel != estimate.Level || build.LevelMode != PetLevelMode.Automatic || build.LevelEvidence != estimate.Explanation;
        build.LevelMode = PetLevelMode.Automatic; build.PetLevel = estimate.Level; build.LevelEvidence = estimate.Explanation;
        if (!changed) return;
        if (PetDestination == PetInventoryDestination.Confirmed) Controller.Profile.ActiveLoadout.PetLevel = build.PetLevel;
        petEditRevision++;
        if (petTheory == null) SchedulePetPersistence(); else { petTheory.Changed(); UpdateTheorycraftNote(); }
    }
    private void SyncPetCurrentItem(PetInventoryItem item)
    {
        if (SelectedPetInventory is not { } build) return;
        var equipped = PetEquipmentInference.Build(build, Controller.Profile.ActiveLoadout, Controller.Catalog);
        var position = equipped.Selections.FirstOrDefault(s => s.EntryId == item.Id)?.PositionId;
        bool was = initializing; initializing = true; petPreferredPosition = position ?? "inventory:" + item.Id; petReplacement.SelectedValue = petPreferredPosition; initializing = was;
    }
    private void SyncPetSelectionFromCurrentItem(string position)
    {
        if (SelectedPetInventory is not { } build || position == "*") return;
        var inferred = PetEquipmentInference.Build(build, Controller.Profile.ActiveLoadout, Controller.Catalog);
        string? entry = position.StartsWith("inventory:") ? position[10..] : inferred.Selections.FirstOrDefault(s => s.PositionId == position)?.EntryId;
        bool was = initializing; initializing = true; petSelectedId = entry ?? ""; petItems.SelectedItem = petItems.Items.OfType<PetItemRow>().FirstOrDefault(r => r.Item.Id == entry); initializing = was;
    }
    private IEnumerable<string> DescribePetActivation(PetBuild before, PetBuild after)
    {
        yield return $"{before.PetType}, level {before.PetLevel?.ToString() ?? "Unknown"} → {after.PetType}, level {after.PetLevel?.ToString() ?? "Unknown"}";
        yield return $"Summon: {after.SummonSpell} +{after.SummonRank} · Focus: {(after.Focus.Length > 0 ? after.Focus : "None")}";
        foreach (int position in before.Items.Select(i => i.Position).Union(after.Items.Select(i => i.Position)).Order())
        { var a = before.Items.FirstOrDefault(i => i.Position == position); var b = after.Items.FirstOrDefault(i => i.Position == position); if (System.Text.Json.JsonSerializer.Serialize(a) != System.Text.Json.JsonSerializer.Serialize(b)) yield return $"{position}: {a?.Name ?? "Empty"} +{a?.Rank ?? 0} → {b?.Name ?? "Empty"} +{b?.Rank ?? 0}"; }
    }
    private DataGridColumn? petRatingColumn;
    private bool petRatingDescending = true;
    private sealed class PetRatingComparer(bool descending) : IComparer
    {
        public int Compare(object? a, object? b) { if (a is not PetSearchRow x || b is not PetSearchRow y) return 0; int value = PetBuildEditing.CompareRatings(x.UnbuffedDelta, x.Comparison?.Evaluation?.Status == "Conditional", y.UnbuffedDelta, y.Comparison?.Evaluation?.Status == "Conditional", descending); return value != 0 ? value : StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name); }
    }
    private void ConfigurePetRatingSort(DataGridColumn column)
    {
        petRatingColumn = column; column.SortDirection = ListSortDirection.Descending;
        petResults.Sorting += (_, e) => { if (e.Column != column) { if (CollectionViewSource.GetDefaultView(petResults.ItemsSource) is ListCollectionView old) old.CustomSort = null; return; } e.Handled = true; petRatingDescending = column.SortDirection != ListSortDirection.Descending; foreach (var c in petResults.Columns) c.SortDirection = null; column.SortDirection = petRatingDescending ? ListSortDirection.Descending : ListSortDirection.Ascending; ApplyPetSort(); };
    }
    private void ApplyPetSort()
    { if (petRatingColumn?.SortDirection != null && CollectionViewSource.GetDefaultView(petResults.ItemsSource) is ListCollectionView view) { view.SortDescriptions.Clear(); view.CustomSort = new PetRatingComparer(petRatingDescending); } }
}

internal sealed class PetBuildReviewDialog : FrostDialog
{
    public PetBuildReviewDialog(string name, IEnumerable<string> changes) : base("Use as current build", 540, 400)
    {
        Editor.Children.Add(UI.Stack(new UIElement[] { UI.Text("Use “" + name + "” as Current build?", 16, "Accent", true), UI.Text("Review the inventory and context changes. Equipment inferred from these items remains unverified. Undo restores the previous build.", 12) }.Concat(changes.Select(line => UI.Text(line, 12))).ToArray()));
        Actions.Children.Add(UI.Button("Use as current build", () => DialogResult = true)); Actions.Children.Add(UI.Button("Cancel", Close));
    }
}
