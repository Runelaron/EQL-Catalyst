using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class ComparisonPanel : StackPanel
{
    public bool AnimateValues { get; set; }
    public Action? FormulaRequested { get; set; }
    private PetPairComparisonView? rawPair;
    public void ShowPair(ItemPairComparison pair)
    {
        if (rawPair != null) Children.Remove(rawPair);
        foreach (UIElement child in Children) child.Visibility = Visibility.Collapsed;
        rawPair = new(pair); Children.Add(rawPair);
    }
    private readonly TextBlock heading = UI.Text("Select an item to compare.", 12, "Accent", true);
    private readonly TextBlock baseline = UI.Text("", 11), quality = UI.Text("", 11, "Muted"), effects = UI.Text("", 11, "Mint");
    private readonly TextBlock arithmetic = UI.Text("", 11, "Mint"), unscored = UI.Text("", 11, "Muted");
    private readonly DataGrid changes = UI.Table(("Stat", "StatLabel", 58, null), ("Current", "Current", 64, "0.##"), ("Candidate", "Candidate", 72, "0.##"), ("Change", "Change", 64, "+0.##;-0.##;0"));
    private readonly DataGrid contributions = UI.Table(("Stat", "Stat", 58, null), ("Change", "Change", 60, "+0.##;-0.##;0"), ("Weight", "Weight", 60, "0.##"), ("Points", "Points", 60, "+0.##;-0.##;0"));
    private readonly Expander explanation;
    private readonly BuildComparisonView buildView = new() { Visibility = Visibility.Collapsed };
    public ComparisonPanel()
    {
        changes.Height = 150; contributions.Height = 100; changes.FontSize = contributions.FontSize = 11; changes.RowHeight = contributions.RowHeight = 24;
        var statStyle = new Style(typeof(TextBlock)); statStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("Stat"))); ((DataGridTextColumn)changes.Columns[0]).ElementStyle = statStyle;
        changes.ToolTip = "All known stat changes, including stats with zero weight in this preset. Current values include the baseline's +rank estimate.";
        contributions.ToolTip = "Each change × its preference weight. Sum these points to reconcile the score change.";
        explanation = new() { Header = "How the score changes", Content = UI.Stack(UI.Text("Points = stat change × preference weight. Zero-weight stats still appear above. Effects are separate; points do not predict DPS.", 11, "Muted"), contributions) };
        Children.Add(heading); Children.Add(baseline); Children.Add(arithmetic); Children.Add(changes); Children.Add(unscored); Children.Add(explanation); Children.Add(effects); Children.Add(quality); Children.Add(buildView);
    }
    public void Show(GearComparison? value, string preset)
    {
        if (rawPair != null) { Children.Remove(rawPair); rawPair = null; }
        bool full = value?.Evaluation != null;
        foreach (UIElement child in Children) child.Visibility = child == buildView ? full ? Visibility.Visible : Visibility.Collapsed : full ? Visibility.Collapsed : Visibility.Visible;
        if (full) { buildView.AnimateValues = AnimateValues; buildView.FormulaRequested = FormulaRequested; buildView.Show(value!, preset); return; }
        if (value == null) { heading.Text = "Select an item to compare."; baseline.Text = arithmetic.Text = unscored.Text = effects.Text = quality.Text = ""; changes.Visibility = explanation.Visibility = Visibility.Collapsed; return; }
        heading.Text = value.CanCompare ? $"{preset} preference: {value.Delta:+0.0;-0.0;0} points" : value.Verdict;
        baseline.Text = $"Candidate: {value.Candidate}\nCurrent: {value.Replaces}\n" + (value.CanCompare ? $"{preset} score {value.BaselineScore:N1} → {value.Score:N1}" : "Comparison unavailable until eligibility and baseline are known.");
        heading.ToolTip = "Preference points reflect the selected weights, not measured or predicted DPS. Gains in scored stats can outweigh losses in unscored stats.";
        quality.Text = "Data quality: " + value.Confidence + "\n" + value.Eligibility;
        quality.ToolTip = "Describes missing or estimated information, not a probability. Ranked stats are estimated at their tier floor; unknown XP and effects remain explicit.";
        effects.Text = value.Effects.Replace("Effects gained: \nEffects lost: ", "Effects: no changes");
        changes.Visibility = explanation.Visibility = value.CanCompare ? Visibility.Visible : Visibility.Collapsed;
        var rows = value.CanCompare ? value.Breakdown.Where(s => s.Change != 0).ToList() : [];
        var scored = rows.Where(s => s.Weight != 0).ToList();
        arithmetic.Text = !value.CanCompare ? "" : string.Join(" · ", scored.Select(s => $"{s.Stat} {s.Points:+0.##;-0.##;0} pts")) + $" = {value.Delta:+0.##;-0.##;0} pts";
        unscored.Text = rows.Any(s => s.Weight == 0) ? "Not scored by " + preset + ": " + string.Join(", ", rows.Where(s => s.Weight == 0).Select(s => s.Stat)) + ". These gains/losses are still shown above." : "";
        changes.Height = Math.Clamp(rows.Count * 24 + 30, 54, 246);
        UI.SetRows(changes, rows); UI.SetRows(contributions, scored);
        Usability.Tooltips(this);
    }
    public void ExplainInventory(ItemDefinition item, int rank)
    {
        Show(null, ""); heading.Text = item.Name + " +" + rank;
        baseline.Text = "No compatible replacement is available with the current selection.";
        quality.Text = "Choose Best compatible under Candidate filters, or refresh missing item metadata. Review slot and class restrictions in item details. Inventory ownership does not prove what the pet wears."; quality.Visibility = Visibility.Visible;
        effects.Text = string.Join("\n", item.Effects); effects.Visibility = Visibility.Visible;
    }
    public void ExplainReason(string reason, Action formula)
    {
        Show(null, ""); heading.Text = reason;
        baseline.Text = "";
        var button = UI.Button("ⓘ Rating details", formula); button.ToolTip = "Based on current formula — Click for more details.";
        if (rawPair != null) Children.Remove(rawPair);
        var old = Children.OfType<Button>().FirstOrDefault(); if (old != null) Children.Remove(old);
        Children.Add(button);
    }
}
