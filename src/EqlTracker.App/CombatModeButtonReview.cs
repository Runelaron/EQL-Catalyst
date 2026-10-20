using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class CombatModeButtonReview
{
    internal static void Run(TrackerController controller, string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "qa"));
        var lab = new DpsWindow(controller) { Left = -15000, Top = -15000, ShowActivated = false };
        lab.Show(); var results = new List<string>(); var proofs = new List<string>();
        void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); results.Add(label); }
        void Flush() { lab.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); lab.UpdateLayout(); }
        void Save(string name, BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file); proofs.Add(name); }
        Button FindButton(string label) => Usability.Tree(lab).OfType<Button>().Single(b => b.Content?.ToString() == label);
        var sheet = new StackPanel { Background = UI.Brush("SolidSurface"), Margin = new(0) }; sheet.Children.Add(UI.Text("B + C · Mode icons, serif labels, selected gold edge", 17, "Melee", true));
        foreach (var mode in Enum.GetValues<CombatLabMode>())
        {
            lab.Width = 1170; lab.Height = 760; Flush();
            FindButton(mode.ToString()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
            var active = FindButton(mode.ToString()); var inactive = FindButton(mode == CombatLabMode.Damage ? "Healing" : "Damage");
            Check(lab.Mode == mode && Usability.GetIsPressed(active) && !Usability.GetIsPressed(inactive), mode + " changes mode and retains exactly one pressed button");
            Check(((Border)active.Template.FindName("Rim", active)).BorderBrush == UI.Brush("Melee"), mode + " uses the selected gold rim");
            Check(Usability.Tree(active).OfType<DamageGlyph>().Single().Channel == (mode == CombatLabMode.Damage ? "Physical" : "Healing"), mode + " retains its original mode glyph/color");
            Check(Usability.Tree(active).OfType<TextBlock>().Any(t => t.Text == mode.ToString() && t.FontFamily.Source == "Georgia"), mode + " has readable serif lettering");
            Check(active.ToolTip != null && ToolTipService.GetInitialShowDelay(active) == 650, mode + " preserves tooltip timing");
            foreach (var size in new[] { (1170d, 760d), (640d, 420d) })
            {
                lab.Width = size.Item1; lab.Height = size.Item2; Flush(); var face = (FrameworkElement)lab.Content;
                Check(new[] { active, inactive }.All(b => { var bounds = b.TransformToAncestor(face).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight)); return bounds.Left >= 0 && bounds.Right <= size.Item1 && bounds.Bottom <= size.Item2; }), mode + $" buttons fit {size.Item1}×{size.Item2}");
                foreach (double dpi in new[] { 1d, 1.5, 2d, 3d }) Save($"qa/mode-combined-{mode.ToString().ToLowerInvariant()}-{(size.Item1 == 640 ? "narrow" : "wide")}-{dpi:0.0}x.png", PreviewRenderer.Capture(face, size.Item1, size.Item2, dpi));
            }
            lab.Width = 1170; lab.Height = 760; Flush(); var content = (FrameworkElement)lab.Content;
            var image = PreviewRenderer.Capture(content, 1170, 760, 2);
            var origin = FindButton("Damage").TranslatePoint(new(0, 0), content); var right = FindButton("Healing").TranslatePoint(new(FindButton("Healing").ActualWidth, 0), content);
            sheet.Children.Add(UI.Text(mode + " selected", 11, "SecondaryText")); sheet.Children.Add(new Image { Source = new CroppedBitmap(image, new((int)origin.X * 2, (int)origin.Y * 2, (int)(right.X - origin.X) * 2, 60)), Width = right.X - origin.X, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8) });
        }
        Save("combat-mode-combined.png", PreviewRenderer.Capture(sheet, 620, 160, 2));
        Json.Write(Path.Combine(folder, "mode-button-checks.json"), new { Passed = results.Count, AppAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))), CoreAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location))), Scope = "Only Combat Lab mode buttons; no other-window or performance reruns", Checks = results, Renders = proofs });
    }
}
