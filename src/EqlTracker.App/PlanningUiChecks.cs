using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class PlanningUiChecks
{
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child; }
    internal static void Run(TrackerController c, QuestWindow quest, Action<bool, string> check, string folder)
    {
        void Flush() { quest.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); quest.UpdateLayout(); }
        void Click(string text) { var button = Tree(quest.Body).OfType<Button>().First(b => b.IsVisible && b.Content?.ToString() == text); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
        var pins = c.Profile.GoalQueue.ToList(); var tracking = new Dictionary<string, bool>(c.Profile.QuestTracking); var quantities = new Dictionary<string, int>(c.Profile.QuestQuantities);
        double width = quest.Width; var measurements = new List<object>();
        try
        {
            var cold = new QuestWindow(c) { Left = -15000, Top = -15000, Width = 1220, Height = 800, ShowActivated = false };
            cold.SetSearch("cold-empty-planning-check"); cold.Show(); cold.ShowView(TrackerTab.Items); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); cold.UpdateLayout();
            var coldTable = Tree(cold).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "H/N"));
            var coldHeader = Tree(coldTable).OfType<DataGridColumnHeader>().Single(h => h.Column?.SortMemberPath == "Availability");
            var coldSearch = Tree(cold).OfType<TextBox>().Single(t => t.IsVisible && double.IsNaN(t.Width));
            check(coldTable.Items.Count == 0 && coldHeader.ActualWidth >= coldTable.Columns.Last().MinWidth && Math.Abs(coldHeader.TranslatePoint(new(coldHeader.ActualWidth, 0), cold).X - coldSearch.TranslatePoint(new(coldSearch.ActualWidth, 0), cold).X) < 2, "Cold-start empty Items aligns the actual Status header and search without generating a data row"); cold.Close();
            quest.ShowView(TrackerTab.Queue); Flush(); var tabs = Tree(quest).OfType<TabControl>().First();
            check(tabs.Items.OfType<TabItem>().Select(t => t.Tag).SequenceEqual(TrackerNavigation.Order.Cast<object>()), "Tracker tabs use stable Queue / Items / Quests / Progress / Gear / Exaltations / Pet order");
            check(Tree(quest).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.StartsWith("Your queue is empty")), "Empty Queue explains how to add quests and items");
            Click("Browse quests"); check((tabs.SelectedItem as TabItem)?.Tag is TrackerTab.Quests, "Browse quests opens the opt-in library");
            var library = Tree(quest).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "Quest / reward"));
            var darkforge = library.Items.OfType<QuestLibraryRow>().First(r => r.Quest.Name == "Darkforge Breastplate"); library.SelectedItem = darkforge; Flush();
            check(darkforge.Tracking == "Not tracked", "New Classic reward is initially untracked"); Click("Add to queue");
            check(c.Profile.GoalQueue.Any(g => g.Kind == "Quest" && g.QuestId == darkforge.Quest.Id), "Quest library queues a whole reward card and enables tracking");
            Click("Add to queue"); check(c.Profile.GoalQueue.Count(g => g.QuestId == darkforge.Quest.Id) == 1, "Repeated queue action deduplicates a quest card");
            quest.ShowView(TrackerTab.Queue); Flush(); var queue = Tree(quest).OfType<ListBox>().Single(l => l.IsVisible);
            check(queue.Items.OfType<QueueEntry>().Single(r => r.Goal.QuestId == darkforge.Quest.Id).Ingredients.Count == 3, "Quest queue card exposes its three ingredient requirements");
            quest.ShowView(TrackerTab.Quests); Flush(); Click("Track / untrack"); quest.ShowView(TrackerTab.Queue); Flush();
            check(queue.Items.OfType<QueueEntry>().Single(r => r.Goal.QuestId == darkforge.Quest.Id).Status == "Paused", "Untracking pauses a pinned reward instead of completing it");
            quest.ShowView(TrackerTab.Items); quest.SetZoneFilter("All zones");
            var table = Tree(quest).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "H/N"));
            foreach (double w in new[] { 640d, 940, 1220 }) foreach (bool empty in new[] { true, false })
            {
                quest.Width = w; quest.SetSearch(empty ? "no-match-ui-planning-xyz" : ""); Flush();
                var headers = Tree(table).OfType<DataGridColumnHeader>().Where(h => h.Column != null).ToList();
                var last = headers.Single(h => h.Column!.SortMemberPath == "Availability");
                var input = Tree(quest).OfType<TextBox>().Single(t => t.IsVisible && double.IsNaN(t.Width));
                double total = table.Columns.Sum(col => col.ActualWidth), end = last.TranslatePoint(new(last.ActualWidth, 0), quest).X, searchEnd = input.TranslatePoint(new(input.ActualWidth, 0), quest).X;
                bool fits = total <= table.ActualWidth + 1;
                check(last.ActualWidth >= table.Columns.Last().MinWidth && last.ActualWidth >= last.DesiredSize.Width - 1, $"Status retains its full width at {w} DIP / {(empty ? "empty" : "populated")}");
                if (fits) check(Math.Abs(end - searchEnd) <= 2, $"Actual Status header edge aligns with search at {w} DIP / {(empty ? "empty" : "populated")}");
                else check(ScrollViewer.GetHorizontalScrollBarVisibility(table) == ScrollBarVisibility.Auto, "Narrow Items table scrolls horizontally rather than shrinking Status");
                measurements.Add(new { Width = w, Empty = empty, Fits = fits, StatusWidth = last.ActualWidth, HeaderEnd = end, SearchEnd = searchEnd, TableWidth = table.ActualWidth, Columns = total });
                if (empty) check(Tree(quest).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "No goals match these filters."), "Empty Items message appears beneath retained headers");
            }
            quest.ShowView(TrackerTab.Pet, "Bloodmoon"); quest.SelectPetDestination(PetInventoryDestination.Planned); Flush();
            var petContext = Tree(quest).OfType<Expander>().Single(e => e.Header?.ToString() == "Pet context & manual confirmation"); petContext.IsExpanded = true; Flush();
            check(Tree(quest).OfType<Button>().Any(b => b.IsVisible && b.Content?.ToString() == "Use as current build…"), "Pet editor keeps explicit equipment confirmation in its context disclosure"); petContext.IsExpanded = false;
            check(Tree(quest).OfType<ComboBox>().Any(b => b.IsVisible && b.SelectedItem is PetBuild), "Pet editor initializes a selected build on first opening");
            check(Tree(quest).OfType<DataGrid>().Any(g => g.IsVisible && g.Columns.Any(col => col.Header?.ToString() == "Slots") && g.Items.Count > 0), "Pet catalog search returns Bloodmoon on first opening");
            var petCatalog = Tree(quest).OfType<DataGrid>().Single(g => g.IsVisible && g.Columns.Any(col => col.Header?.ToString() == "Slots")); petCatalog.SelectedIndex = 0;
            quest.ShowView(TrackerTab.Exaltations); Flush(); check(Tree(quest).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "Unlock Rank")).Items.Count > 0, "Exaltations exposes cached effects with their unlock ranks");
            check(Tree(quest.Body).OfType<Button>().Any(b => b.IsVisible && b.Template?.FindName("Face", b) != null), "Tracker content uses raised themed buttons");
            Json.Write(Path.Combine(folder, "planning-layout-checks.json"), measurements);
        }
        finally { c.Profile.GoalQueue = pins; c.Profile.QuestTracking = tracking; c.Profile.QuestQuantities = quantities; c.Save(); quest.Width = width; quest.ShowView(TrackerTab.Items); quest.SetSearch(""); Flush(); }
    }
}
