using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class SolidDialogChecks
{
    internal static void Run(TrackerController c, QuestWindow q, Action<bool, string> check)
    {
        IEnumerable<T> Find<T>(DependencyObject root) => Usability.Tree(root).OfType<T>();
        void Flush() { q.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); q.UpdateLayout(); }
        var saved = EquipmentPositions.Copy(c.Profile.ActiveLoadout); int index = c.Profile.Loadouts.FindIndex(l => l.Name == c.Profile.SelectedLoadout); string theme = c.Profile.Theme;
        try
        {
            var form = new EntryDialog("Short form", [("Name", "Original")], "Review this value before saving.") { Owner = q, Left = -15000, Top = -15000, WindowStartupLocation = WindowStartupLocation.Manual }; form.Show();
            foreach (string choice in Appearance.Themes.Where(t => t != "Auto"))
            { Appearance.Apply(choice); form.UpdateLayout(); check(form.Background is SolidColorBrush { Color.A: 255 } && ((Border)q.Content).Background is SolidColorBrush { Color.A: < 255 }, "Dialogs remain opaque and main Tracker retains theme transparency · " + choice); }
            form.Close(); Appearance.Apply(theme == "Auto" ? c.ThemeName : theme);
            GearBuildFixtures.Apply(c); q.Width = 1220; q.Height = 800; q.SetGearTarget(false); q.ShowView(TrackerTab.Gear, "Red Dragonscale Armor"); q.SelectGearPosition("CHEST:1"); Flush();
            var plan = Find<Button>(q).Single(b => b.IsVisible && b.Content?.ToString() == "Add to build");
            string tip = plan.ToolTip is ToolTip { Content: TextBlock tipText } ? tipText.Text : plan.ToolTip?.ToString() ?? "";
            check(!FocusedLayout.GetIsPrimary(plan) && tip.Contains("planned loadout") && tip.Contains("Undo"), "Add to build uses normal styling and explains its saved, reversible result");
            check(ToolTipService.GetInitialShowDelay(plan) == 650 && ToolTipService.GetShowDuration(plan) == 5000, "Gear action tooltip retains 650 ms / five-second policy");
            q.ShowView(TrackerTab.Pet, "Bloodmoon"); q.SelectPetDestination(PetInventoryDestination.Planned); Flush();
            var destination = Find<ComboBox>(q).Single(combo => combo.Items.OfType<PetBuildSelection>().Any(c => c.Id == "current")); destination.ApplyTemplate();
            var popup = (System.Windows.Controls.Primitives.Popup)destination.Template.FindName("PART_Popup", destination);
            check(popup.Child is Border { Background: SolidColorBrush { Color.A: 255 } }, "Dropdown popup surfaces are opaque independently of compact overlay opacity");
            var catalog = Find<DataGrid>(q).Single(g => g.Columns.Any(col => col.Header?.ToString() == "Slots")); catalog.SelectedIndex = 0;
            int before = c.Profile.ActiveLoadout.PetBuilds[0].Items.Count; q.AddPetCandidateForCheck("bloodmoon", 1); Flush();
            var build = c.Profile.ActiveLoadout.PetBuilds[0]; var added = build.Items.Last();
            check(build.Items.Count == before + 1 && added.Position == 3 && added.Slot == "Storage" && !added.EquipmentVerified && !Application.Current.Windows.OfType<PetItemDialog>().Any(w => w.IsVisible), "Occupied quick addition selects next empty position with no dialog or equipped claim");
            var inventory = Find<DataGrid>(q).Single(g => g.Columns.Any(col => col.Header?.ToString() == "+Rank"));
            var rank = inventory.Columns.Single(col => col.Header?.ToString() == "+Rank");
            check(!inventory.IsReadOnly && !rank.IsReadOnly && rank.Visibility == Visibility.Visible && inventory.Columns.Where(col => col != rank).All(col => col.IsReadOnly), "Only the always-visible +Rank inventory column is editable");
            inventory.SelectedItem = inventory.Items.Cast<object>().Single(r => (r.GetType().GetProperty("Item")?.GetValue(r) as PetInventoryItem)?.Id == added.Id);
            inventory.CurrentCell = new DataGridCellInfo(inventory.SelectedItem, rank); inventory.ScrollIntoView(inventory.SelectedItem, rank); inventory.BeginEdit(); inventory.UpdateLayout();
            var edit = Find<TextBox>(inventory).First(t => t.IsVisible); edit.Text = "5"; inventory.CommitEdit(DataGridEditingUnit.Cell, true); Flush();
            check(build.Items.Single(i => i.Id == added.Id).Rank == 5 && !Application.Current.Windows.OfType<PetItemDialog>().Any(w => w.IsVisible), "Real grid +Rank edit commits without an item dialog");
            check(!q.CommitPetRank(added.Id, 99) && build.Items.Single(i => i.Id == added.Id).Rank == 5, "Invalid inline rank preserves the saved value");
            q.ReorderPetForCheck(added.Id, 1); Flush(); check(build.Items.Single(i => i.Id == added.Id).Position == 1 && build.Items.Select(i => i.Position).Distinct().Count() == build.Items.Count, "Inventory reorder retains identity, effects and unique positions");
            var existing = build.Items.Single(i => i.Id == added.Id); var editor = new PetItemDialog(c, c.Catalog.FindKey(existing.ItemKey)!, build, PetInventoryDestination.Planned, existing) { Owner = q, Left = -15000, Top = -15000, WindowStartupLocation = WindowStartupLocation.Manual }; editor.Show(); editor.UpdateLayout();
            check(editor is FrostDialog && editor.Background is SolidColorBrush { Color.A: 255 } && !Find<Expander>(editor).Single(e => e.Header?.ToString() == "Exaltations").IsExpanded, "Explicit pet editor is solid and initially collapses Exaltations");
            var save = Find<Button>(editor).Single(b => b.Content?.ToString() == "Save planned item");
            check(!FocusedLayout.GetIsPrimary(save) && save.TranslatePoint(new(), editor).Y + save.ActualHeight <= editor.ActualHeight, "Editor footer remains visible with a normal save action");
            Find<TextBox>(editor).First(t => t != editor.PositionInput).Text = "19"; editor.Close(); check(existing.Rank == 5, "Closing an editor discards its working-copy changes");
            q.SelectPetDestination(PetInventoryDestination.Confirmed); Flush(); var add = Find<Button>(q).Single(b => b.IsVisible && b.Content?.ToString() == "Add manual item"); check(FocusedLayout.GetIsPrimary(add), "Manual inventory addition retains explicit gold emphasis");
            q.ShowView(TrackerTab.Exaltations); Flush();
            var effects = Find<DataGrid>(q).Single(g => g.Columns.Any(col => col.Header?.ToString() == "Unlock Rank"));
            var split = (Grid)effects.Parent; TableLayout.Reset(effects); Flush();
            var textColumn = effects.Columns[0]; var fixedColumn = effects.Columns[1]; TableLayout.Prefer(effects, textColumn, 360); TableLayout.Prefer(effects, fixedColumn, 80); double initial = textColumn.ActualWidth;
            split.ColumnDefinitions[0].Width = new(.42, GridUnitType.Star); split.ColumnDefinitions[2].Width = new(.58, GridUnitType.Star); Flush();
            check(Math.Abs(textColumn.ActualWidth - initial) > 1 && Math.Abs(fixedColumn.ActualWidth - 80) < 1, "Pane resize reflows text despite saved widths while preserving compact fixed columns");
            foreach (double width in new[] { 640d, 940, 1220 }) { q.Width = width; q.Height = width == 640 ? 420 : 800; Flush(); check(effects.Columns.All(col => col.Visibility != Visibility.Visible || col.ActualWidth >= col.MinWidth - 1), "Resized Exaltations preserves readable header minima · " + width); }
        }
        finally { c.Profile.Loadouts[index] = saved; c.Save(); Appearance.Apply(theme == "Auto" ? c.ThemeName : theme); q.Width = 1220; q.Height = 800; q.ShowView(TrackerTab.Items); }
    }
}
