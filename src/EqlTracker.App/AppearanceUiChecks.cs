using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class AppearanceUiChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child; }
    internal static void Run(TrackerController controller, QuestWindow quest, OverlayWindow meter, Action<bool, string> check)
    {
        var p = controller.Profile; string savedTheme = p.Theme, savedStyle = p.MeterStyle; double savedOpacity = p.Dps.Opacity; bool savedHealing = p.ShowLinkedHealing;
        var settings = new SettingsWindow(controller) { Left = -15000, Top = -15000 }; settings.Show();
        void Flush() { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); settings.UpdateLayout(); }
        try
        {
            Flush(); var themes = Tree(settings).OfType<ComboBox>().Single(c => c.Items.Contains("Classic EQ"));
            var brush = UI.Brush("Accent"); themes.SelectedItem = "Classic EQ"; Flush();
            check(p.Theme == "Classic EQ" && ReferenceEquals(brush, UI.Brush("Accent")) && brush.Color == (Color)ColorConverter.ConvertFromString("#EDD28A"), $"Theme changes recolor existing shared brushes immediately (theme={p.Theme}, same={ReferenceEquals(brush, UI.Brush("Accent"))}, color={brush.Color}, frozen={brush.IsFrozen})");
            themes.SelectedItem = "Midnight"; Flush(); check(UI.Brush("Surface").Color.R < 20, "Midnight theme applies instantly without rebuilding view data");
            var opacity = Tree(settings).OfType<Slider>().Single(); opacity.Value = .55; Flush();
            check(Math.Abs(((App)Application.Current).DpsOverlay.Opacity - .55) < .001 && Math.Abs(Tree(settings).OfType<MeterPreview>().Single().Opacity - .55) < .001, "Opacity immediately updates both overlay and actual-component preview");
            var style = Tree(settings).OfType<ComboBox>().Single(c => c.Items.Contains("Luminous")); style.SelectedItem = "Luminous"; Flush();
            check(Tree(settings).OfType<MeterPreview>().Single().Effect != null && p.MeterStyle == "Luminous", "Meter style preview reflects the selected glow immediately");
            check(Tree(settings).OfType<Button>().Any(b => b.Template?.FindName("Face", b) != null), "Settings use raised EQ-inspired button faces");
            check(Tree(settings).OfType<MeterPreview>().Single().TranslatePoint(new(), settings).X > style.TranslatePoint(new(), settings).X, "Live preview appears to the right of the style selector");
            check(themes.Items.Contains("Auto") && themes.Items.Contains("Rune4K_Frost_Clear") && themes.Items.Contains("Warm Amber"), "Themes retain Auto and installed Rune variants");
            var menu = meter.HeadingTitle.ContextMenu!;
            var click = menu.Items.OfType<MenuItem>().Single(i => i.Header is StackPanel);
            check(((StackPanel)click.Header).Children.OfType<TextBlock>().Any(t => t.Text == "(Ctrl+Alt+T to restore)"), "Click-through recovery hint sits on a smaller second line");
            var heal = menu.Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).Single(i => i.Header?.ToString() == "Show healing beside damage"); heal.IsChecked = false; heal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(!p.ShowLinkedHealing, "Damage healing visibility is controlled by the meter options menu");
            quest.ShowView(TrackerTab.Items); quest.SetZoneFilter("All zones"); quest.SetSearch(""); quest.Refresh(); Flush();
            var table = Tree(quest).OfType<DataGrid>().Single(g => g.Columns.Any(c => c.Header?.ToString() == "H/N"));
            int populated = table.Items.Count; quest.SetSearch("no-matching-goal-000000"); quest.Refresh(); Flush();
            check(table.Items.Count == 0 && table.Columns[1].ActualWidth >= table.Columns[1].MinWidth && table.Columns[2].ActualWidth >= table.Columns[2].MinWidth, "Empty table retains measured H/N and Cls columns");
            var questSearch = Tree(quest).OfType<TextBox>().Single(t => t.IsVisible && double.IsNaN(t.Width));
            Json.Write(Path.Combine(controller.DataFolder, "search-alignment.json"), new { Search = questSearch.ActualWidth, Columns = table.Columns.Sum(c => c.ActualWidth), Table = table.ActualWidth, Widths = table.Columns.Select(c => new { c.Header, c.ActualWidth }).ToArray() });
            check(Math.Abs(questSearch.ActualWidth - table.Columns.Sum(c => c.ActualWidth)) < 2 && Math.Abs(questSearch.TranslatePoint(new(), quest).X - table.TranslatePoint(new(), quest).X) < 1, "Empty quest search and table share their left and right pane boundaries");
            quest.SetSearch(""); quest.SetZoneFilter("Permafrost"); quest.Refresh(); Flush();
            check(table.Items.OfType<ObjectiveRow>().Any(r => r.Item == "Heart of Frost"), "Manual offline zone browsing uses cached data");
            check(p.Zone == "Permafrost", "Browsing filters do not overwrite the last logged zone");
            quest.SetZoneFilter("All zones"); quest.SetSearch(""); quest.Refresh(); Flush();
            check(table.Items.Count == populated, "Offline browsing restores the complete table immediately");
            quest.ShowView(TrackerTab.Progress, "Magician Epic Quest"); Flush();
            var status = Tree(quest).OfType<TextBlock>().Single(t => t.Text == "STEP STATUS"); var obtain = Tree(quest).OfType<TextBlock>().Single(t => t.Text.Equals("How to obtain", StringComparison.OrdinalIgnoreCase));
            check(status.TranslatePoint(new(), quest).X < obtain.TranslatePoint(new(), quest).X, "Step Status stays in the left pane beside full-height acquisition details");
            var native = new NativeControlsWindow(controller) { Left = -15000, Top = -15000 }; native.Show(); Flush(); native.UpdateLayout();
            check(Tree(native.Body).OfType<Button>().Where(b => b.Content is string).All(b => b.ToolTip != null && ToolTipService.GetShowOnDisabled(b)), "Every native-control button includes disabled-aware tooltips · missing: " + string.Join(',', Tree(native.Body).OfType<Button>().Where(b => b.Content is string && (b.ToolTip == null || !ToolTipService.GetShowOnDisabled(b))).Select(b => b.Content)));
            check(Tree(native).OfType<TextBlock>().Any(t => t.Text.StartsWith("Automatic sync is optional") && ReferenceEquals(t.Foreground, UI.Brush("ReadableNote"))), "Native setup notes use readable theme-aware text without an extra background"); native.Close();
            var objectives = ((App)Application.Current).ObjectivesOverlay; double ow = objectives.Width, oh = objectives.Height; string originalZone = p.Zone;
            objectives.Width = 128; objectives.Height = 90; p.Zone = "Unknown test zone"; objectives.Refresh(); objectives.UpdateLayout(); Flush();
            var footer = Tree(objectives).OfType<TextBlock>().Single(t => t.Text == "0 Obj");
            check(footer.IsVisible && footer.ActualHeight > 0 && footer.TranslatePoint(new(), objectives).Y + footer.ActualHeight <= objectives.ActualHeight, "Minimum objective panel keeps 0 Obj visible inside its bounds");
            check(Tree(objectives).OfType<TextBlock>().Any(t => t.Text == "No goals in Zone or Queue"), "Empty objective message describes the current zone without a misleading browse action");
            p.Zone = originalZone; objectives.Width = ow; objectives.Height = oh; objectives.Refresh();
        }
        finally
        {
            settings.Close(); p.Theme = savedTheme; p.MeterStyle = savedStyle; p.Dps.Opacity = p.Objectives.Opacity = savedOpacity; p.ShowLinkedHealing = savedHealing;
            ((App)Application.Current).ApplySettings(); quest.ShowView(TrackerTab.Items); quest.SetZoneFilter("All zones"); quest.SetSearch("");
        }
    }
}
