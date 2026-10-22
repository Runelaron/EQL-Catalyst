using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class FocusedWorkspaceChecks
{
    internal static void Run(TrackerController c, QuestWindow q, Action<bool, string> check)
    {
        IEnumerable<T> Find<T>() => Usability.Tree(q).OfType<T>();
        void Flush() { q.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); q.UpdateLayout(); }
        var pins = c.Profile.GoalQueue.ToList(); var tracking = new Dictionary<string, bool>(c.Profile.QuestTracking);
        var saved = EquipmentPositions.Copy(c.Profile.ActiveLoadout); int index = c.Profile.Loadouts.FindIndex(l => l.Name == c.Profile.SelectedLoadout);
        var expanded = c.Profile.ExpandedQueueEntries.ToHashSet();
        string combat = JsonSerializer.Serialize(c.Profile.Meter);
        try
        {
            q.Width = 1220; q.Height = 800; q.ShowView(TrackerTab.Items); Flush();
            var itemFilters = Find<Expander>().Single(e => e.Header?.ToString() == "Filters · classes & expansions"); itemFilters.IsExpanded = false; Flush();
            check(!Find<CheckBox>().Any(b => b.IsVisible), "Focused Items leaves class selection available inside a closed filter group");
            itemFilters.IsExpanded = true; Flush();
            check(Find<CheckBox>().Any(b => b.IsVisible && b.Content?.ToString()?.StartsWith("MAG") == true), "Items class controls remain usable when expanded"); itemFilters.IsExpanded = false;
            var quest = c.Catalog.Quests.First(x => x.Name == "Darkforge Breastplate");
            QuestTracking.Track(c.Profile, quest, true); var pin = new QueuedGoal { Kind = "Quest", QuestId = quest.Id, ItemKey = quest.RewardKey, Name = quest.Name };
            c.Profile.GoalQueue = [pin]; c.Profile.ExpandedQueueEntries.Add(pin.Id); c.Save(); q.ShowView(TrackerTab.Queue); Flush();
            var list = Find<ListBox>().Single(l => l.IsVisible); var entry = list.Items.OfType<QueueEntry>().Single();
            check(entry.Ingredients.Count > 0 && Find<TextBlock>().Any(t => t.IsVisible && t.Text == quest.Name), "Queue cards show actual goal names rather than internal record text");
            check(!Find<TextBlock>().Any(t => t.IsVisible && t.Text.StartsWith("QueueEntry {")), "Queue presentation never leaks internal record formatting");
            var ingredient = Find<Button>().First(b => b.DataContext is ObjectiveRow && b.IsVisible);
            var row = (ObjectiveRow)ingredient.DataContext; ingredient.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            check(Find<TextBlock>().Any(t => t.IsVisible && t.Text == row.Item && t.FontSize == 16), "Clicking a queued ingredient updates the adjacent source inspector");
            check(ScrollViewer.GetHorizontalScrollBarVisibility(list) == ScrollBarVisibility.Disabled, "Queue cards constrain long source names to the list viewport");
            var sources = Find<Button>().Single(b => b.IsVisible && b.Content?.ToString() == "View sources"); sources.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            check(Find<TabControl>().First().SelectedItem is TabItem { Tag: TrackerTab.Progress }, "Queue source action navigates to the matching quest branch without fragile name search");
            q.ShowView(TrackerTab.Quests, "Darkforge"); Flush();
            check(Find<Button>().Count(b => b.IsVisible && b.Content?.ToString() == "Queue") == 1, "Quest discovery exposes one queue action in the selected reward inspector");
            check(Find<Expander>().Any(e => e.Header?.ToString() == "Full checklist & coverage" && !e.IsExpanded), "Full quest narrative is disclosed on request in discovery");
            q.ShowView(TrackerTab.Exaltations, "Bloodmoon"); Flush();
            check(Find<ComboBox>().Any(b => b.Items.Contains("Any") && b.Items.Contains("Pet") && b.SelectedItem?.ToString() == "Any"), "Exaltations starts with Any; selecting a target exposes its assignment controls");
            GearBuildFixtures.Apply(c); q.SetGearTarget(false); q.ShowView(TrackerTab.Gear, "Red Dragonscale Armor"); q.SelectGearPosition("CHEST:1"); Flush();
            var gear = Find<DataGrid>().Single(g => g.Columns.Any(col => col.SortMemberPath == "ItemScore"));
            check(gear.Columns.Count(col => col.Visibility == Visibility.Visible) == 4, "Default gear columns prioritize name, both context deltas and ownership");
            q.ShowView(TrackerTab.Pet, "Bloodmoon"); q.SelectPetDestination(PetInventoryDestination.Planned); Flush();
            var filters = Find<Expander>().Single(e => e.Header?.ToString() == "Filters & Rank"); filters.IsExpanded = true; Flush();
            Find<ComboBox>().Single(b => b.Items.Contains("Character stock")).SelectedItem = "All items";
            Find<ComboBox>().Single(b => b.Items.Contains("All slots")).SelectedItem = "All slots"; filters.IsExpanded = false; Flush();
            q.SelectPetInventoryPosition(1); Flush();
            var candidates = Find<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "Slots"));
            check(candidates.Items.Count > 0, "Inventory row selection does not filter catalog items by its old equipment slot");
            q.SelectPetInventoryPosition(3); Flush();
            check(candidates.Items.Count > 0, "Selecting an empty storage position restores global candidate search");
            check(Find<Button>().Any(b => b.IsVisible && b.Content?.ToString() == "Add to planned build"), "Pet's primary action explicitly identifies its draft destination");
            q.SelectPetDestination(PetInventoryDestination.Confirmed); Flush();
            check(Find<Button>().Any(b => b.IsVisible && b.Content?.ToString() == "Add manual item"), "Confirmed destination explicitly labels a manual inventory report");
            q.Width = 640; q.Height = 420; Flush();
            check(candidates.IsVisible && candidates.ActualHeight >= 65 && Find<Button>().Any(b => b.IsVisible && b.Content?.ToString() == "Compare…"), "Narrow Pet preserves usable candidates and access to full comparison");
            check(JsonSerializer.Serialize(c.Profile.Meter) == combat, "Planning presentation changes do not alter combat selection or calculations");
        }
        finally
        {
            c.Profile.Loadouts[index] = saved; c.Profile.GoalQueue = pins; c.Profile.QuestTracking = tracking; c.Profile.ExpandedQueueEntries = expanded; c.Save();
            q.Width = 1220; q.Height = 800; q.ShowView(TrackerTab.Items); q.SetSearch("");
        }
    }
}
