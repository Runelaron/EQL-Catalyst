using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

// Public website images must never read an existing character database or game folder.
internal static class PublicScreens
{
    internal static void Run(string folder, TrackerController c, QuestWindow tracker, DpsWindow lab, OverlayWindow overlay)
    {
        if (!c.Demo || c.Profile.GameFolder.Length != 0 || c.Profile.Character != "ExampleHero") throw new InvalidOperationException("Synthetic public screenshot context required.");
        Directory.CreateDirectory(folder);
        var images = new List<string>();
        void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void Show(Window w) { w.ShowActivated = false; w.ShowInTaskbar = false; w.Left = w.Top = -15000; w.Show(); Flush(); }
        void Capture(string name, Window w, double width, double height)
        {
            Flush(); var image = PreviewRenderer.Capture((FrameworkElement)w.Content, width, height, 1.5);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(folder, name + ".png")); png.Save(output); images.Add(name);
        }
        GearBuildFixtures.Apply(c); c.Profile.SmoothMeterMotion = false;
        c.Profile.GoalQueue = [];
        foreach (string name in new[] { "Warrior Epic Quest", "Darkforge Breastplate", "White Dragonscale Cloak" })
            if (c.Catalog.Quests.FirstOrDefault(q => q.Name == name) is { } q)
            {
                QuestTracking.Track(c.Profile, q, true);
                var goal = new QueuedGoal { Kind = "Quest", QuestId = q.Id, ItemKey = q.RewardKey, Name = q.Name };
                c.Profile.GoalQueue.Add(goal); c.Profile.ExpandedQueueEntries.Add(goal.Id);
            }
        c.Save();
        foreach (var w in new Window[] { tracker, lab, overlay }) Show(w);
        foreach (var theme in ThemeCatalog.Choices.Skip(1))
        {
            c.Profile.Theme = theme.Id; ((App)Application.Current).ApplySettings(); Flush();
            foreach (var tab in Enum.GetValues<TrackerTab>())
            {
                tracker.ShowView(tab, tab == TrackerTab.Gear ? "Bloodmoon" : tab == TrackerTab.Pet ? "Sunderfury" : tab == TrackerTab.Progress ? "Magician Epic Quest" : null); tracker.Refresh(); Flush(); tracker.SelectPublicSample(tab); Flush();
                Capture(theme.Id + "-" + tab.ToString().ToLowerInvariant(), tracker, 1220, 800);
            }
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); lab.Refresh(); Capture(theme.Id + "-combat", lab, 1170, 740);
            lab.ShowView(CombatLabMode.Healing, CombatLabTab.Breakdown); lab.Refresh(); Capture(theme.Id + "-healing", lab, 1170, 740);
            overlay.Refresh(); Capture(theme.Id + "-overlay", overlay, 440, 410);
            var settings = new SettingsWindow(c); Show(settings); settings.SelectSection(SettingsSection.Appearance); Capture(theme.Id + "-appearance", settings, 940, 610); settings.Hide();
            var first = new FirstRunDialog([]); Show(first); Capture(theme.Id + "-setup", first, 610, 475); first.Close();
        }
        Json.Write(Path.Combine(folder, "screenshots.json"), new { Synthetic = true, Character = "ExampleHero", ImportedUserData = false, Images = images });
    }
}

public sealed partial class QuestWindow
{
    internal void SelectPublicSample(TrackerTab tab)
    {
        if (!Controller.Demo) throw new InvalidOperationException("Synthetic screenshot context required.");
        if (tab == TrackerTab.Pet) petResults.SelectedItem = petResults.Items.OfType<PetSearchRow>().FirstOrDefault();
        if (tab == TrackerTab.Queue) queueList.SelectedIndex = queueList.Items.Count > 0 ? 0 : -1;
    }
}
