using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class WindowVisualChecks
{
    internal static void Run(TrackerController c, QuestWindow quest, DpsWindow lab, OverlayWindow meter, Action<bool, string> check)
    {
        static byte Alpha(FrostWindow window) => ((SolidColorBrush)((Border)window.Content).Background).Color.A;
        void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var theme = c.Profile.Theme == "Auto" ? c.ThemeName : c.Profile.Theme;
        double opacity = meter.Opacity;
        var oldMeter = EquipmentPositions.Copy(c.Profile.OverlayMeter!);
        try
        {
            quest.ShowView(TrackerTab.Pet); lab.ShowView(0); Flush();
            check(quest.SurfaceStyle == WindowSurface.Theme && lab.SurfaceStyle == WindowSurface.Theme, "Only the two main workspaces explicitly select theme surfaces");
            foreach (var owner in new FrostWindow[] { quest, lab })
            {
                var form = new EntryDialog("Surface check", [("Name", "Example")]) { Owner = owner, Left = -15000, Top = -15000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
                var comparison = new FrostWindow(c, "Comparison check", "", 640, 420) { Owner = owner, Left = -15000, Top = -15000 };
                form.Show(); comparison.Show(); Flush();
                foreach (string choice in Appearance.Themes.Where(t => t != "Auto"))
                {
                    Appearance.Apply(choice); Flush();
                    check(Alpha(quest) == UI.Brush("Surface").Color.A && Alpha(lab) == UI.Brush("Surface").Color.A && Alpha(quest) < 255 && quest.Opacity == 1 && lab.Opacity == 1,
                        "Main workspace transparency follows live theme brushes with crisp content · " + choice + " · " + owner.Title);
                    check(form.Background is SolidColorBrush { Color.A: 255 } && comparison.SurfaceStyle == WindowSurface.Solid && Alpha(comparison) == 255,
                        "Child form and comparison remain opaque · " + choice + " · " + owner.Title);
                    var combo = Usability.Tree(owner).OfType<ComboBox>().First(combo => combo.IsVisible); combo.ApplyTemplate();
                    check(((Popup)combo.Template.FindName("PART_Popup", combo)).Child is Border { Background: SolidColorBrush { Color.A: 255 } }, "Workspace dropdown stays opaque · " + choice + " · " + owner.Title);
                    var menu = new ContextMenu(); var tip = new ToolTip(); menu.Style = (Style)Application.Current.FindResource(typeof(ContextMenu)); tip.Style = (Style)Application.Current.FindResource(typeof(ToolTip));
                    check(menu.Background is SolidColorBrush { Color.A: 255 } && tip.Background is SolidColorBrush { Color.A: 255 }, "Independent menu and tooltip backgrounds stay opaque · " + choice);
                }
                comparison.Hide(); comparison.Show(); Flush(); check(Alpha(comparison) == 255, "Reopening an owned comparison retains its solid surface · " + owner.Title);
                comparison.Close(); form.Close();
            }
            Appearance.Apply(theme); Flush();
            foreach (var window in Application.Current.Windows.OfType<FrostWindow>().Where(w => w is SettingsWindow or StatProfileDialog or IconGallery or NativeControlsWindow))
                check(window.SurfaceStyle == WindowSurface.Solid && Alpha(window) == 255, "Utility/editor surface remains solid · " + window.Title);
            var badge = new ActorBadge();
            static byte[] Pixels(BitmapSource image) { var bytes = new byte[image.PixelHeight * image.PixelWidth * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes; }
            foreach (double size in new[] { 16d, 20, 24 })
            {
                badge.Set("Combined", false, "Pet text", "Unknown");
                var total = Pixels(PreviewRenderer.Capture(badge, size, size, 3));
                foreach (string element in PetElementDetector.Elements)
                {
                    badge.Set("Pet", true, "Owned " + element, element); PreviewRenderer.Capture(badge, size, size, 3);
                    badge.Set("Player", true, "Self", element); var self = Pixels(PreviewRenderer.Capture(badge, size, size, 3));
                    badge.Set("Combined", true, "Different pet evidence", element); var again = Pixels(PreviewRenderer.Capture(badge, size, size, 3));
                    check(total.SequenceEqual(again) && !total.SequenceEqual(self) && badge.Element == "Unknown" && badge.ToolTip?.ToString() == ActorBadge.TotalTooltip,
                        "Total is distinct from Self and ignores pet evidence after actor switching · " + size + " DIP · " + element);
                }
            }
            c.Profile.OverlayMeter!.Actor = "Combined"; meter.Refresh(); Flush();
            var liveBadge = Usability.Tree(meter).OfType<ActorBadge>().Single();
            check(liveBadge.ToolTip?.ToString() == ActorBadge.TotalTooltip, "Live Total meter exposes the exact Self + owned pet tooltip");
            check(meter.Opacity == opacity, "Main-window surface correction leaves compact overlay opacity unchanged");
            quest.Hide(); lab.Hide(); quest.Show(); lab.Show(); Flush(); check(Alpha(quest) < 255 && Alpha(lab) < 255, "Reopening both main windows preserves theme transparency");
        }
        finally { c.Profile.OverlayMeter = oldMeter; Appearance.Apply(theme); c.Save(); meter.Refresh(); }
    }
}
