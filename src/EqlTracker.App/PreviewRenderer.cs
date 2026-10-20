using EqlTracker.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EqlTracker.App;

public static class PreviewRenderer
{
    public static void Render(string folder, QuestWindow quest, DpsWindow dps, OverlayWindow objectives, OverlayWindow meter, double scale)
    {
        Directory.CreateDirectory(folder);
        foreach (var window in new Window[] { quest, dps, objectives, meter })
        {
            window.ShowInTaskbar = false; window.ShowActivated = false; window.Left = -15000; window.Top = -15000; window.Show();
        }
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        quest.ShowView(TrackerTab.Items);
        dps.MarkAbility("backstabs");
        FrameworkElement View(Window w) => (FrameworkElement)w.Content;
        var q = Capture(View(quest), 1220, 800, scale); var d = Capture(View(dps), 1170, 740, scale);
        var o = Capture(View(objectives), 380, 360, scale); var m = Capture(View(meter), 440, 410, scale);
        var micro = Capture(View(meter), 190, 88, scale); var smallMeter = Capture(View(meter), 240, 160, scale);
        Compose(Path.Combine(folder, "quest-layouts.png"), 1680, 930, scale, "QUEST TRACKER / DENSE FROST", [(q, 28, 66, 1220, 800), (o, 1274, 66, 380, 360)], "Actual WPF controls · Permafrost objectives · synthetic inventory sample");
        Compose(Path.Combine(folder, "dps-layouts.png"), 1692, 870, scale, "COMBAT LAB / DENSE FROST", [(d, 28, 66, 1170, 740), (m, 1224, 66, 440, 410), (smallMeter, 1224, 504, 240, 160), (micro, 1224, 688, 190, 88)], "Illustrative combat fixture · actual controls · hotbar, compact and full meter sizes");
        Compose(Path.Combine(folder, "combined-4k.png"), 3840, 2160, scale, "EQL CATALYST / 3840 × 2160 LAYOUT PREVIEW", [(q, 80, 220, 1220, 800), (d, 1580, 220, 1170, 740), (o, 3350, 220, 380, 360), (m, 3350, 620, 440, 410)], "Actual companion components · movable overlays preserve the central view · native controls install separately");
        // Additional layout QA captures exercise constrained viewports and DPI scaling.
        double currentDpi = VisualTreeHelper.GetDpi(meter).DpiScaleX;
        foreach (var dpi in new[] { 1d, 1.5, 2d, currentDpi }.Distinct())
        {
            var qa = Path.Combine(folder, "qa"); Directory.CreateDirectory(qa);
            Save(Path.Combine(qa, $"quest-1080-{dpi:0.0}x.png"), Capture(View(quest), dpi == 2 ? 930 : 1160, dpi == 2 ? 520 : 800, dpi));
            Save(Path.Combine(qa, $"dps-1080-{dpi:0.0}x.png"), Capture(View(dps), dpi == 2 ? 930 : 1160, dpi == 2 ? 520 : 740, dpi));
            Save(Path.Combine(qa, $"meter-narrow-{dpi:0.0}x.png"), Capture(View(meter), 340, 410, dpi));
            Save(Path.Combine(qa, $"meter-user-width-{dpi:0.0}x.png"), Capture(View(meter), 140, 320, dpi));
            Save(Path.Combine(qa, $"meter-hotbar-{dpi:0.0}x.png"), Capture(View(meter), 190, 88, dpi));
            Save(Path.Combine(qa, $"meter-minimum-{dpi:0.0}x.png"), Capture(View(meter), 128, 72, dpi));
            Save(Path.Combine(qa, $"objectives-minimum-{dpi:0.0}x.png"), Capture(View(objectives), 128, 90, dpi));
            Save(Path.Combine(qa, $"quest-minimum-{dpi:0.0}x.png"), Capture(View(quest), 640, 420, dpi));
            Save(Path.Combine(qa, $"combat-minimum-{dpi:0.0}x.png"), Capture(View(dps), 640, 420, dpi));
            Save(Path.Combine(qa, $"meter-compact-{dpi:0.0}x.png"), Capture(View(meter), 240, 160, dpi));
            Save(Path.Combine(qa, $"meter-summary-{dpi:0.0}x.png"), Capture(View(meter), 190, 112, dpi));
            Save(Path.Combine(qa, $"meter-4k-{dpi:0.0}x.png"), Capture(View(meter), 440, 410, dpi));
            Save(Path.Combine(qa, $"dps-4k-{dpi:0.0}x.png"), Capture(View(dps), 1170, 740, dpi));
            Save(Path.Combine(qa, $"quest-4k-{dpi:0.0}x.png"), Capture(View(quest), 1220, 800, dpi));
        }
        quest.ShowView(TrackerTab.Gear, "Bloodmoon"); Save(Path.Combine(folder, "qa", "gear-bloodmoon.png"), Capture(View(quest), 1220, 800, 1));
        foreach (var dpi in new[] { 1d, 1.5, 2d, currentDpi }.Distinct())
        {
            Save(Path.Combine(folder, "qa", $"gear-minimum-{dpi:0.0}x.png"), Capture(View(quest), 640, 420, dpi));
            quest.ShowView(TrackerTab.Progress, "Magician Epic Quest"); Save(Path.Combine(folder, "qa", $"quest-tree-{dpi:0.0}x.png"), Capture(View(quest), 1220, 800, dpi));
            quest.ShowView(TrackerTab.Gear, "Bloodmoon");
        }
        quest.ShowView(TrackerTab.Items);
        dps.ShowView(4);
        foreach (var dpi in new[] { 1d, 1.5, 2d, currentDpi }.Distinct())
        {
            Save(Path.Combine(folder, "qa", $"healing-4k-{dpi:0.0}x.png"), Capture(View(dps), 1170, 740, dpi));
            Save(Path.Combine(folder, "qa", $"healing-minimum-{dpi:0.0}x.png"), Capture(View(dps), 640, 420, dpi));
        }
        Save(Path.Combine(folder, "healing-layout.png"), Capture(View(dps), 1170, 740, 1)); dps.ShowView(0);
        meter.ToggleCollapse(); Save(Path.Combine(folder, "qa", "meter-collapsed.png"), Capture(View(meter), 240, 28, 3)); meter.ToggleCollapse();
        foreach (var window in new Window[] { quest, dps }) { window.MinWidth = 640; window.MinHeight = 420; }
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Background = UI.Brush("Surface") };
        foreach (string element in EqlTracker.Core.PetElementDetector.Elements)
        { var badge = new ActorBadge { Width = 32, Height = 32 }; badge.Set("Pet", true, element, element); var label = UI.Text(element, 12); label.HorizontalAlignment = HorizontalAlignment.Center; var card = UI.Stack(badge, label); card.Width = 70; card.Margin = new(8); icons.Children.Add(card); }
        Save(Path.Combine(folder, "qa", "element-vector-badges.png"), Capture(icons, 344, 76, 2));
        AppearancePreviews(folder, ((App)Application.Current).Controller, scale);
        PlanningPreviews(folder, quest);
        ClassicPreviews(folder, quest);
        ProgressDetailPreviews(folder, ((App)Application.Current).Controller, quest);
        UsabilityPreviews(folder, ((App)Application.Current).Controller, quest, dps);
        GearBuildPreviews.Render(folder, ((App)Application.Current).Controller, quest);
        FocusedWorkspacePreviews.Render(folder, ((App)Application.Current).Controller, quest);
        SolidDialogPreviews.Render(folder, ((App)Application.Current).Controller, quest);
        PetUpgradePreviews.Render(folder, ((App)Application.Current).Controller, quest);
        WindowVisualPreviews.Render(folder, ((App)Application.Current).Controller, quest, dps, meter);
        CombatPolishPreviews.Render(folder, ((App)Application.Current).Controller, dps, quest);
        SessionPolishPreviews.Render(folder, ((App)Application.Current).Controller, dps);
        meter.MinWidth = 128; meter.MinHeight = 72; objectives.MinWidth = 128; objectives.MinHeight = 90;
        quest.Width = 1220; quest.Height = 800; dps.Width = 1170; dps.Height = 740; meter.Width = 440; meter.Height = 410; objectives.Width = 380; objectives.Height = 360;
        File.WriteAllText(Path.Combine(folder, "render-report.txt"), $"Rendered actual WPF component trees. Main previews use 4K-oriented logical dimensions. QA renders cover constrained 1080p, 4K, 128×72 DPS, 128×90 objectives, and 640×420 expanded panels at 100%, 150%, 200%, and the current Windows scaling ({currentDpi:P0}). Combat values are illustrative fixture data.");
    }
    private static void PlanningPreviews(string folder, QuestWindow quest)
    {
        var controller = ((App)Application.Current).Controller; var p = controller.Profile;
        var pins = p.GoalQueue.ToList(); var tracking = new Dictionary<string, bool>(p.QuestTracking); var builds = p.ActiveLoadout.PetBuilds.ToList();
        try
        {
            foreach (var q in controller.Catalog.Quests.Where(q => q.Name is "Darkforge Breastplate" or "White Dragonscale Cloak"))
            { EqlTracker.Core.QuestTracking.Track(p, q, true); var g = new EqlTracker.Core.QueuedGoal { Kind = "Quest", QuestId = q.Id, ItemKey = q.RewardKey, Name = q.Name }; p.GoalQueue.Add(g); p.ExpandedQueueEntries.Add(g.Id); }
            controller.Save(); quest.ShowView(TrackerTab.Queue); Save(Path.Combine(folder, "queue-layout.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            quest.ShowView(TrackerTab.Quests, "Darkforge"); Save(Path.Combine(folder, "quest-discovery.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            p.ActiveLoadout.PetBuilds = [new() { Name = "Earth · weapon plan", PetType = "Earth", PetLevel = 49, Items = [new() { Position = 1, Name = "Bloodmoon", ItemKey = "bloodmoon", Slot = "Primary", Rank = 4 }, new() { Position = 2, Name = "Darkforge Breastplate", ItemKey = "darkforge breastplate", Slot = "Chest", Rank = 1 }] }]; controller.Save();
            quest.ShowView(TrackerTab.Pet, "Bloodmoon"); quest.SelectPetDestination(PetInventoryDestination.Planned); Save(Path.Combine(folder, "pet-builds.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            quest.ShowView(TrackerTab.Exaltations, "Bloodmoon"); Save(Path.Combine(folder, "exaltations.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            quest.ShowView(TrackerTab.Items); quest.SetZoneFilter("All zones"); quest.SetSearch("no-matching-goal-preview");
            foreach (double dpi in new[] { 1d, 1.5, 2d, 3d }) Save(Path.Combine(folder, "qa", $"items-empty-{dpi:0.0}x.png"), Capture((FrameworkElement)quest.Content, 1220, 800, dpi));
            Save(Path.Combine(folder, "items-empty.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
        }
        finally { p.GoalQueue = pins; p.QuestTracking = tracking; p.ActiveLoadout.PetBuilds = builds; controller.Save(); quest.SetSearch(""); quest.ShowView(TrackerTab.Items); }
    }
    private static void ClassicPreviews(string folder, QuestWindow quest)
    {
        var controller = ((App)Application.Current).Controller;
        foreach (var name in new[] { "Helm of Ro", "Regent Symbol of Innoruuk", "Paw of Opolla", "White Dragonscale Cloak" })
        {
            quest.ShowView(TrackerTab.Progress,name);
            Save(Path.Combine(folder,"classic-"+name.ToLowerInvariant().Replace(' ','-')+".png"),Capture((FrameworkElement)quest.Content,1220,800,1));
        }
        var gallery = new IconGallery(controller) { Left=-15000, Top=-15000, ShowActivated=false };gallery.Show();
        foreach(double dpi in new[] {1d,1.5,2d,3d}) Save(Path.Combine(folder,"qa",$"icon-picker-{dpi:0.0}x.png"),Capture((FrameworkElement)gallery.Content,640,550,dpi));
        Save(Path.Combine(folder,"icon-picker.png"),Capture((FrameworkElement)gallery.Content,640,550,1));gallery.Close();
        quest.ShowView(TrackerTab.Items);
    }
    private static void ProgressDetailPreviews(string folder, TrackerController controller, QuestWindow quest)
    {
        var war = controller.Catalog.Quests.Single(q => q.Class == "WAR" && q.Kind == "Epic");
        quest.ShowView(TrackerTab.Progress, war.Name);
        foreach (string key in new[] { "block of permafrost", "finely crafted dragon head hilt", "" })
        {
            quest.SelectProgressNode(n => key.Length == 0 ? n.Kind == QuestNodeKind.Quest : n.ItemKeys.Contains(key));
            string name = key.Length == 0 ? "quest" : key == "block of permafrost" ? "ingredient" : "intermediate";
            Save(Path.Combine(folder, "progress-" + name + ".png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            foreach (double dpi in new[] { 1d, 1.5, 2d, 3d })
                Save(Path.Combine(folder, "qa", $"progress-{name}-{dpi:0.0}x.png"), Capture((FrameworkElement)quest.Content, 940, 610, dpi));
        }
        quest.ShowView(TrackerTab.Items);
    }
    private static void AppearancePreviews(string folder, TrackerController controller, double scale)
    {
        string originalTheme = controller.Profile.Theme; var samples = new List<(ImageSource, double, double, double, double)>();
        foreach (string name in new[] { "Classic EQ", "Frost Blue", "Midnight", "Warm Amber" })
        {
            controller.Profile.Theme = name; Appearance.Apply(name); var settings = new SettingsWindow(controller) { Left = -15000, Top = -15000, ShowActivated = false }; settings.Show();
            samples.Add((Capture((FrameworkElement)settings.Content, 940, 610, 1), 28 + samples.Count % 2 * 970, 66 + samples.Count / 2 * 640, 940, 610)); settings.Close();
        }
        Compose(Path.Combine(folder, "theme-settings.png"), 1968, 1360, scale, "EQL CATALYST / LIVE APPEARANCE", samples, "Actual Settings controls · Classic EQ / Frost Blue / Midnight / Warm Amber · settings rail · gold title · raised buttons · live appearance");
        controller.Profile.Theme = originalTheme; Appearance.Apply(originalTheme == "Auto" ? controller.ThemeName : originalTheme);
    }
    private static void UsabilityPreviews(string folder, TrackerController controller, QuestWindow quest, DpsWindow lab)
    {
        var settings = new SettingsWindow(controller) { Left = -15000, Top = -15000, ShowActivated = false }; settings.Show();
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            settings.SelectSection(section);
            Save(Path.Combine(folder, "settings-" + section.ToString().ToLowerInvariant() + ".png"), Capture((FrameworkElement)settings.Content, 940, 610, 1));
        }
        settings.SelectSection(SettingsSection.Appearance);
        foreach (double dpi in new[] { 1d, 1.5, 2d, 3d })
        {
            Save(Path.Combine(folder, "qa", $"settings-rail-{dpi:0.0}x.png"), Capture((FrameworkElement)settings.Content, 940, 610, dpi));
            Save(Path.Combine(folder, "qa", $"settings-minimum-{dpi:0.0}x.png"), Capture((FrameworkElement)settings.Content, 640, 420, dpi));
        }
        settings.Close();
        var p = controller.Profile; int index = p.Loadouts.FindIndex(l => l.Name == p.SelectedLoadout); string saved = System.Text.Json.JsonSerializer.Serialize(p.ActiveLoadout);
        try
        {
            p.ActiveLoadout.Preset = "Tank"; p.ActiveLoadout.Equipment = [new() { Location = "Chest", Name = "Apothic Robe +5", GameId = 1240, Count = 1 }]; controller.Save();
            quest.ShowView(TrackerTab.Quests, "Red Dragonscale Armor");
            Save(Path.Combine(folder, "recommendation-red-dragonscale.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            var inventory = new PetBuild { Name = "Confirmed inventory", PetType = "Earth", PetLevel = 49, Items = [new() { Position = 1, ItemKey = "bloodmoon", Name = "Bloodmoon", Slot = "Primary", Rank = 4, ConfirmedAt = DateTime.Now }] };
            if (controller.Catalog.Find("Indicolite Breastplate") is { } armor) inventory.Items.Add(new() { Position = 2, ItemKey = armor.Key, Name = armor.Name, Slot = "Chest", Rank = 2, ConfirmedAt = DateTime.Now });
            PetInventoryEditing.Confirm(p.ActiveLoadout, inventory, DateTime.Now); controller.Save(); quest.ShowView(TrackerTab.Pet, "Indicolite"); quest.SelectPetDestination(PetInventoryDestination.Confirmed);
            Save(Path.Combine(folder, "pet-confirmed-inventory.png"), Capture((FrameworkElement)quest.Content, 1220, 800, 1));
            var editor = new PetItemDialog(controller, controller.Catalog.Find("Bloodmoon")!, inventory, PetInventoryDestination.Planned) { Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual }; editor.Show();
            Save(Path.Combine(folder, "pet-item-editor.png"), Capture((FrameworkElement)editor.Content, 600, 480, 1)); editor.Close();
            lab.Width = 1170; lab.Height = 740; lab.ShowView(0); Save(Path.Combine(folder, "combat-usability.png"), Capture((FrameworkElement)lab.Content, 1170, 740, 1));
            lab.ShowView(4); Save(Path.Combine(folder, "healing-layout.png"), Capture((FrameworkElement)lab.Content, 1170, 740, 1)); lab.ShowView(0);
        }
        finally { p.Loadouts[index] = System.Text.Json.JsonSerializer.Deserialize<LoadoutProfile>(saved)!; controller.Save(); quest.ShowView(TrackerTab.Quests, ""); quest.ShowView(TrackerTab.Items); }
    }
    internal static readonly List<object> CaptureGeometry = [];
    internal static RenderTargetBitmap Capture(FrameworkElement element, double width, double height, double scale)
    {
        var owner = Window.GetWindow(element); bool root = owner?.Content == element;
        if (owner is { } window) { window.MinWidth = 0; window.MinHeight = 0; window.Width = width; window.Height = height; window.UpdateLayout(); }
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        // A native HWND can cap its client size to the host monitor. Render the same
        // component tree detached, retaining its theme, so wide proofs are not clipped.
        var resources = element.Resources; double oldWidth = element.Width, oldHeight = element.Height;
        Grid? host=null;
        try
        {
            if (root) { owner!.Content = null; owner.UpdateLayout(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); var scope = new ResourceDictionary(); scope.MergedDictionaries.Add(owner.Resources); scope.MergedDictionaries.Add(resources); element.Resources = scope; }
            element.Width = width; element.Height = height;
            if(VisualTreeHelper.GetParent(element)==null){host=new Grid { Width=width,Height=height,UseLayoutRounding=true };host.Children.Add(element);host.Measure(new Size(width,height));host.Arrange(new Rect(0,0,width,height));}
            element.InvalidateMeasure();element.InvalidateArrange();
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            CaptureGeometry.Add(new { Width = width, Height = height, ActualWidth = element.ActualWidth, ActualHeight = element.ActualHeight, Scale = scale, Parent = VisualTreeHelper.GetParent(element)?.GetType().Name, Clip = VisualTreeHelper.GetClip(element)?.Bounds.ToString(), Dpi = VisualTreeHelper.GetDpi(element).DpiScaleX });
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(element); return bitmap;
        }
        finally { if(host!=null)host.Children.Remove(element);element.Width = oldWidth; element.Height = oldHeight; if (root) { element.Resources = resources; owner!.Content = element; owner.UpdateLayout(); } }
    }
    private static void Compose(string path, int width, int height, double scale, string heading, IEnumerable<(ImageSource Image, double X, double Y, double W, double H)> images, string note)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(4, 10, 17)), null, new Rect(0, 0, width, height));
            var text = new FormattedText(heading, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), width > 2000 ? 30 : 22, UI.Brush("Accent"), 1); dc.DrawText(text, new Point(30, 25));
            foreach (var (image, x, y, w, h) in images) dc.DrawImage(image, new Rect(x, y, w, h));
            dc.DrawText(new FormattedText(note, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), width > 2000 ? 20 : 12, UI.Brush("Muted"), 1), new Point(30, height - 30));
        }
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(drawing); Save(path, bitmap);
    }
    private static void Save(string path, BitmapSource image) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
}
