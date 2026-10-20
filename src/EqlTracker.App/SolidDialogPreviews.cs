using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class SolidDialogPreviews
{
    internal static void Render(string folder, TrackerController c, QuestWindow q)
    {
        var saved = EquipmentPositions.Copy(c.Profile.ActiveLoadout); int index = c.Profile.Loadouts.FindIndex(l => l.Name == c.Profile.SelectedLoadout);
        void Save(string name, FrameworkElement view, double width, double height, double scale)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(view, width, height, scale)));
            using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
        }
        void Flush() { q.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); q.UpdateLayout(); }
        try
        {
            GearBuildFixtures.Apply(c); q.Width = 1220; q.Height = 690; q.ShowView(TrackerTab.Pet, "Indicolite"); q.SelectPetDestination(PetInventoryDestination.Planned); Flush();
            q.AddPetCandidateForCheck("bloodmoon", 1); Flush();
            var inventory = c.Profile.ActiveLoadout.PetBuilds[0];
            var item = inventory.Items.First(i => i.Name == "Indicolite Breastplate");
            using var editorScope = new WindowScope(new PetItemDialog(c, c.Catalog.FindKey(item.ItemKey)!, inventory, PetInventoryDestination.Planned, item) { Owner = q, Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual });
            var editor = editorScope.Window;
            using var formScope = new WindowScope(new EntryDialog("Rename planned build", [("Build name", "Water · sustained damage")], "Rename this local plan. Confirmed inventory stays unchanged.") { Owner = q, Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual });
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
            {
                Save($"qa/solid-pet-editor-{scale:0.0}x.png", (FrameworkElement)editor.Content, 560, 400, scale);
                Save($"qa/solid-entry-{scale:0.0}x.png", (FrameworkElement)formScope.Window.Content, 560, 220, scale);
                Save($"qa/solid-pet-{scale:0.0}x.png", (FrameworkElement)q.Content, 1220, 690, scale);
            }
            Save("solid-pet-editor.png", (FrameworkElement)editor.Content, 560, 400, 1);
            Save("solid-entry.png", (FrameworkElement)formScope.Window.Content, 560, 220, 1);
            Save("solid-pet.png", (FrameworkElement)q.Content, 1220, 690, 1);
            var comparison = new FrostWindow(c, "Pet comparison", "Inventory presence does not establish equipped slots", 640, 480) { Owner = q, Left = -15000, Top = -15000, ShowActivated = false };
            var details = new ComparisonPanel(); details.ExplainInventory(c.Catalog.Find("Bloodmoon")!, 0); comparison.Body.Children.Add(UI.Scroll(details)); comparison.Show();
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d }) Save($"qa/solid-comparison-{scale:0.0}x.png", (FrameworkElement)comparison.Content, 640, 480, scale);
            Save("solid-comparison.png", (FrameworkElement)comparison.Content, 640, 480, 1); comparison.Close();
            q.ShowView(TrackerTab.Exaltations, ""); Flush();
            var grid = Usability.Tree(q).OfType<DataGrid>().Single(g => g.Columns.Any(col => col.Header?.ToString() == "Unlock Rank"));
            grid.SelectedIndex = 0;
            var split = (Grid)grid.Parent; split.ColumnDefinitions[0].Width = new(.46, GridUnitType.Star); split.ColumnDefinitions[2].Width = new(.54, GridUnitType.Star); Flush();
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d }) Save($"qa/solid-exaltations-{scale:0.0}x.png", (FrameworkElement)q.Content, 1220, 690, scale);
            Save("solid-exaltations.png", (FrameworkElement)q.Content, 1220, 690, 1);
            q.ShowView(TrackerTab.Gear, "Red Dragonscale Armor"); q.SetGearTarget(false); q.SelectGearPosition("CHEST:1"); Flush(); Save("solid-gear.png", (FrameworkElement)q.Content, 1220, 690, 1);
        }
        finally { c.Profile.Loadouts[index] = saved; c.Save(); q.Width = 1220; q.Height = 800; q.ShowView(TrackerTab.Items); }
    }
    private sealed class WindowScope(Window window) : IDisposable
    {
        internal Window Window { get; } = Open(window);
        private static Window Open(Window window) { window.Show(); return window; }
        public void Dispose() => Window.Close();
    }
}
