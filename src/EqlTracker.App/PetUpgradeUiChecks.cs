using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class PetUpgradeUiChecks
{
    internal static void Run(TrackerController c, QuestWindow q, Action<bool, string> check)
    {
        int index = c.Profile.Loadouts.FindIndex(p => p.Name == c.Profile.SelectedLoadout); var saved = EquipmentPositions.Copy(c.Profile.ActiveLoadout);
        void Flush() { q.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); q.UpdateLayout(); }
        try
        {
            var owner = c.Profile.ActiveLoadout; owner.Classes = ["SHD", "BRD", "MAG"]; owner.PetPreset = "Pet DPS";
            owner.ConfirmedPetInventory = new() { PetType = "Earth", Items = new[] { ("Sunderfury", 4), ("Indicolite Breastplate", 5), ("Indicolite Bracer", 7), ("Indicolite Greaves", 7), ("Cloak of Flames", 4), ("Icebelt", 3), ("Indicolite Vambraces", 7) }.Select((x, i) => new PetInventoryItem { Id = "ui-retained-" + i, Name = x.Item1, ItemKey = c.Catalog.Find(x.Item1)!.Key, Position = i + 1, Rank = x.Item2, Slot = "Storage" }).ToList() };
            c.Save(); q.Width = 1220; q.Height = 760; q.ShowView(TrackerTab.Pet, "Indicolite Breastplate"); q.SelectPetDestination(PetInventoryDestination.Confirmed); q.PreviewPetRank(6); Flush();
            var tables = Usability.Tree(q).OfType<DataGrid>().Where(g => g.IsVisible).ToList();
            var candidates = tables.Single(g => g.Columns.Any(col => col.Header?.ToString() == "Upgrade rating"));
            check(!candidates.Columns.Any(col => col.Header?.ToString() is "Buffed Δ" or "Unbuffed Δ"), "Pet candidates expose one Upgrade rating without buff contexts");
            var comparison = Usability.Tree(q).OfType<BuildComparisonView>().Single(v => v.IsVisible);
            check(Usability.Tree(comparison).OfType<TextBlock>().Any(t => t.Text.StartsWith("Upgrade rating")) && !Usability.Tree(comparison).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "Usual buffs"), "Pet comparison reuses the evaluator with a single visible context");
            check(Usability.Tree(q).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("AC ") && t.Text.Contains("DEX ") && t.Text.Contains("Haste ")), "Pet gear totals include ranked AC, DEX and stacked worn haste");
            check(tables.Single(g => g.Columns.Any(col => col.Header?.ToString() == "+Rank")).Columns.Any(col => col.Header?.ToString() == "Procs" && col.Visibility == Visibility.Visible), "Pet inventory exposes proc effects beside editable rank");
            var source = candidates.ItemsSource; Flush(); check(ReferenceEquals(source, candidates.ItemsSource), "Unchanged Pet refresh preserves its row collection and selection");
            q.SetGearTarget(true); q.ShowView(TrackerTab.Gear, "Indicolite Breastplate"); q.SelectGearPosition("CHEST:1"); Flush();
            var gear = Usability.Tree(q).OfType<DataGrid>().Single(g => g.IsVisible && g.Columns.Any(col => col.SortMemberPath == "ItemScore"));
            check(gear.Columns.Single(col => col.SortMemberPath == "BuffedDelta").Visibility == Visibility.Collapsed && gear.Items.OfType<GearRow>().Any(r => r.Comparison?.Evaluation?.Pet == true && r.UnbuffedDelta != null), "Gear pet target uses inferred storage equipment and one rated context");
            foreach (string theme in new[] { "Classic EQ", "Frost Blue", "Midnight", "Warm Amber" }) { Appearance.Apply(theme); check(UI.Brush("InnerSurface").Color.A == 191 && UI.Brush("DetailSurface").Color.A == 191 && UI.Brush("SolidDetailSurface").Color.A == 255 && UI.Brush("Foreground").Color.A == 255, "Only inner cards use 75% alpha, preserving solid dialogs and crisp text · " + theme); }
            var dialog = new StatProfileDialog(c, owner, true, PetEquipmentInference.Build(owner.ConfirmedPetInventory, owner, c.Catalog).Positions, () => { }) { Left = -15000, Top = -15000 }; dialog.Show();
            var tabs = Usability.Tree(dialog).OfType<TabControl>().First(); tabs.SelectedIndex = 2; dialog.UpdateLayout();
            check(!Usability.Tree(dialog).OfType<ComboBox>().Any(b => b.Items.Contains("Usual buffs")), "Pet stat profiles offer one context while retaining legacy data");
            var aaTable = Usability.Tree(dialog).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "Rank evidence"));
            double occupied = aaTable.Columns.Where(col => col.Visibility == Visibility.Visible).Sum(col => col.ActualWidth);
            check(occupied <= aaTable.ActualWidth + 1, "AA default columns fit the actual viewport with spinner minimum widths included");
            var editor = Usability.Tree(dialog).OfType<AaRankEditor>().First(e => e.DataContext is GearAaModifier { Rank: null }); var aa = (GearAaModifier)editor.DataContext;
            Usability.Tree(editor).OfType<Button>().Single(b => b.Content?.ToString() == "+").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(aa.Rank == 1, "AA increment establishes rank one from Unknown through the real control");
            Usability.Tree(editor).OfType<Button>().Single(b => b.Content?.ToString() == "−").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(aa.Rank == 0, "AA decrement reaches explicit zero without becoming Unknown"); dialog.Close();
        }
        finally { c.Profile.Loadouts[index] = saved; Appearance.Apply(c.Profile.Theme == "Auto" ? c.ThemeName : c.Profile.Theme); c.Save(); q.Width = 1220; q.Height = 800; q.ShowView(TrackerTab.Items); }
    }
}
