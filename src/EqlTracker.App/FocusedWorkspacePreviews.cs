using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class FocusedWorkspacePreviews
{
    internal static void Render(string folder, TrackerController c, QuestWindow q)
    {
        var saved = EquipmentPositions.Copy(c.Profile.ActiveLoadout); int index = c.Profile.Loadouts.FindIndex(l => l.Name == c.Profile.SelectedLoadout);
        var pins = c.Profile.GoalQueue.ToList(); var tracking = new Dictionary<string, bool>(c.Profile.QuestTracking); var expanded = c.Profile.ExpandedQueueEntries.ToHashSet();
        void Flush() { q.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); q.UpdateLayout(); }
        void Save(string name, double width, double height, double scale)
        {
            q.Width = width; q.Height = height; Flush();
            var bitmap = PreviewRenderer.Capture((FrameworkElement)q.Content, width, height, scale);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file);
        }
        try
        {
            GearBuildFixtures.Apply(c);
            c.Profile.GoalQueue = [];
            foreach (string name in new[] { "Warrior Epic Quest", "Darkforge Breastplate", "White Dragonscale Cloak" })
                if (c.Catalog.Quests.FirstOrDefault(quest => quest.Name == name) is { } quest)
                { QuestTracking.Track(c.Profile, quest, true); var goal = new QueuedGoal { Kind = "Quest", QuestId = quest.Id, ItemKey = quest.RewardKey, Name = quest.Name }; c.Profile.GoalQueue.Add(goal); c.Profile.ExpandedQueueEntries.Add(goal.Id); }
            c.Save();
            foreach (var tab in TrackerNavigation.Order)
            {
                q.Width = 1220; q.Height = 690;
                q.ShowView(tab, tab switch { TrackerTab.Quests => "Darkforge", TrackerTab.Gear => "Red Dragonscale Armor", TrackerTab.Pet or TrackerTab.Exaltations => "Bloodmoon", TrackerTab.Progress => "Warrior Epic Quest", _ => null });
                if (tab == TrackerTab.Items) { q.SetZoneFilter("Current zone"); q.SetSearch(""); }
                if (tab == TrackerTab.Gear) { q.SetGearTarget(false); q.SelectGearPosition("CHEST:1"); }
                if (tab == TrackerTab.Pet) { q.SelectPetDestination(PetInventoryDestination.Planned); Flush(); q.SelectPetInventoryPosition(3); Flush(); Usability.Tree(q).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "Slots")).SelectedIndex = 0; }
                if (tab == TrackerTab.Progress) q.SelectProgressNode(node => node.ItemKeys.Contains("block of permafrost"));
                string key = tab.ToString().ToLowerInvariant(); Save("focused-" + key + ".png", 1220, 690, 1);
                foreach (double scale in new[] { 1d, 1.5, 2d, 3d }) Save($"qa/focused-{key}-minimum-{scale:0.0}x.png", 640, 420, scale);
            }
        }
        finally
        { c.Profile.Loadouts[index] = saved; c.Profile.GoalQueue = pins; c.Profile.QuestTracking = tracking; c.Profile.ExpandedQueueEntries = expanded; c.Save(); q.Width = 1220; q.Height = 800; q.ShowView(TrackerTab.Items); }
    }
}
