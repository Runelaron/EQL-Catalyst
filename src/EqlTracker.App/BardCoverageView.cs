using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class CombatGroupPanel
{
    private UIElement memberDetail = null!;
    private GroupPaneLayout groupPanes = null!;
    private readonly StackPanel songInspector = new() { Visibility = Visibility.Collapsed };
    private string songViewKey = "";
    private Expander? songSetReview;
    private void ToggleSongCoverage(bool show)
    { memberDetail.Visibility = show ? Visibility.Collapsed : Visibility.Visible; songInspector.Visibility = show ? Visibility.Visible : Visibility.Collapsed; groupPanes.FocusDetails(show); songViewKey = ""; if (show) { RefreshSongCoverage(); Dispatcher.BeginInvoke(() => { if(songInspector.Children.Count>0)((FrameworkElement)songInspector.Children[0]).BringIntoView(); }); } }
    private void RefreshSongCoverage()
    {
        var view = controller.SongCoverage(); string key = PetComparisonEngine.Key(new{view.AuraSongs,view.Conditions,view.Suggestions,view.Coverage,Setup=controller.IntegratedSnapshot().Bard});
        if (key == songViewKey) return; songViewKey = key;
        songInspector.Children.Clear(); songInspector.Children.Add(UI.Text("Song coverage", 15, "Accent", true));
        songInspector.Children.Add(UI.Button("Member details", () => ToggleSongCoverage(false)));
        if(songSetReview==null) { songSetReview = FocusedLayout.Disclosure("Review my song set", new StackPanel(), "Preserves your choices; saving changes Catalyst’s setup only"); songSetReview.Expanded += (_,_)=>{if(songSetReview.Content is StackPanel {Children.Count:0})songSetReview.Content=BuildSongEditor();}; }
        songInspector.Children.Add(songSetReview);
        var historical = controller.IntegratedSnapshot().Coverage.OrderByDescending(c=>c.At).Take(12).ToArray();
        if(historical.Length>0) songInspector.Children.Add(FocusedLayout.Disclosure("Observed song evidence", BuildCoverageReview(historical), "Review successful recipient evidence and its supported coverage window; attempts are not coverage"));
        songInspector.Children.Add(UI.Text(view.AuraSongs.Count > 0 ? "Aura selection · " + string.Join(" · ", view.AuraSongs.Select(s => $"gem {s.Gem}: {s.Identity.Name}")) : "Aura selection unknown", 11));
        foreach (string condition in view.Conditions) songInspector.Children.Add(UI.Text(condition, 11, "SecondaryText"));
        foreach (var coverage in view.Coverage) songInspector.Children.Add(UI.Text($"{coverage.Caster} · {coverage.Song.Name} +{coverage.Song.UpgradeRank?.ToString() ?? "?"}\n{coverage.Recipient} · {coverage.At:HH:mm:ss} · supported until {coverage.Until:HH:mm:ss}", 11));
        void Suggest(StackPanel pane, SongRecommendation suggestion)
        {
            pane.Children.Add(UI.Text($"Gem {suggestion.Gem}: {suggestion.Current.Name} → {suggestion.Replacement.Name}", 12, "Mint", true)); pane.Children.Add(UI.Text(suggestion.Explanation, 11));
            pane.Children.Add(UI.Button("Save planned swap", () => { controller.SaveSongPlan(suggestion); songViewKey = ""; RefreshSongCoverage(); }));
        }
        if (view.Suggestions.Count == 0) songInspector.Children.Add(UI.Text("No defensible swap yet. Your chosen songs stay in place.", 12));
        else { if (view.Suggestions.Count > 1) songInspector.Children.Add(UI.Text("Useful options · no universal best across different benefits", 11, "SecondaryText")); Suggest(songInspector, view.Suggestions[0]); if (view.Suggestions.Count > 1) { var alternatives = new StackPanel(); foreach (var suggestion in view.Suggestions.Skip(1)) Suggest(alternatives, suggestion); songInspector.Children.Add(FocusedLayout.Disclosure("Other useful options", alternatives, "At most two alternatives")); } }
        foreach (var swap in controller.IntegratedSnapshot().Bard.Planned) songInspector.Children.Add(UI.Text($"Planned gem {swap.Gem}: {swap.Before.Name} → {swap.After.Name}\nSaved in Catalyst; game gems and AA are unchanged.", 11, "SecondaryText"));
    }
    private UIElement BuildSongEditor()=>new BardSongEditor(controller,()=>{songViewKey="";RefreshSongCoverage();});
}
