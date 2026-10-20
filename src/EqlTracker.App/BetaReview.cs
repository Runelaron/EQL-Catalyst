using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class BetaReview
{
    internal static void Run(string folder, TrackerController c, QuestWindow tracker, DpsWindow lab, OverlayWindow overlay)
    {
        if (!c.Demo) throw new InvalidOperationException("Isolated review required.");
        var checks = new List<string>(); var palettes = new List<object>(); var images = new List<object>();
        Directory.CreateDirectory(Path.Combine(folder, "qa"));
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void Show(Window window) { window.Left = window.Top = -15000; window.ShowInTaskbar = false; window.ShowActivated = false; window.Show(); Flush(); }
        void Capture(string name, FrameworkElement content, double width, double height, bool allScales = true)
        {
            foreach (double scale in allScales ? new[] { 1d, 1.5, 2, 3 } : new[] { 1d })
            {
                var bitmap = PreviewRenderer.Capture(content, width, height, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string file = $"beta-{name}-{scale:0.0}x.png"; using (var stream = File.Create(Path.Combine(folder, "qa", file))) encoder.Save(stream);
                images.Add(new { File = file, Scale = scale, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight });
                Check(bitmap.PixelWidth == (int)Math.Ceiling(width * scale), "Rendered pixel dimensions · " + file);
            }
        }
        FrameworkElement Content(Window w) => (FrameworkElement)w.Content;
        var original = c.Profile.Theme; c.Profile.SmoothMeterMotion = false; GearBuildFixtures.Apply(c);
        foreach (var window in new Window[] { tracker, lab, overlay }) Show(window);
        c.Profile.Theme = "Rune4K_Vellum";
        var legacySettings = new SettingsWindow(c); Show(legacySettings);
        var legacyPicker = Usability.Tree(legacySettings).OfType<ComboBox>().First(b => b.Items.Cast<object>().Select(o => o.ToString()).SequenceEqual(Appearance.Themes));
        Check(legacyPicker.SelectedItem?.ToString() == "Sunstrider", "Legacy theme has branded picker label");
        typeof(SettingsWindow).GetMethod("ApplyAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(legacySettings, null);
        Check(c.Profile.Theme == "Rune4K_Vellum", "Unrelated appearance edits preserve legacy theme ID"); legacySettings.Hide();
        var foregroundBrush = UI.Brush("Foreground");
        var semantic = new[] { "Melee", "Spell", "Healing", "Mint" }.ToDictionary(k => k, k => UI.Brush(k).Color);
        foreach (var theme in ThemeCatalog.Choices.Skip(1))
        {
            c.Profile.Theme = theme.Id; ((App)Application.Current).ApplySettings(); Flush();
            Check(ReferenceEquals(foregroundBrush, UI.Brush("Foreground")), theme.Name + " updates existing brushes in place");
            foreach (var pair in semantic) Check(UI.Brush(pair.Key).Color == pair.Value, theme.Name + " preserves semantic " + pair.Key);
            Check(UI.Brush("InnerSurface").Color.A == 191 && UI.Brush("DetailSurface").Color.A == 191, theme.Name + " retains 75 percent inner cards");
            Check(UI.Brush("Surface").Color.A == 190 && UI.Brush("SolidSurface").Color.A == 255, theme.Name + " retains transparent main and solid child surfaces");
            var colors = new[] { "Surface", "InnerSurface", "DetailSurface", "Foreground", "Muted", "Accent", "SecondaryText", "Selection", "AlternateRow", "CellSelection" }.ToDictionary(k => k, k => UI.Brush(k).Color.ToString()); palettes.Add(new { Theme = theme.Name, Colors = colors });
            if (theme.Id == "shadowman") foreach (string key in new[] { "Surface", "InnerSurface", "DetailSurface", "Foreground", "Muted", "Accent", "SecondaryText", "Selection", "AlternateRow", "CellSelection" }) { var color = UI.Brush(key).Color; Check(color.R == color.G && color.G == color.B, "Neutral Shadowman " + key); }
            var settings = new SettingsWindow(c); Show(settings); settings.SelectSection(SettingsSection.Appearance);
            Capture(theme.Id + "-settings", Content(settings), 940, 610);
            tracker.ShowView(TrackerTab.Gear); tracker.Refresh(); Flush(); Capture(theme.Id + "-gear", Content(tracker), 1220, 800);
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); lab.Refresh(); Flush(); Capture(theme.Id + "-damage", Content(lab), 1170, 740);
            lab.ShowView(CombatLabMode.Healing, CombatLabTab.Breakdown); lab.Refresh(); Flush(); Capture(theme.Id + "-healing", Content(lab), 1170, 740);
            overlay.Refresh(); Flush(); Capture(theme.Id + "-overlay", Content(overlay), 440, 410);
            var utility = new OverlayFilterWindow(c, overlay); Show(utility); Check(utility.Opacity == 1 && utility.AllowsTransparency, theme.Name + " utility keeps opaque text over theme transparency"); Capture(theme.Id + "-filters", Content(utility), 402, 350); utility.Close();
            var dialog = new EntryDialog("Name build", [("Build name", "Beta review")]) { Owner = tracker }; Show(dialog); Check(UI.Brush("Surface").Color.A < ((SolidColorBrush)dialog.Resources["Surface"]).Color.A, theme.Name + " editor remains solid"); Capture(theme.Id + "-dialog", Content(dialog), 560, 260); dialog.Close();
            tracker.ShowView(TrackerTab.Pet); tracker.Refresh(); Flush(); Capture(theme.Id + "-pet-narrow", Content(tracker), 680, 510);
            var setup = new FirstRunDialog([]); Show(setup); Capture(theme.Id + "-first-launch", Content(setup), 610, 475); setup.Close();
            settings.Hide();
            if (theme.Id == "shadowman")
            {
                foreach (var tab in Enum.GetValues<TrackerTab>()) { tracker.ShowView(tab); tracker.Refresh(); Flush(); Capture("shadowman-tab-" + tab.ToString().ToLowerInvariant(), Content(tracker), 1220, 800, false); }
            }
        }
        foreach (string key in new[] { "Classic EQ", "Frost Blue", "Midnight", "Warm Amber", "Rune4K_Frost", "Rune4K_Vellum", "Rune4K_Krome" }) { Appearance.Apply(key); Check(ThemeCatalog.Resolve(key).RenderKey == key, "Legacy renderer retained · " + key); }
        c.Profile.Theme = original; ((App)Application.Current).ApplySettings();
        string Hash(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name)))).ToLowerInvariant();
        Json.Write(Path.Combine(folder, "beta-checks.json"), new { Count = checks.Count, Checks = checks, Palettes = palettes, Images = images, AppSha256 = Hash("EQLTracker.dll"), CoreSha256 = Hash("EqlTracker.Core.dll"), Version = ReleaseInfo.Version, Label = ReleaseInfo.Label, Geometry = PreviewRenderer.CaptureGeometry });
    }
}
