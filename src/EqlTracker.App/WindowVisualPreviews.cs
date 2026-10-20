using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class WindowVisualPreviews
{
    internal static void Render(string folder, TrackerController c, QuestWindow quest, DpsWindow lab, OverlayWindow meter)
    {
        var oldMeter = EquipmentPositions.Copy(c.Profile.Meter); string theme = c.Profile.Theme == "Auto" ? c.ThemeName : c.Profile.Theme;
        double width = meter.Width, height = meter.Height;
        void Flush() { quest.Refresh(); lab.Refresh(); meter.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        static void Save(string path, BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream); }
        try
        {
            c.Profile.Meter.Actor = "Combined"; c.Profile.Meter.Target = c.Profile.Meter.Ability = c.Profile.Meter.Kind = ""; c.Profile.Meter.CriticalOnly = false;
            quest.ShowView(TrackerTab.Pet, "Indicolite"); lab.ShowView(0); Flush();
            var popup = new EntryDialog("Edit pet inventory", [("Build name", "Water · sustained damage"), ("Notes", "Local changes only")], "Save commits this edit. Cancel preserves your saved values.") { Owner = quest, Left = -15000, Top = -15000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual }; popup.Show();
            try
            {
                foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
                {
                    Save(Path.Combine(folder, "qa", $"transparent-tracker-{scale:0.0}x.png"), PreviewRenderer.Capture((FrameworkElement)quest.Content, 1000, 650, scale));
                    Save(Path.Combine(folder, "qa", $"transparent-combat-{scale:0.0}x.png"), PreviewRenderer.Capture((FrameworkElement)lab.Content, 1000, 650, scale));
                    Save(Path.Combine(folder, "qa", $"opaque-child-{scale:0.0}x.png"), PreviewRenderer.Capture((FrameworkElement)popup.Content, 560, 300, scale));
                    var icons = new StackPanel { Orientation = Orientation.Horizontal, Background = UI.Brush("SolidSurface"), Margin = new(0) };
                    foreach (var actor in new[] { "Player", "Combined", "Water", "Earth", "Air", "Fire" })
                    {
                        var column = UI.Stack(UI.Text(actor == "Player" ? "Self" : actor == "Combined" ? "Total" : actor, 12)); column.Width = 90; column.Margin = new(8);
                        foreach (double size in new[] { 16d, 20, 24 })
                        { var icon = new ActorBadge { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left }; icon.Set(actor is "Player" or "Combined" ? actor : "Pet", true, actor, actor); column.Children.Add(UI.Row(icon, UI.Text(size.ToString("0") + " DIP", 10))); }
                        icons.Children.Add(column);
                    }
                    Save(Path.Combine(folder, "qa", $"actor-emblems-{scale:0.0}x.png"), PreviewRenderer.Capture(icons, 660, 140, scale));
                    var headers = new StackPanel { Orientation = Orientation.Horizontal, Background = UI.Brush("SolidSurface") };
                    foreach (double meterWidth in new[] { 128d, 190, 240, 440 })
                    {
                        meter.Width = meterWidth; meter.Height = 160; Flush();
                        var bitmap = PreviewRenderer.Capture((FrameworkElement)meter.Content, meterWidth, 160, scale);
                        var column = UI.Stack(UI.Text(meterWidth.ToString("0") + " DIP", 11), new Image { Source = bitmap, Width = meterWidth, Height = 160 }); column.Margin = new(8); headers.Children.Add(column);
                    }
                    Save(Path.Combine(folder, "qa", $"total-meter-widths-{scale:0.0}x.png"), PreviewRenderer.Capture(headers, 1070, 200, scale));
                }
                var q = PreviewRenderer.Capture((FrameworkElement)quest.Content, 1000, 650, 1);
                var d = PreviewRenderer.Capture((FrameworkElement)lab.Content, 1000, 650, 1);
                var child = PreviewRenderer.Capture((FrameworkElement)popup.Content, 560, 300, 1);
                foreach (var (name, main) in new[] { ("tracker", q), ("combat", d) })
                    foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
                    {
                        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen())
                        {
                            dc.DrawRectangle(UI.Brush("SolidSurface"), null, new Rect(0, 0, 2040, 730));
                            foreach (int offset in new[] { 0, 1020 })
                            {
                                var background = new SolidColorBrush(offset == 0 ? Color.FromRgb(37, 64, 65) : Color.FromRgb(226, 211, 176));
                                dc.DrawRectangle(background, null, new Rect(offset + 10, 50, 1000, 650));
                                var text = new FormattedText(offset == 0 ? "Dark backdrop · transparent workspace / solid child" : "Bright backdrop · transparent workspace / solid child", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, UI.Brush("Foreground"), 1);
                                dc.DrawText(text, new Point(offset + 10, 16)); dc.DrawImage(main, new Rect(offset + 10, 50, 1000, 650));
                                dc.DrawImage(child, new Rect(offset + 425, 120, 560, 300));
                            }
                        }
                        var proof = new RenderTargetBitmap((int)(2040 * scale), (int)(730 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); proof.Render(drawing);
                        Save(Path.Combine(folder, "qa", $"transparency-{name}-contrast-{scale:0.0}x.png"), proof);
                        if (scale == 1) Save(Path.Combine(folder, $"transparency-{name}.png"), proof);
                    }
                File.Copy(Path.Combine(folder, "qa", "actor-emblems-1.0x.png"), Path.Combine(folder, "total-emblem.png"), true);
                File.Copy(Path.Combine(folder, "qa", "total-meter-widths-1.0x.png"), Path.Combine(folder, "total-meter-widths.png"), true);
            }
            finally { popup.Close(); }
        }
        finally { c.Profile.Meter = oldMeter; Appearance.Apply(theme); meter.Width = width; meter.Height = height; c.Save(); quest.ShowView(TrackerTab.Items); Flush(); }
    }
}
