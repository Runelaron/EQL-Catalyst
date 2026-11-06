using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;


internal static class IntegratedReview
{
    internal static async Task Run(string folder, TrackerController controller, QuestWindow tracker, DpsWindow lab)
    {
        controller.SeedIntegratedReview(); Directory.CreateDirectory(folder);
        var settings = new SettingsWindow(controller); var checks = new List<string>();
        var themes=new Dictionary<string,IReadOnlyDictionary<string,string>>();
        foreach (var window in new Window[] { tracker, lab, settings }) { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -15000; window.Show(); }
        foreach (var theme in ThemeCatalog.Choices.Skip(1))
        {
            controller.Profile.Theme = theme.Id; Appearance.Apply(theme.Id);
            themes[theme.Id]=Appearance.PhoneTokens;
            foreach (double scale in new[] { 1d, 1.5, 2d, 3d })
            {
                async Task Capture(Window window, string name)
                {
                    window.Width = 640; window.Height = 420; window.UpdateLayout(); await Task.Delay(50); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var bitmap = PreviewRenderer.Capture((FrameworkElement)window.Content, 640, 420, scale); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(folder, $"{theme.Id}-{name}-{scale:0.0}x.png")); encoder.Save(output);
                    if (bitmap.PixelWidth != (int)(640 * scale)) throw new InvalidOperationException("Render scale mismatch"); checks.Add(theme.Id + "/" + name + "/" + scale);
                }
                tracker.OpenSessionLoot("review-session"); tracker.Refresh(); await Capture(tracker, "loot");
                tracker.ShowView(TrackerTab.Gear); tracker.Refresh(); await Capture(tracker,"gear");
                tracker.ShowView(TrackerTab.Pet); tracker.Refresh(); await Capture(tracker,"pet");
                lab.Refresh(); await Capture(lab, "history");
                lab.OpenSongCoverageReview(); await Capture(lab, "bard");
                settings.SelectSection(SettingsSection.Phone); await Capture(settings, "phone");
            }
        }
        var view = new CompatibilityInspector(); var context = new EvaluationContext { SummonedPet = true, PetLevel = 50, ObservedOrdinaryAttackSpeed = 171, SpeedEvidence = EvidenceQuality.UserConfirmed };
        EffectInput Haste(int n) => new() { Identity = new("cloak", "Cloak", null), Family = "Haste", Channel = EffectChannel.WornHaste, Amount = n, Equipment = true, Quality = EvidenceQuality.UserConfirmed, Source = "Synthetic inspected item" };
        view.Show(BuildCompatibility.Compare(context, [Haste(20)], [Haste(30)], [])); foreach (var expander in view.Children.OfType<Expander>()) expander.IsExpanded = true;
        Save(Path.Combine(folder, "compatibility-expanded.png"), PreviewRenderer.Capture(view, 440, 480, 1));
        var source = new ItemSource { Zone = "Solusek's Eye (Group)", Mob = "a named camp guardian" }; var notebook = new CampNotebookView(controller, source) { IsExpanded = true };
        // Expanded normally fires after insertion; raising here uses the same component path.
        notebook.RaiseEvent(new RoutedEventArgs(Expander.ExpandedEvent)); Save(Path.Combine(folder, "camp-expanded.png"), PreviewRenderer.Capture(notebook, 440, 580, 1));
        Json.Write(Path.Combine(folder, "render-checks.json"), new { ActualWpfComponents = true, MinimumWidth = 640, MinimumHeight = 420, Scales = new[] { 1, 1.5, 2, 3 }, Checks = checks });
        controller.Profile.Theme="coldain";Appearance.Apply("coldain");
        File.WriteAllText(Path.Combine(folder,"phone-projection.json"),System.Text.Json.JsonSerializer.Serialize(controller.BuildPhoneProjection(42),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web){WriteIndented=true}));
        File.WriteAllText(Path.Combine(folder,"phone-themes.json"),System.Text.Json.JsonSerializer.Serialize(themes));
        settings.Close();
    }
    private static void Save(string path, System.Windows.Media.Imaging.BitmapSource image) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(path); encoder.Save(output); }
}
public sealed partial class CombatGroupPanel { internal void OpenSongCoverageReview() => ToggleSongCoverage(true); }
public sealed partial class DpsWindow { internal void OpenSongCoverageReview() { ShowView(CombatLabMode.Damage,CombatLabTab.Group);UpdateLayout();group.OpenSongCoverageReview(); } }
