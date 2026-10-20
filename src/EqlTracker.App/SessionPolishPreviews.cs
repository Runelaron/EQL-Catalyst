using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class SessionPolishPreviews
{
    internal static void Render(string folder, TrackerController c, DpsWindow lab)
    {
        var meter = EquipmentPositions.Copy(c.Profile.Meter); var preferences = EquipmentPositions.Copy(c.Profile.CombatLab);
        var previous = c.Sessions;
        void Flush() { lab.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); lab.UpdateLayout(); }
        void Save(string name, BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file); }
        try
        {
            var fights = c.Combat.Encounters.OrderBy(f => f.Start).ToArray();
            var s = new PlaySession { Id = "preview-session", Start = fights[0].Start.AddMinutes(-5), LastObserved = fights[^1].End, Evidence = "Illustrative verified-login fixture", Classes = [new("preview-who", fights[0].Start, "BRD/MAG/SHD", "Illustrative /who evidence", "Preview fixture")], Members = [new() { Name = "Shieldwarden", From = fights[0].Start }, new() { Name = "Lightweaver", From = fights[0].Start }] };
            c.SetPreviewSessions(new(999, [s], [new(s.Id, $"{s.Start:MMM/dd HH:mm} · {fights.Length} fights · saved session")]));
            c.Profile.Meter = new() { Scope = "Session", SessionId = s.Id, Actor = "Combined" }; c.Profile.CombatLab.ShowContribution = true;
            foreach (var (mode, tab, name) in new[] { (CombatLabMode.Damage, CombatLabTab.History, "session-history"), (CombatLabMode.Healing, CombatLabTab.History, "session-history-healing"), (CombatLabMode.Damage, CombatLabTab.Comparison, "session-comparison"), (CombatLabMode.Damage, CombatLabTab.Breakdown, "session-breakdown"), (CombatLabMode.Damage, CombatLabTab.Group, "session-members") })
            {
                lab.Width = 1170; lab.Height = 760; lab.ShowView(mode, tab); Flush();
                Save(name + ".png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 1170, 760, 1));
                foreach (double dpi in new[] { 1d, 1.5, 2d, 3d })
                {
                    Save($"qa/{name}-{dpi:0.0}x.png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 1170, 760, dpi));
                    lab.Width = 640; lab.Height = 420; Flush(); Save($"qa/{name}-narrow-{dpi:0.0}x.png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 640, 420, dpi)); lab.Width = 1170; lab.Height = 760; Flush();
                }
            }
            var sheet = new StackPanel { Background = UI.Brush("SolidSurface"), Margin = new(0) }; Appearance.RaisedButtons(sheet);
            sheet.Children.Add(UI.Text("Damage / Healing · review-only alternatives", 20, "Melee", true));
            foreach (int option in new[] { 0, 1, 2 })
            {
                var card = UI.Stack(UI.Label(new[] { "A · Serif labels", "B · Mode glyphs + familiar type", "C · Serif + selected gold edge" }[option])); var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 8) };
                foreach (var (text, channel, active) in new[] { ("Damage", "Physical", true), ("Healing", "Healing", false) })
                {
                    var button = UI.Button(text, () => { }); button.Width = 150; button.Height = 34; button.Margin = new(0, 0, 12, 0);
                    if (option != 1) button.FontFamily = new("Georgia");
                    else { button.ContentTemplate = null; button.Content = UI.Row(new DamageGlyph { Channel = channel, Width = 16, Height = 16, Margin = new(0, 0, 6, 0) }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }); }
                    if (option == 2) button.Resources["Accent"] = UI.Brush("Melee");
                    UI.SetPressed(button, active); row.Children.Add(button);
                }
                card.Children.Add(row); sheet.Children.Add(UI.Card(card));
            }
            sheet.Children.Add(UI.Text("Current production buttons remain unchanged. No glow, animation or additional color coding is proposed.", 12, "SecondaryText"));
            Save("combat-mode-options.png", PreviewRenderer.Capture(sheet, 660, 340, 2));
        }
        finally { c.SetPreviewSessions(previous); c.Profile.Meter = meter; c.Profile.CombatLab = preferences; lab.Width = 1170; lab.Height = 760; lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); }
    }
}
