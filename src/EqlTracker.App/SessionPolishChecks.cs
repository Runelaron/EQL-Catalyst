using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class SessionPolishChecks
{
    internal static void Run(TrackerController c, DpsWindow lab, Action<bool, string> check, string folder)
    {
        var saved = EquipmentPositions.Copy(c.Profile.Meter); var prefs = EquipmentPositions.Copy(c.Profile.CombatLab);
        IEnumerable<T> Find<T>() where T : DependencyObject => Usability.Tree(lab).OfType<T>();
        void Flush() { lab.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); lab.UpdateLayout(); }
        try
        {
            c.Profile.Meter.Scope = "Session"; lab.Width = 1170; lab.Height = 760; lab.ShowView(CombatLabMode.Damage, CombatLabTab.History); Flush();
            var session = Find<ComboBox>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b)=="Session");
            check(session.IsVisible&&!Find<ComboBox>().Any(b=>b.Items.Contains("Fight")), "Session is always visible without obsolete time-scope choices");
            var table = Find<DataGrid>().Single(g => g.Columns.Any(c => c.SortMemberPath == "Started"));
            check(table.IsVisible && table.Columns.All(c => c.CanUserSort) && table.Items.OfType<EncounterHistoryRow>().Any(), "Encounter history is populated and sortable by readable headers");
            check(Find<ScrollViewer>().Any(s => s.Content is StackPanel panel && Usability.Tree(panel).OfType<Button>().Any(b => b.Content?.ToString() == "Inspect fight")), "History actions have an independent scrolling inspector");
            var inspect = Find<Button>().Single(b => b.Content?.ToString() == "Inspect fight"); var older = Find<Button>().Single(b => b.Content?.ToString() == "Load older");
            check(Math.Abs(inspect.TranslatePoint(new(), lab).Y - older.TranslatePoint(new(), lab).Y) < 1 && Math.Abs(inspect.ActualWidth - older.ActualWidth) < 1, "History actions use two equal-width aligned buttons per row");
            inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); check(c.Profile.Meter.Scope == "History" && session.IsVisible, "Inspect pins a saved encounter within the selected session");
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Metrics); Flush(); check(Find<TextBlock>().Any(t => t.Text.StartsWith("Pinned fight")), "Pinned encounter context stays visible outside Breakdown");
            Find<Button>().Single(b => b.Content?.ToString() == "Back to session").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); check(c.Profile.Meter.Scope == "Session", "Back to session clears the pinned-fight selection");
            check(!Find<Button>().Any(b => b.Content?.ToString() is "Export encounter" or "Export scope"), "Exports are exclusive to Tools in the shared header");
            var tools = Find<Button>().Single(b => b.Content?.ToString() == "Tools ▾"); tools.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            check(tools.ContextMenu?.IsOpen == true && Usability.GetIsPressed(tools), "Tools remains visibly pressed while its menu is open");
            lab.Refresh(); check(tools.ContextMenu?.IsOpen == true && Usability.GetIsPressed(tools), "Routine refresh does not reset the Tools menu");
            tools.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(tools.ContextMenu?.IsOpen == false && !Usability.GetIsPressed(tools), "Tools toggles closed without reopening");
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); Flush(); check(Find<DamageRowsView>().Any(r => r.IsVisible && r.ShowContribution), "Contribution percentage is visible by default in Damage Breakdown");
            lab.ShowView(CombatLabMode.Healing, CombatLabTab.Breakdown); Flush(); check(Find<DamageRowsView>().Any(r => r.IsVisible && r.ShowContribution), "Contribution preference is shared with Healing Breakdown");
            foreach (var size in new[] { (1170d, 760d), (640d, 420d) })
            {
                lab.Width = size.Item1; lab.Height = size.Item2;
                foreach (var mode in Enum.GetValues<CombatLabMode>()) { lab.ShowView(mode, CombatLabTab.History); Flush(); check(table.ActualHeight > 30 && Find<Button>().Single(b => b.Content?.ToString() == "Inspect fight").ActualWidth > 70, $"{mode} history table and paired actions remain usable at {size.Item1}×{size.Item2}"); }
            }
            lab.Width = 1170; lab.Height = 760; lab.ShowView(CombatLabMode.Damage, CombatLabTab.Comparison); Flush();
            var comparison = Find<DataGrid>().Single(g => g.Columns.Any(c => c.SortMemberPath == "CombinedDps")); check(comparison.Columns.Single(c => c.SortMemberPath == "Pet").MinWidth >= 80, "Default Pet column fits Unknown with padding");
            check(Find<ResponsiveCombatSplit>().Any(p => p.IsVisible) && Find<TextBlock>().Any(t => t.Text == "SELECTED DAMAGE SAMPLE"), "Loadout comparison offers an independent details pane");
            check(DamageGlyph.ShapeKey("Physical", "kicks") == "Kick" && DamageGlyph.ShapeKey("Physical", "flying kicks") == "MartialKick", "Armored boot and martial kick have distinct cached geometries");
            Json.Write(Path.Combine(folder, "session-polish-checks.json"), new { Passed = true, LiveGame = "Pending", DirectClassSwitchMessages = "Pending client verification" });
        }
        finally { c.Profile.Meter = saved; c.Profile.CombatLab = prefs; lab.Width = 1170; lab.Height = 760; lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); }
    }
}
