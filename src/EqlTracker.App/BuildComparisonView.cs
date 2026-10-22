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
        updates.Add(() => { string next = value(); text.Visibility = next.Length == 0 ? Visibility.Collapsed : Visibility.Visible; NumericTransition.Set(text, next, AnimateValues); if (liveColor != null && liveColor() is var selected && selected != lastColor) { lastColor = selected; text.SetResourceReference(TextBlock.ForegroundProperty, selected); } });
        return text;
    }
    private Button Info()
    {
        var button = UI.Button("ⓘ", () => FormulaRequested?.Invoke()); button.ToolTip = "Based on current formula — Click for more details."; button.Padding = new(0); button.Width=button.Height=22; button.MinWidth=button.MinHeight=0; button.Margin=new(6,0,0,0); button.VerticalAlignment=VerticalAlignment.Center; return button;
    }
    private Grid Heading(TextBlock text, bool information)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new(0);grid.Children.Add(text);
        if(information){var info=Info();Grid.SetColumn(info,1);grid.Children.Add(info);}return grid;
    }
    public void Show(GearComparison comparison, string selectedRole)
    {
        if (comparison.Evaluation == null) return;
        using var measure = UpdateDiagnostics.Measure("Rating values apply");
        current = comparison; role = selectedRole;
        string key = Result.Pet + "|" + (Result.PetDefense!=null) + "|" + (FormulaRequested != null);
        if (shape != key) { shape = key; updates.Clear(); Children.Clear(); Build(); }
        foreach (var update in updates) update();
    }
    private void Build()
    {
        bool pet = Result.Pet; bool balanced=pet&&Result.PetDefense!=null;
        if (!pet || balanced) { var title = Heading(Live(() => "Upgrade rating · " + role, 12, "Accent", true), FormulaRequested != null); Children.Add(title); }
        Children.Add(Live(() => Result.IsHypothetical ? "Hypothetical · " + string.Join("; ", Result.Assumptions) : "", color:"Melee", bold:true));
        Children.Add(Live(() => "Replaces: " + current.Replaces));

        Children.Add(Live(() => Result.Eligibility == "Eligible" ? "" : Result.Eligibility, 12, "Violet", true));
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = pet ? 1 : 2, Margin = new(0,4,0,4) };
        int count = balanced ? 2 : pet ? 1 : 2;
        for (int index = 0; index < count; index++)
        {
            int slot = index; ContextRating Context() => slot == 0 ? Result.Unbuffed : balanced ? Result.PetDefense! : Result.Buffed;
            var heading = Heading(Live(() => balanced ? slot==0 ? "Damage preference" : "Defense preference" : pet ? "Upgrade rating · " + role : Context().Name, 11, "Foreground", true), pet && !balanced && FormulaRequested != null);
            var card = UI.Card(UI.Stack(heading,
                Live(() => Result.Status == "Conditional" ? "Conditional" : Context().Delta?.ToString("+0.0;-0.0;0") ?? "—", 18, "Mint", true, () => Context().Delta < 0 ? "Violet" : "Mint"),
                Live(() => Context().CurrentScore.HasValue ? $"{Context().CurrentScore:N1} → {Context().CandidateScore:N1} pts" : Context().MarginalEstimate.HasValue ? "Marginal estimate only" : Context().Quality, 10)));
            card.Margin = new(0,0,4,0); cards.Children.Add(card);
            var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 410 }; card.ToolTip = new ToolTip { Content = tip };
            updates.Add(() => tip.Text = Context().Quality + "\n" + string.Join('\n', Context().Notes) + (Result.Status=="Conditional" && Context().Delta is {} estimate ? "\nConditional preference estimate: " + RatingExplanation.Signed(estimate) + " points; no confirmed winner." : ""));
        }
        Children.Add(cards);
        Children.Add(Live(() => Result.WeaponNote, 11, "Muted")); Children.Add(Live(() => Result.ActivationNote, 11, "Muted"));
        List<BuildContribution> Raw() => Result.Unbuffed.Changes.Count > 0 ? Result.Unbuffed.Changes : Result.Buffed.Changes;
        foreach (bool gains in new[] { true, false })
        {
            bool positive = gains;
            Children.Add(Live(() => Raw().Any(c => positive ? c.ItemChange > 0 : c.ItemChange < 0) ? positive ? "Gains" : "Losses" : "", 12, positive ? "Mint" : "Violet", true));
            foreach (string category in ItemStatGroups.Categories)
            {
                string group=category;
                var line=Live(()=>{var values=RatingExplanation.Ordered(Raw()).Where(c=>ItemStatGroups.Category(c.Stat)==group&&(positive?c.ItemChange>0:c.ItemChange<0)).Select(c=>RatingExplanation.GroupedLabel(c.Stat)+" "+RatingExplanation.Signed(c.ItemChange)+(c.Stat=="Delay"?c.ItemChange<0?" (faster)":" (slower)":""));string text=string.Join(" · ",values);return text.Length==0?"":group+": "+text;},11,"Foreground");
                line.ToolTip="Known changes in AC / Resources / Weapons / Stats / Resists order. See rating details for weights and uncertainty.";Children.Add(line);
            }
        }
        for (int index = 0; index < count; index++)
        {
            int slot = index; ContextRating Context() => slot == 0 ? Result.Unbuffed : balanced ? Result.PetDefense! : Result.Buffed;
            Children.Add(Live(() => (balanced ? slot==0 ? "Damage preference" : "Defense preference" : pet ? "Equipment" : Context().Name) + ": " + RatingExplanation.Status(Context()), 10, "Muted"));
            Children.Add(Live(() => Context().Changes.Any(c => c.Uncertain) ? "Cap-crossing loss is uncertain: hidden overcap stats are unknown." : "", 11, "Violet"));
            Children.Add(Live(() => Context().Delta.HasValue ? RatingExplanation.Equation(Context()) : "", 11, "Mint"));
            var detail = new Expander { Header = (balanced ? slot==0 ? "Damage preference" : "Defense preference" : pet ? "Equipment" : Context().Name) + " · stats, caps & arithmetic" }; DataGrid? table = null; TextBlock? notes = null;
            void ApplyDetails()
            {
                if (table == null || !detail.IsExpanded) return;
                table.Height = Math.Clamp(Context().Changes.Count * 24 + 28, 60, 190); UI.SetRows(table, RatingExplanation.Ordered(Context().Changes).ToList()); notes!.Text = string.Join('\n', Context().Notes);
            }
            detail.Expanded += (_, _) =>
            {
                if (table == null)
                {
                    table = UI.Table(("Stat","Stat",60,null),("Current","Current",65,"0.##"),("Candidate","Candidate",72,"0.##"),("Item Δ","ItemChange",62,"+0.##;-0.##;0"),("Effective Δ","UsableChange",72,"+0.##;-0.##;0"),("Weight","Weight",55,"0.##"),("Points","Points",60,"+0.##;-0.##;0"));
                    table.FontSize = 10; var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("Explanation"))); table.RowStyle = rowStyle;
                    notes = UI.Text("",10,"Muted"); detail.Content = UI.Stack(table,notes); Usability.Tooltips(detail);
                }
                ApplyDetails();
            };
            updates.Add(ApplyDetails); Children.Add(detail);
        }
        Children.Add(Live(() => Result.EffectsGained.Count > 0 ? "Effects gained: " + string.Join(", ",Result.EffectsGained) : "",11,"Mint"));
        Children.Add(Live(() => Result.EffectsLost.Count > 0 ? "Effects lost: " + string.Join(", ",Result.EffectsLost) : "",11,"Violet"));
        Children.Add(FocusedLayout.Disclosure("About these ratings",UI.Text("Points reflect your role weights, not predicted DPS, proc rate or damage reduction. Item ranks are tier-floor estimates.",10,"Muted"),"Preference estimates; confirmed mechanics and observed performance remain separate."));
        Usability.Tooltips(this);
    }
}
