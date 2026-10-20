using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class CombatPolishChecks
{
    internal static void Run(TrackerController c, DpsWindow lab, QuestWindow quest, Action<bool, string> check, string folder)
    {
        var meter = EquipmentPositions.Copy(c.Profile.Meter); var prefs = EquipmentPositions.Copy(c.Profile.CombatLab); var healing = EquipmentPositions.Copy(c.Profile.Healing); string tone = c.Profile.SecondaryText;
        void Flush() { lab.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); lab.UpdateLayout(); }
        IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject => Usability.Tree(root).OfType<T>();
        try
        {
            c.Profile.Meter.Actor = "Combined"; c.Profile.Meter.Ability = c.Profile.Meter.Target = c.Profile.Meter.Kind = ""; c.Profile.Meter.CriticalOnly = false;
            lab.Width = 1170; lab.Height = 760; lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); Find<Expander>(lab).Single(e => e.Header?.ToString() == "Filters").IsExpanded = true; Flush();
            var damage = Find<Button>(lab).Single(b => b.Content?.ToString() == "Damage"); var heal = Find<Button>(lab).Single(b => b.Content?.ToString() == "Healing");
            check(Usability.GetIsPressed(damage) && !Usability.GetIsPressed(heal), "Top-level Damage mode remains visibly selected");
            check(Find<TabControl>(lab).Single().Items.Cast<TabItem>().All(t => t.Tag is CombatLabTab && t.Header?.ToString() != "HEALING"), "Combat subtabs use stable identifiers and no lower Healing tab");
            var scope = Find<ComboBox>(lab).Single(b => b.IsVisible && b.Items.Contains("Session") && b.Items.Contains("Fight")); var date = Find<Button>(lab).Single(b => b.Content?.ToString() == "Date range"); var sort = Find<ComboBox>(lab).Single(b => b.IsVisible && b.Items.Contains("DPS") && b.Items.Contains("Average"));
            double Center(FrameworkElement element) => element.TranslatePoint(new(0, element.ActualHeight / 2), lab).Y;
            check(Math.Abs(Center(scope) - Center(date)) < 1, "Scope input and adjacent action buttons share their input-row center");
            check(Math.Abs(Center(sort) - Center(date)) < 1, "Labeled Sort by input aligns with actions");
            check(Find<TextBlock>((DependencyObject)sort.Parent).Any(t => t.Text == "SORT BY"), "Damage sorting has a readable field title");
            var crit = Find<CheckBox>(lab).Single(b => b.IsVisible && b.Content?.ToString() == "Critical only");
            check(Math.Abs(Center(crit) - Center(Find<TextBox>(lab).Single(b => b.IsVisible && b.Width == 180))) < 1, "Critical only aligns with the input row instead of its labels");
            var filterGrid = (Grid)Find<Expander>(lab).Single(e => e.Header?.ToString() == "Filters").Content;
            check(filterGrid.Children.Cast<UIElement>().Count(e => e.Visibility == Visibility.Collapsed) == 1 && filterGrid.Children.Cast<UIElement>().All(e => e.Visibility != Visibility.Hidden), "Inactive mode filters collapse rather than reserve invisible rows");
            check(lab.HeadingTitle.Inlines.OfType<Run>().First().FontFamily.Source.Contains("Cinzel Decorative") && lab.HeadingTitle.Inlines.OfType<Run>().First().Foreground is LinearGradientBrush, "Combat Lab uses bundled serif EQL lettering and gold gradient");
            check(quest.HeadingTitle.Inlines.OfType<Run>().Any(r => r.Text.Contains("Quest & Gear Tracker")), "Tracker and Combat Lab share compact Catalyst branding");
            lab.ToggleCollapse(); Flush(); check(lab.HeadingTitle.Inlines.OfType<Run>().First().FontSize < 14 && lab.Height == 28, "Branded panel titles shrink with the collapsed title bar"); lab.ToggleCollapse(); Flush(); check(Math.Abs(lab.HeadingTitle.Inlines.OfType<Run>().First().FontSize - 20) < .01, "Restoring a branded panel restores its original typography");
            foreach (var (ability, shape) in new[] { ("bashes", "Shield"), ("kicks", "Kick"), ("flying kicks", "MartialKick"), ("backstabs", "Dagger"), ("punches", "Fist"), ("pierces", "Spear"), ("crushes", "Mace"), ("shoots", "Bow"), ("bites", "Fang"), ("claws", "Claw"), ("slashes", "Sword") }) check(DamageGlyph.ShapeKey("Physical", ability) == shape, "Melee glyph identifies " + ability);
            check(DamageGlyph.ShapeKey("Cold", "bashes") == "Cold" && DamageGlyph.ShapeKey("Healing", "Kick") == "Healing", "Spell and healing glyphs retain channel precedence");
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Metrics); Flush();
            var metrics = Find<DataGrid>(lab).Single(g => g.IsVisible && g.Columns.Any(col => col.SortMemberPath == "MaxHit"));
            check(metrics.Columns[0].ActualWidth <= 161 && metrics.Columns[0].ActualWidth >= metrics.Columns[0].MinWidth, "Metrics defaults to a bounded compact ability-name column");
            check(Find<AbilityInspector>(lab).Single().IsVisible && Find<ResponsiveCombatSplit>(lab).Any(p => p.IsVisible), "Metrics supplies a resizable ability inspector");
            metrics.SelectedItem = metrics.Items.Cast<DamageRow>().FirstOrDefault(r => r.Ability == "Spirit Tap"); Flush();
            check(Find<TextBlock>(Find<AbilityInspector>(lab).Single()).Any(t => t.Text.StartsWith("Conflicting source values")), "Ability inspector retains contradictory sourced spell values without an expected-damage claim");
            lab.Width = 640; lab.Height = 420; Flush();
            var responsive = Find<ResponsiveCombatSplit>(lab).Single(p => p.IsVisible);
            check(responsive.RowDefinitions.Count == 3 && Find<AbilityInspector>(lab).Single().ActualWidth > 400 && Find<AbilityInspector>(lab).Single().ActualHeight > 60, "Narrow Metrics stacks details while keeping table and inspector usable");
            lab.Width = 1170; lab.Height = 760; Flush(); check(responsive.ColumnDefinitions.Count == 3, "Wide Metrics restores side-by-side panes without recreating the inspector");
            lab.ShowView(CombatLabMode.Healing, CombatLabTab.Metrics); Flush(); check(c.HealingVisible && lab.Mode == CombatLabMode.Healing, "Healing metrics keep background healing snapshots active");
            damage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); check(lab.ActiveTab == CombatLabTab.Metrics && lab.Mode == CombatLabMode.Damage, "Switching modes restores the saved Damage subtab");
            heal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); check(lab.ActiveTab == CombatLabTab.Metrics && Usability.GetIsPressed(heal), "Switching back restores Healing navigation and pressed breadcrumb");
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Group); Flush(); check(Find<CombatGroupPanel>(lab).Single().IsVisible && !Find<ComboBox>(lab).Single(b => b.Items.Contains("Unknown")).IsVisible, "Group has its own member view and does not expose the personal actor filter");
            lab.ShowView(CombatLabMode.Healing, CombatLabTab.Group); Flush(); check(Find<CombatGroupPanel>(lab).Single().IsVisible && c.HealingVisible, "Group is available in Healing with independent healing evidence");
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); Flush(); lab.MarkAbility("backstabs"); var timeline = Find<Timeline>(lab).Single(); var hit = timeline.Highlight;
            var peaks = Find<ComboBox>(lab).Single(b => b.Items.Contains("Highest")); peaks.SelectedItem = "Off"; Flush(); check(timeline.PeakMode == ChartPeakMode.Off && timeline.Highlight == hit, "Peak display choices leave the selected ability Peak hit independent"); peaks.SelectedItem = "All"; Flush(); check(c.Profile.CombatLab.Peaks == ChartPeakMode.All, "All local peaks preference saves independently of damage filters"); timeline.PreviewCursor(.5); check(timeline.CursorVisible, "Timeline cursor can display cached samples without native input"); lab.CancelInteraction(); check(!timeline.CursorVisible && !timeline.IsMouseCaptured, "Timeline cursor cleanup releases interaction state");
            var settings = new SettingsWindow(c) { Left = -15000, Top = -15000 }; settings.Show();
            try
            {
                var choice = Find<ComboBox>(settings).Single(b => b.Items.Contains("Parchment Ivory")); var melee = UI.Brush("Melee").Color; var alpha = UI.Brush("InnerSurface").Color.A;
                foreach (string value in Appearance.SecondaryTextChoices) { choice.SelectedItem = value; Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); check(c.Profile.SecondaryText == value && Appearance.SecondaryText == value && UI.Brush("SecondaryText").Color.A == 255 && UI.Brush("Melee").Color == melee && UI.Brush("InnerSurface").Color.A == alpha, value + " updates live without changing damage colors or surface opacity"); }
            }
            finally { settings.Close(); }
            check(ToolTipService.GetInitialShowDelay(date) == 650 && ToolTipService.GetShowDuration(date) == 5000, "Polished controls preserve 650ms / 5s tooltip timing");
            Json.Write(Path.Combine(folder, "combat-polish-checks.json"), new { Passed = true, DefaultSecondaryText = "Bright Frost", Modes = Enum.GetNames<CombatLabMode>(), Tabs = Enum.GetNames<CombatLabTab>(), PeakModes = Enum.GetNames<ChartPeakMode>(), LiveGameAcceptance = "Pending" });
        }
        finally { c.Profile.Meter = meter; c.Profile.Healing = healing; c.Profile.SecondaryText = tone; Appearance.ApplySecondaryText(tone); lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); c.Profile.CombatLab = prefs; c.Save(); }
    }
}
