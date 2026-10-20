using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class HealingUiChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child; }
    internal static void Run(TrackerController controller, DpsWindow lab, Action<bool, string> check)
    {
        var saved = controller.Profile.Healing; var damageActor = controller.Profile.Meter.Actor;
        try
        {
            controller.Profile.Healing = new(); lab.ShowView(4);
            void Flush() { lab.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); lab.UpdateLayout(); }
            lab.Width = 1170; lab.Height = 740; Tree(lab).OfType<Expander>().First(e => e.Header?.ToString() == "Filters").IsExpanded = true; Flush(); var panel = Tree(lab).OfType<HealingPanel>().Single(); var view = controller.HealingView();
            check(panel.IsVisible && controller.HealingVisible, "Healing tab enables its revisioned background view only while visible");
            check(view.Effective == view.Rows.Sum(r => r.Effective), "Healing rows reconcile with effective HP");
            var rows = Tree(panel).OfType<ListBox>().Single(); var first = rows.Items.Cast<DamagePresentation>().First();
            check(first.Row.Unit == "hps" && first.Row.Channel == "Healing", "Healing rows use HPS labels and mint healing glyphs");
            check(first.Tooltip.Contains("HP restored") && first.Tooltip.Contains("overheal"), "Healing tooltip distinguishes effective HP and known overheal");
            check(VirtualizingPanel.GetScrollUnit(rows) == ScrollUnit.Pixel && VirtualizingPanel.GetIsVirtualizing(rows), "Healing rows retain virtualized pixel scrolling");
            check(Tree(panel).OfType<GridSplitter>().Any(), "Healing spell and party panels have a draggable splitter");
            lab.Width = 640; lab.Height = 420; Flush();
            check(panel.PartyTable.Columns.Take(2).All(c => c.ActualWidth >= c.MinWidth), "Narrow healing party names and HP retain readable minimum widths");
            check(panel.PartyTable.Columns.Skip(2).All(c => c.Visibility == Visibility.Collapsed), "Narrow healing party tables hide secondary columns instead of clipping numbers");
            lab.Width = 1170; lab.Height = 740; Flush();
            panel.PartyTable.SelectedIndex = 0; string? selectedParty = (panel.PartyTable.SelectedItem as HealingPartyRow)?.Name;
            var direction = Tree(lab).OfType<ComboBox>().Single(c => c.Items.Contains("Received")); direction.SelectedItem = "Received"; Flush();
            check((panel.PartyTable.SelectedItem as HealingPartyRow)?.Name == selectedParty, "Healing party selection survives updated totals and ordering");
            check(controller.Profile.Healing.Direction == "Received", "Healing Done/Received switch updates the typed selection");
            check(controller.HealingView().Healers.Any(h => h.Name == "Helpfulcleric"), "Received healing includes named healers without a confirmed roster");
            check(controller.Profile.Meter.Actor == damageActor, "Healing actor filters do not change DPS actor selection");
            var crit = Tree(lab).OfType<CheckBox>().Single(c => c.IsVisible && c.Content?.ToString() == "Critical only"); crit.IsChecked = true; crit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            check(controller.HealingView().Rows.All(r => r.CriticalPercent == 100), "Critical healing filter reconciles the visible breakdown");
            check(CatalystBranding.Name == "EQL Catalyst" && CatalystBranding.WindowIcon.PixelWidth >= 16, "EQL Catalyst name and embedded Windows icon load");
            using var tray = CatalystBranding.CreateTrayIcon(); check(tray.Width >= 16, "Multi-resolution EQL Catalyst icon loads for the tray");
            lab.ShowView(0); Flush(); check(!controller.HealingVisible, "Leaving Healing suspends healing-only snapshot work");
        }
        finally { controller.Profile.Healing = saved; lab.ShowView(0); }
    }
}
