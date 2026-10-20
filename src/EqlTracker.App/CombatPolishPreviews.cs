using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class CombatPolishPreviews
{
    internal static void Render(string folder, TrackerController c, DpsWindow lab, QuestWindow quest)
    {
        if (!c.Demo) throw new InvalidOperationException("Polish previews require isolated demo data.");
        Directory.CreateDirectory(Path.Combine(folder, "qa"));
        var savedHealing = EquipmentPositions.Copy(c.Profile.Healing); var meter = EquipmentPositions.Copy(c.Profile.Meter); var preferences = EquipmentPositions.Copy(c.Profile.CombatLab); var group = c.Profile.GroupMembers; string tone = c.Profile.SecondaryText;
        var fight = c.Combat.Encounters.Last(); var events = fight.Events.ToArray(); var heals = fight.Heals.ToArray();
        void Flush() { lab.Refresh(); quest.Refresh(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); lab.UpdateLayout(); }
        void Save(string name, BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file); }
        try
        {
            c.Profile.GroupMembers = [new() { Name = "Shieldwarden", From = fight.Start }, new() { Name = "Lightweaver", From = fight.Start }];
            for (int i = 0; i < 30; i++)
            {
                fight.Events.Add(new() { Id = "preview-group-" + i, Time = fight.Start.AddSeconds(i * 2), Actor = "Shieldwarden", Owner = "Group", ActivityOnly = true, Relevance = "Group", Target = "an ice giant", Ability = i % 2 == 0 ? "bashes" : "slashes", Damage = 150 + i * 4, Kind = "Melee" });
                fight.Heals.Add(new() { Id = "preview-group-heal-" + i, Time = fight.Start.AddSeconds(i * 2), Healer = "Lightweaver", Recipient = c.Profile.Character, RecipientOwner = "Player", Ability = "Greater Healing", Effective = 250 + i * 10, Critical = i % 7 == 0 });
            }
            c.Profile.Meter.Scope = "History"; c.Profile.Meter.EncounterId = fight.Id; c.Profile.Meter.Actor = "Combined"; c.Profile.Meter.Kind = c.Profile.Meter.Target = c.Profile.Meter.Ability = ""; c.Profile.Meter.CriticalOnly = false;
            c.Profile.Healing = new(); c.Profile.SecondaryText = "Bright Frost"; Appearance.ApplySecondaryText("Bright Frost");
            c.SeedPolishOutcomes(fight); c.Save();
            foreach (var (mode, tab, name) in new[] { (CombatLabMode.Damage, CombatLabTab.Breakdown, "combat-polish-damage"), (CombatLabMode.Healing, CombatLabTab.Breakdown, "combat-polish-healing"), (CombatLabMode.Damage, CombatLabTab.Metrics, "ability-inspector"), (CombatLabMode.Healing, CombatLabTab.Metrics, "healing-inspector"), (CombatLabMode.Damage, CombatLabTab.Group, "group-damage"), (CombatLabMode.Healing, CombatLabTab.Group, "group-healing") })
            {
                lab.ShowView(mode, tab); lab.Width = 1170; lab.Height = 760; Flush();
                if (tab == CombatLabTab.Metrics) { var table = Usability.Tree(lab).OfType<DataGrid>().Single(t => t.IsVisible && t.Columns.Any(col => col.SortMemberPath == "MaxHit")); table.SelectedItem = table.Items.Cast<DamageRow>().FirstOrDefault(r => r.Ability == "Spirit Tap") ?? table.Items.Cast<object>().FirstOrDefault(); Flush(); }
                foreach (double scale in new[] { 1d, 1.5, 2, 3 }) Save($"qa/{name}-{scale:0.0}x.png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 1170, 760, scale));
                Save(name + ".png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 1170, 760, 1));
                foreach (double scale in new[] { 1d, 1.5, 2, 3 }) Save($"qa/{name}-minimum-{scale:0.0}x.png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 640, 420, scale));
            }
            var glyphs = new WrapPanel { Background = UI.Brush("SolidSurface") };
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); lab.Width = 1170; lab.Height = 760; Flush();
            var timeline = Usability.Tree(lab).OfType<Timeline>().Single(); timeline.PeakMode = ChartPeakMode.All; lab.MarkAbility("backstabs"); timeline.PreviewCursor(.4);
            foreach (double scale in new[] { 1d, 1.5, 2, 3 }) { timeline.PreviewCursor(.4); Save($"qa/chart-peaks-cursor-{scale:0.0}x.png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 1170, 760, scale)); }
            Save("chart-peaks-cursor.png", PreviewRenderer.Capture((FrameworkElement)lab.Content, 1170, 760, 1)); timeline.ClearCursor(); timeline.PeakMode = c.Profile.CombatLab.Peaks;
            foreach (string ability in new[] { "bashes", "kicks", "flying kicks", "punches", "backstabs", "slashes", "pierces", "crushes", "shoots", "bites", "claws", "cleaves", "unknown" })
            { var glyph = new DamageGlyph { Channel = "Physical", Ability = ability, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left }; var tile = UI.Stack(glyph, UI.Text(ability, 11)); tile.Width = 110; tile.Margin = new(8); glyphs.Children.Add(tile); }
            foreach (double scale in new[] { 1d, 1.5, 2, 3 }) Save($"qa/melee-glyphs-{scale:0.0}x.png", PreviewRenderer.Capture(glyphs, 910, 140, scale)); Save("melee-glyphs.png", PreviewRenderer.Capture(glyphs, 910, 140, 1));
            lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); lab.Width = 1000; lab.Height = 680; Flush();
            var proof = new DrawingVisual(); using (var dc = proof.RenderOpen())
            {
                int y = 0;
                foreach (string choice in Appearance.SecondaryTextChoices)
                {
                    Appearance.ApplySecondaryText(choice); var capture = PreviewRenderer.Capture((FrameworkElement)lab.Content, 1000, 680, 1);
                    foreach (int offset in new[] { 0, 1020 })
                    {
                        dc.DrawRectangle(new SolidColorBrush(offset == 0 ? Color.FromRgb(18, 41, 51) : Color.FromRgb(209, 195, 157)), null, new(offset, y, 1000, 225));
                        for (int i = 0; i < 20; i++) { var b = new SolidColorBrush(i % 2 == 0 ? Color.FromArgb(80, 245, 235, 205) : Color.FromArgb(100, 5, 15, 30)); dc.DrawRectangle(b, null, new(offset + i * 51, y + i % 4 * 28, 90, 225)); }
                        dc.PushClip(new RectangleGeometry(new(offset, y + 25, 1000, 200))); dc.DrawImage(capture, new(offset, y + 25, 1000, 680)); dc.Pop();
                        dc.DrawText(new FormattedText(choice + (offset == 0 ? " · dark busy backdrop" : " · bright busy backdrop"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1), new(offset + 8, y + 3));
                    }
                    y += 235;
                }
            }
            foreach (double scale in new[] { 1d, 1.5, 2, 3 }) { var b = new RenderTargetBitmap((int)(2020 * scale), (int)(705 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); b.Render(proof); Save($"qa/secondary-text-options-{scale:0.0}x.png", b); if (scale == 1) Save("secondary-text-options.png", b); }
        }
        finally { fight.Events.Clear(); fight.Events.AddRange(events); fight.Heals.Clear(); fight.Heals.AddRange(heals); c.Profile.GroupMembers = group; c.Profile.Healing = savedHealing; c.Profile.Meter = meter; c.Profile.CombatLab = preferences; c.Profile.SecondaryText = tone; Appearance.ApplySecondaryText(tone); lab.ShowView(CombatLabMode.Damage, CombatLabTab.Breakdown); c.Save(); }
    }
}
public sealed partial class TrackerController
{
    internal void SeedPolishOutcomes(Encounter fight)
    {
        if (!Demo) throw new InvalidOperationException("Diagnostic fixtures require demo mode.");
        for (int i = 0; i < 3; i++) abilityOutcomes["preview-resist-" + i] = new("preview-resist-" + i, fight.Start.AddSeconds(3 + i * 4), Profile.Character, "an ice giant", "Spirit Tap", "Full resist", "Illustrative verified-format fixture"); outcomeRevision++; outcomesCaughtUp = true;
    }
}
