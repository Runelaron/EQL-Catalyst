using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed class BuildComparisonView : StackPanel
{
    public bool AnimateValues { get; set; }
    public Action? FormulaRequested { get; set; }
    private GearComparison current = null!;
    private string role = "", shape = "";
    private LoadoutComparison Result => current.Evaluation!;
    private readonly List<Action> updates = [];
    private TextBlock Live(Func<string> value, double size = 11, string color = "Foreground", bool bold = false, Func<string>? liveColor = null)
    {
        var text = UI.Text("", size, color, bold);
        string lastColor = color;
        updates.Add(() => { NumericTransition.Set(text, value(), AnimateValues); if (liveColor != null && liveColor() is var selected && selected != lastColor) { lastColor = selected; text.SetResourceReference(TextBlock.ForegroundProperty, selected); } });
        return text;
    }
    private Button Info()
    {
        var button = UI.Button("ⓘ", () => FormulaRequested?.Invoke()); button.ToolTip = "Based on current formula — Click for more details."; button.Padding = new(3,0,3,0); return button;
    }
    public void Show(GearComparison comparison, string selectedRole)
    {
        if (comparison.Evaluation == null) return;
        using var measure = UpdateDiagnostics.Measure("Rating values apply");
        current = comparison; role = selectedRole;
        string key = Result.Pet + "|" + (FormulaRequested != null);
        if (shape != key) { shape = key; updates.Clear(); Children.Clear(); Build(); }
        foreach (var update in updates) update();
    }
    private void Build()
    {
        bool pet = Result.Pet;
        if (!pet) { var title = UI.Row(Live(() => "Upgrade rating · " + role, 12, "Accent", true)); if (FormulaRequested != null) title.Children.Add(Info()); Children.Add(title); }
        Children.Add(Live(() => Result.IsHypothetical ? "Hypothetical · " + string.Join("; ", Result.Assumptions) : "", color:"Melee", bold:true));
        Children.Add(Live(() => "Replaces: " + current.Replaces));
        Children.Add(Live(() => Result.WeaponNote, 11, "Muted")); Children.Add(Live(() => Result.ActivationNote, 11, "Muted"));
        Children.Add(Live(() => Result.Eligibility == "Eligible" ? "" : Result.Eligibility, 12, "Violet", true));
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = pet ? 1 : 2, Margin = new(0,4,0,4) };
        int count = pet ? 1 : 2;
        for (int index = 0; index < count; index++)
        {
            int slot = index; ContextRating Context() => slot == 0 ? Result.Unbuffed : Result.Buffed;
            var heading = UI.Row(Live(() => pet ? "Upgrade rating · " + role : Context().Name, 11, "Foreground", true)); if (pet && FormulaRequested != null) heading.Children.Add(Info());
            var card = UI.Card(UI.Stack(heading,
                Live(() => Context().Delta?.ToString("+0.0;-0.0;0") ?? (Result.Status == "Conditional" ? "Conditional" : "—"), 18, "Mint", true, () => Context().Delta < 0 ? "Violet" : "Mint"),
                Live(() => Context().CurrentScore.HasValue ? $"{Context().CurrentScore:N1} → {Context().CandidateScore:N1} pts" : Context().MarginalEstimate.HasValue ? "Marginal estimate only" : Context().Quality, 10)));
            card.Margin = new(0,0,4,0); cards.Children.Add(card);
            var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 410 }; card.ToolTip = new ToolTip { Content = tip };
            updates.Add(() => tip.Text = Context().Quality + "\n" + string.Join('\n', Context().Notes));
        }
        Children.Add(cards);
        List<BuildContribution> Raw() => Result.Unbuffed.Changes.Count > 0 ? Result.Unbuffed.Changes : Result.Buffed.Changes;
        foreach (string category in ItemStatGroups.Categories)
        {
            string group=category;
            var line=Live(()=>{var values=Raw().Where(c=>c.Stat!="Delay"&&c.ItemChange!=0&&ItemStatGroups.Category(c.Stat)==group).OrderBy(c=>ItemStatGroups.Order(c.Stat)).ThenBy(c=>c.Stat).Select(c=>(group=="AC"?"":c.Stat.Replace("Resist","")+" ")+c.ItemChange.ToString("+0.##;-0.##;0"));string text=string.Join(" · ",values);return text.Length==0?"":group+": "+text;},11,"Foreground");
            line.ToolTip=group=="Res"?"Resources: HP, mana, vitality and direct resource additions. Positive values are gains; negative values are losses.":"Known item changes in classic stat order. Positive values are gains; negative values are losses.";Children.Add(line);
        }
        Children.Add(Live(() => Raw().FirstOrDefault(c => c.Stat == "Delay") is { } delay ? $"Weapon delay change: {delay.ItemChange:+0.##;-0.##;0} · lower is faster; inspect the affected hand." : "", 10, "Muted"));
        for (int index = 0; index < count; index++)
        {
            int slot = index; ContextRating Context() => slot == 0 ? Result.Unbuffed : Result.Buffed;
            Children.Add(Live(() => (pet ? "Equipment" : Context().Name) + ": " + Context().Quality, 10, "Muted"));
            Children.Add(Live(() => Context().Changes.Any(c => c.Uncertain) ? "Cap-crossing loss is uncertain: hidden overcap stats are unknown." : "", 11, "Violet"));
            Children.Add(Live(() => Context().Delta.HasValue ? string.Join(" · ", Context().Changes.GroupBy(c => c.Category).Select(g => g.Key + " " + g.Sum(c => c.Points).ToString("+0.##;-0.##;0"))) : "", 11, "Mint"));
            var detail = new Expander { Header = (pet ? "Equipment" : Context().Name) + " · stats, caps & arithmetic" }; DataGrid? table = null; TextBlock? notes = null;
            void ApplyDetails()
            {
                if (table == null || !detail.IsExpanded) return;
                table.Height = Math.Clamp(Context().Changes.Count * 24 + 28, 60, 190); UI.SetRows(table, Context().Changes); notes!.Text = string.Join('\n', Context().Notes);
            }
            detail.Expanded += (_, _) =>
            {
                if (table == null)
                {
                    table = UI.Table(("Stat","Stat",60,null),("Current","Current",65,"0.##"),("Candidate","Candidate",72,"0.##"),("Item Δ","ItemChange",62,"+0.##;-0.##;0"),("Usable Δ","UsableChange",72,"+0.##;-0.##;0"),("Weight","Weight",55,"0.##"),("Points","Points",60,"+0.##;-0.##;0"));
                    table.FontSize = 10; var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("Explanation"))); table.RowStyle = rowStyle;
                    notes = UI.Text("",10,"Muted"); detail.Content = UI.Stack(table,notes); Usability.Tooltips(detail);
                }
                ApplyDetails();
            };
            updates.Add(ApplyDetails); Children.Add(detail);
        }
        Children.Add(Live(() => Result.EffectsGained.Count > 0 ? "Effects gained: " + string.Join(", ",Result.EffectsGained) : "Effects gained: none",11,"Mint"));
        Children.Add(Live(() => Result.EffectsLost.Count > 0 ? "Effects lost: " + string.Join(", ",Result.EffectsLost) : "",11,"Violet"));
        Children.Add(FocusedLayout.Disclosure("About these ratings",UI.Text("Points reflect your role weights, not predicted DPS, proc rate or damage reduction. Item ranks are tier-floor estimates.",10,"Muted"),"Preference estimates; confirmed mechanics and observed performance remain separate."));
        Usability.Tooltips(this);
    }
}
