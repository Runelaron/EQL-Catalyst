using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class PetUpgradePreviews
{
    internal static void Render(string folder, TrackerController c, QuestWindow q)
    {
        int index = c.Profile.Loadouts.FindIndex(p => p.Name == c.Profile.SelectedLoadout); var saved = EquipmentPositions.Copy(c.Profile.ActiveLoadout);
        void Flush() { q.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); q.UpdateLayout(); }
        void Save(string name, FrameworkElement view, double width, double height, double scale) { var bitmap = PreviewRenderer.Capture(view, width, height, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream); }
        try
        {
            var owner = c.Profile.ActiveLoadout; owner.PetType = "Earth"; owner.PetLevel = null; owner.Classes = ["SHD", "BRD", "MAG"]; owner.PetPreset = "Pet DPS";
            owner.ConfirmedPetInventory = new() { Name = "Confirmed inventory", PetType = "Earth", Items = new[] { ("Sunderfury", 4), ("Indicolite Breastplate", 5), ("Indicolite Bracer", 7), ("Indicolite Greaves", 7), ("Cloak of Flames", 4), ("Icebelt", 3), ("Indicolite Vambraces", 7) }.Select((x, i) => new PetInventoryItem { Id = "retained-" + i, Name = x.Item1, ItemKey = c.Catalog.Find(x.Item1)!.Key, Position = i + 1, Rank = x.Item2, Slot = "Storage", ConfirmedAt = DateTime.Now }).ToList() };
            owner.PetBuilds = [GuidedPetBuilds.Clone(owner.ConfirmedPetInventory, "Earth · weapon plan")]; c.Save();
            q.Width = 1220; q.Height = 760; q.ShowView(TrackerTab.Pet, "Indicolite Breastplate"); q.SelectPetDestination(PetInventoryDestination.Confirmed); q.PreviewPetRank(6); Flush();
            foreach (double dpi in new[] { 1d, 1.5, 2, 3 }) Save($"qa/pet-upgrades-{dpi:0.0}x.png", (FrameworkElement)q.Content, 1220, 760, dpi);
            Save("pet-upgrades.png", (FrameworkElement)q.Content, 1220, 760, 1);
            GearBuildFixtures.Apply(c); q.SetGearTarget(false); q.ShowView(TrackerTab.Gear, "Red Dragonscale Armor"); q.SelectGearPosition("CHEST:1"); Flush();
            Usability.Tree(q).OfType<Button>().Single(b => b.IsVisible && b.Content?.ToString() == "Add to build").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            foreach (double dpi in new[] { 1d, 1.5, 2, 3 }) Save($"qa/build-changes-{dpi:0.0}x.png", (FrameworkElement)q.Content, 1220, 760, dpi);
            Save("build-changes.png", (FrameworkElement)q.Content, 1220, 760, 1);
            c.SeedStatEvidencePreview(); var dialog = new StatProfileDialog(c, c.Profile.ActiveLoadout, false, c.Profile.ActiveLoadout.EquipmentPositions, () => { }) { Left = -15000, Top = -15000 }; dialog.Show();
            Usability.Tree(dialog).OfType<TabControl>().First().SelectedIndex = 2; dialog.UpdateLayout();
            foreach (double dpi in new[] { 1d, 1.5, 2, 3 }) Save($"qa/aa-controls-{dpi:0.0}x.png", (FrameworkElement)dialog.Content, 900, 650, dpi);
            Save("aa-controls.png", (FrameworkElement)dialog.Content, 900, 650, 1); dialog.Close();
            var card = UI.Card(UI.Stack(UI.Text("Upgrade Comparison", 18, "Accent", true), UI.Text("Equipment remains readable over the scene", 12), UI.Text("75% fill · opaque text and controls", 11, "Muted"))); card.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DetailSurface");
            var contrast = new Grid { Background = new LinearGradientBrush(Colors.LightSkyBlue, Color.FromRgb(10, 19, 25), 0) }; contrast.Children.Add(card); card.Margin = new(20); card.Width = 440; card.Height = 160;
            foreach (double dpi in new[] { 1d, 1.5, 2, 3 }) Save($"qa/inner-cards-{dpi:0.0}x.png", contrast, 520, 210, dpi);
            Save("inner-cards.png", contrast, 520, 210, 1);
        }
        finally { c.Profile.Loadouts[index] = saved; c.Save(); q.ShowView(TrackerTab.Items); }
    }
}

public sealed partial class QuestWindow
{
    internal void PreviewPetRank(int rank) { petFeedback.Text = ""; petRank.Text = rank.ToString(); petKey = ""; RefreshPet(); petResults.SelectedIndex = 0; }
}
