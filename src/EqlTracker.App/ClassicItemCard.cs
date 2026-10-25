using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class ClassicItemCard : Border
{
    private ItemDefinition? previous;
    private int previousRank = -1;
    private string previousInstance = "";
    private bool previousPet;
    internal ClassicItemCard(ItemDefinition item, int rank, InventoryEntry? instance = null, bool pet = false)
    {
        Update(item, rank, instance, false, pet);
    }
    internal void Update(ItemDefinition item, int rank, InventoryEntry? instance, bool animate, bool pet = false)
    {
        string instanceKey = PhysicalItemChoices.Identity(instance);
        if (ReferenceEquals(item, previous) && previousRank == rank && previousInstance == instanceKey && previousPet == pet) return;
        previous = item; previousRank = rank; previousInstance = instanceKey; previousPet = pet;
        using var measure = UpdateDiagnostics.Measure("Classic item values apply");
        SetResourceReference(BackgroundProperty, "DetailSurface"); SetResourceReference(BorderBrushProperty, "Border");
        BorderThickness = new(1); Padding = new(7); Margin = new(0, 4, 0, 5);
        var text = Child as StackPanel ?? new StackPanel(); int line = 0;
        void Line(string value, string color = "Foreground")
        {
            if (value.Length == 0) return;
            if (line < text.Children.Count && text.Children[line] is TextBlock block) { NumericTransition.Set(block, value, animate); if (block.Tag?.ToString() != color) { block.SetResourceReference(TextBlock.ForegroundProperty, color); block.Tag = color; } }
            else { if (line < text.Children.Count) text.Children.RemoveAt(line); var added = UI.Text(value,11,color); added.Tag = color; text.Children.Insert(line, added); }
            line++;
        }
        void Fields(IEnumerable<string> values) {
            var row = line < text.Children.Count ? text.Children[line] as WrapPanel : null;
            if (row == null) { if (line < text.Children.Count) text.Children.RemoveAt(line); row = new WrapPanel(); text.Children.Insert(line, row); }
            var fields = values.Where(v => v.Length > 0).ToArray();
            for (int i = 0; i < fields.Length; i++) {
                TextBlock block; if (i < row.Children.Count) block = (TextBlock)row.Children[i]; else { block = UI.Text("",11); block.TextWrapping = TextWrapping.NoWrap; block.Margin = new(0,0,10,3); row.Children.Add(block); }
                NumericTransition.Set(block, fields[i], animate);
            }
            while(row.Children.Count > fields.Length) row.Children.RemoveAt(row.Children.Count-1);
            line++;
        }
        var projection = RankedItemStats.Resolve(item,rank); var stats = projection.Stats;
        string Number(string key, string label = "") => stats.TryGetValue(key, out double value) ? (label.Length > 0 ? label : key) + ": " + value.ToString("0.###") : "";
        string Signed(string key, string label = "") => stats.TryGetValue(key, out double value) ? (label.Length > 0 ? label : key) + ": " + value.ToString("+0.##;-0.##;0") : "";
        string Join(params string[] values) => string.Join("  ", values.Where(v => v.Length > 0));
        Line("Type: " + PetItemRules.Type(item), "Accent");
        Line(string.Join(" ", item.Display.Flags));
        if (item.Slots.Count > 0) Line("Slot: " + ItemPresentation.Slots(item.Slots));
        Line(Join(item.Display.Skill.Length > 0 ? "Skill: " + item.Display.Skill : "", Number("Delay", "Atk Delay")));
        Line(Join(Number("Damage", "DMG"), Number("Ratio")));
        Line(Number("AC"));
        Fields(new[] { "HP", "Mana", "Endurance", "STR", "DEX", "STA", "CHA", "WIS", "INT", "AGI", "ATK", "Haste" }.Select(s => Signed(s, s == "Mana" ? "MANA" : s == "Endurance" ? "END" : s)));
        Fields(new[] { "Fire", "Cold", "Magic", "Disease", "Poison", "Corruption", "Void" }.Select(s => Signed("Resist" + s, "SV " + s.ToUpperInvariant())));
        if (projection.Accuracy is {} accuracy) Line("Accuracy: +"+accuracy.ToString("0.##")+"% ("+(projection.Kind==ItemStatEvidenceKind.ClientObserved ? "observed" : "rank rule")+")");
        if (projection.ObservedDamageBonus is {} damageBonus) Line("DMG Bonus: "+damageBonus.ToString("0.##")+" (inspection only; not Pet ratio)","Muted");
        foreach (string effect in ItemEffects.Active(item, instance?.TransferredOutEffects)) Line((pet ? "Catalog effect (not selected): " : "Effect: ") + effect, pet ? "Muted" : "Mint");
        IEnumerable<InventoryEntry> selectedEffects = instance?.Augments ?? [];
        var conflictingTypes = selectedEffects.GroupBy(e => ExaltationPlanner.EffectType(e.EffectText)).Where(g => PetEffectPolicy.Recognized(g.Key) && g.Count()>1).Select(g=>g.Key).ToHashSet();
        if (pet) selectedEffects = selectedEffects.OrderBy(e => ExaltationPlanner.EffectType(e.EffectText) == "Proc" && (ItemEquipmentIcons.IsWeapon(item) || item.Stats.GetValueOrDefault("Delay")>0) ? -1 : Array.IndexOf(new[]{"Focus","Click","Worn","Proc"},ExaltationPlanner.EffectType(e.EffectText)) is var order && order>=0 ? order : 4);
        foreach (var effect in selectedEffects)
        {
            string type = ExaltationPlanner.EffectType(effect.EffectText);
            string locked = type != "Unknown" && rank < ExaltationPlanner.Unlock(type) ? " · locked at this preview rank; requires +" + ExaltationPlanner.Unlock(type) : "";
            if (pet && conflictingTypes.Contains(type)) locked = " · inactive; conflicting " + type + " assignments";
            Line("Exaltation: " + (effect.EffectText.Length > 0 ? effect.EffectText : effect.Name) + locked, locked.Length > 0 ? "Melee" : "Mint");
        }
        if (item.RequiredLevel > 0) Line("Required level: " + item.RequiredLevel);
        if (item.EffectActivationLevel is { } level) Line("Effect activation: level " + level, "Mint");
        Line(Join(projection.Weight is { } weight ? (projection.WeightIsBase ? "WT (base): " : "WT: ") + weight.ToString("0.0") : "", item.Display.Range is { } range ? "Range: " + range : "", item.Display.Size.Length > 0 ? "Size: " + item.Display.Size : ""));
        if (projection.WeaponUncertain) Line("Weapon stats incomplete: damage or delay missing", "Violet");
        if (item.Classes.Count > 0) Line("Class: " + string.Join(" ", item.Classes));
        if (item.Races.Count > 0) Line("Race: " + string.Join(" ", item.Races));
        if (item.Deity.Length > 0) Line("Deity: " + item.Deity);
        if (ItemPresentation.ExpansionNote(item).Length > 0) Line(ItemPresentation.ExpansionNote(item), "Muted");
        if (item.ContentAvailability == ContentAvailability.Future) Line("Future content · " + item.AvailabilityEvidence, "Melee");
        while (text.Children.Count > line) text.Children.RemoveAt(text.Children.Count - 1);
        if (Child == null) Child = text;
        ToolTip = "Known item facts at +" + rank + ".\n" + projection.Evidence + "\n" + (item.Display.Source.Length > 0 ? item.Display.Source + " revision " + item.Display.Revision : "Display metadata incomplete; undocumented facts are omitted.")+"\n"+string.Join("\n",PublicContentNotes.Item(item.Key));
    }
}

internal sealed class ItemDetailsInspector : StackPanel
{
    internal bool PetContext { get; set; }
    private readonly ItemHeaderIcons headerIcons;
    private readonly TextBlock title = UI.Text("Select an item to view its stats.", 16, "Accent", true);
    internal Button ResetPreview { get; private set; } = null!;
    private readonly ContentControl facts = new();
    private readonly TextBlock pending = UI.Text("", 10, "Muted");
    private readonly ComboBox copies = new() { DisplayMemberPath = "Label", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
    private Action<InventoryEntry?>? copyChanged;
    private readonly ItemAcquisitionView acquisition = new();
    private readonly CompatibilityInspector compatibility = new();
    private readonly OtherHoldingsView otherHoldings = new();
    private string copiesKey = "";
    private sealed record CopyChoice(string Label, InventoryEntry? Instance) { public override string ToString() => Label; }
    internal bool AnimateValues { get; set; }
    internal event Action? PreviewCommitted;
    internal ComparisonPanel Rating => rating;
    private string itemKey = "";
    internal readonly Slider RankSlider = new() { Minimum = 0, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1, MinWidth = 100, ToolTip = "Preview this selected item at +0–10. Saved inventory ranks and other candidate rows are unchanged." };
    private readonly ComparisonPanel rating = new();
    private readonly StackPanel rankArea;
    private bool updating;
    private Action<int?>? rankChanged;
    internal ItemDetailsInspector()
    {
        RankSlider.Style = RankSliderStyles.Style(3);
        var reset = ResetPreview = UI.Button("Reset preview", () => rankChanged?.Invoke(null)); reset.ToolTip = "Restore this candidate's recorded/list rank without editing inventory.";
        reset.MinHeight=0; reset.Height=22; reset.Padding=new(7,0,7,0); reset.Margin=new(6,0,0,0); reset.FontSize=11;reset.VerticalAlignment=VerticalAlignment.Center;
        var previewRow=new Grid();previewRow.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});previewRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var previewTitle=UI.Label("Preview +Rank");previewTitle.VerticalAlignment=VerticalAlignment.Center;previewTitle.Margin=new(0);Grid.SetColumn(reset,1);previewRow.Children.Add(previewTitle);previewRow.Children.Add(reset);
        rankArea = UI.Stack(previewRow, RankSlider);
        copies.Visibility = Visibility.Collapsed;
        copies.SelectionChanged += (_, _) => { if (!updating && copies.SelectedItem is CopyChoice copy) copyChanged?.Invoke(copy.Instance); };
        headerIcons=new(title);Children.Add(headerIcons); Children.Add(copies); Children.Add(facts); Children.Add(acquisition); Children.Add(otherHoldings); Children.Add(rankArea); Children.Add(pending); Children.Add(compatibility); Children.Add(rating);
        RankSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => PreviewCommitted?.Invoke()));
        RankSlider.PreviewKeyUp += (_, _) => PreviewCommitted?.Invoke();
        RankSlider.ValueChanged += (_, _) => { if (!updating) rankChanged?.Invoke((int)RankSlider.Value); };
        Clear();
    }
    internal void Clear() { headerIcons.Clear(); copiesKey = ""; itemKey = ""; pending.Text = ""; copies.Visibility = Visibility.Collapsed; title.Text = "Select an item to view its stats and comparison."; facts.Content = null; acquisition.Visibility = rankArea.Visibility = rating.Visibility = compatibility.Visibility = otherHoldings.Visibility = Visibility.Collapsed; }
    internal void SetCompatibility(CompatibilityComparison? result) => compatibility.Show(result);
    internal void SetOwnership(ItemDefinition item, InventorySnapshot inventory) { headerIcons.Show(item, ItemOwnership.For(item, inventory)); acquisition.Visibility=Visibility.Visible;if(Application.Current is App app){acquisition.Show(item,app.Controller.Catalog,inventory);otherHoldings.Show(app.Controller,item);} }
    internal void ShowItem(ItemDefinition item, int rank, int listRank, bool preview, InventoryEntry? instance, GearComparison? comparison, string role, ItemPairComparison? pair, string reason, Action<int?> changed, Action formula)
    {
        rankChanged = changed; updating = true;
        bool sameItem = itemKey == item.Key; itemKey = item.Key;
        title.Text = item.Name + (rank > 0 ? " +" + rank : "");
        headerIcons.Show(item, new(false, "", false, null));
        bool pet = PetContext || comparison?.Evaluation?.Pet == true || pair?.Pet == true;
        if (sameItem && facts.Content is ClassicItemCard card) card.Update(item, rank, instance, AnimateValues, pet); else facts.Content = new ClassicItemCard(item, rank, instance, pet);
        RankSlider.Value = Math.Clamp(rank, 0, 10);
        ResetPreview.IsEnabled = rank != listRank;
        ResetPreview.ToolTip = rank == listRank ? "Already at the selected actual/list rank (+"+listRank+")." : "Restore the selected actual/list rank (+"+listRank+") without editing inventory.";
        RankSlider.ToolTip = "Temporary +0–10 preview. Baseline: +"+listRank+". Recorded inventory ranks are unchanged.";
        updating = false; rankArea.Visibility = rating.Visibility = Visibility.Visible;
        rating.FormulaRequested = formula; rating.AnimateValues = AnimateValues;
        bool waiting = reason.StartsWith("Updating"); pending.Text = waiting ? reason : "";
        if (waiting && sameItem) return;
        if (comparison?.Evaluation != null) rating.Show(comparison, role);
        else if (pair != null) rating.ShowPair(pair);
        else { rating.Show(null, ""); rating.ExplainReason(reason.Length > 0 ? reason : "No compatible replacement is available with the current selection.", formula); }
    }
    internal void SetCopies(IEnumerable<InventoryEntry> instances, InventoryEntry? selected, Action<InventoryEntry?> changed)
    {
        var rows = instances.Select(i => new CopyChoice($"{i.Location} · +{i.Rank}" + (i.Augments.Count > 0 ? $" · {i.Augments.Count} effects" : ""), i)).ToList();
        copyChanged = changed;
        string key = string.Join('\n', rows.Select(r => PhysicalItemChoices.Identity(r.Instance))) + "|" + PhysicalItemChoices.Identity(selected);
        if (key == copiesKey) return; copiesKey = key;
        updating = true; copies.ItemsSource = rows;
        copies.SelectedItem = rows.FirstOrDefault(c => ReferenceEquals(c.Instance, selected) || PhysicalItemChoices.Identity(c.Instance) == PhysicalItemChoices.Identity(selected));
        copies.Visibility = rows.Count > 1 ? Visibility.Visible : Visibility.Collapsed; copies.ToolTip = "Choose a physical copy and inspect its recorded rank and effects. Other copies and saved inventory stay unchanged."; updating = false;
    }
}

internal sealed class RatingFormulaDialog : FrostDialog
{
    internal RatingFormulaDialog(string role, Dictionary<string, double> weights, GearComparison? comparison, bool pet = true, string candidate = "", Action? statProfiles = null) : base("How this rating works", 680, 560)
    {
        var result = comparison?.Evaluation;
        var resolved = result?.Role;
        var actualWeights = resolved?.Weights ?? weights;
        var lines = UI.Stack(UI.Text(comparison?.Candidate ?? (candidate.Length > 0 ? candidate : "No item selected"), 18, "Accent", true),
            UI.Text(comparison == null ? "Select an item for replacement arithmetic." : "Replaces: " + comparison.Replaces + " · Slot: " + comparison.Slot, 12),
            UI.Text("Role: " + role, 14, "Melee", true),
            UI.Text("Method: compare the entire loadout before and after replacement, including displaced equipment. Points = effective change × weight.", 12),
            FocusedLayout.Disclosure("Formula & resolved weights",UI.Text("Formula: " + RatingExplanation.Formula(actualWeights),12,"Mint"),"Each resolved weight multiplies the effective stat change. The substituted result is shown below."));
        if(pet) lines.Children.Add(UI.Text(PetRoleRatings.Explain(role),12,"SecondaryText"));
        if (resolved != null)
            lines.Children.Add(FocusedLayout.Disclosure("Why this role values these stats", RoleExplanationContent(resolved), "Resolved class priorities, design rationale and source. These weights are preferences, not game coefficients."));
        if (result != null)
        {
            var summaries = new System.Windows.Controls.Primitives.UniformGrid { Columns = pet ? 1 : 2 };
            foreach (var context in pet ? result.PetDefense!=null ? new[] {result.Unbuffed,result.PetDefense} : new[] { result.Unbuffed } : new[] { result.Unbuffed, result.Buffed }) summaries.Children.Add(UI.Card(UI.Stack(UI.Text(pet ? ReferenceEquals(context,result.PetDefense) ? "Defense preference" : result.PetDefense!=null ? "Damage preference" : "Equipment" : context.Name, 13, "Accent", true), UI.Text(context.Delta.HasValue ? context.Delta.Value.ToString("+0.0;-0.0;0") + " points" : "Unavailable", 16, context.Delta < 0 ? "Violet" : "Mint", true))));
            lines.Children.Add(summaries);
            foreach (var context in pet ? result.PetDefense!=null ? new[] {result.Unbuffed,result.PetDefense} : new[] { result.Unbuffed } : new[] { result.Unbuffed, result.Buffed })
            {
                lines.Children.Add(UI.Text(pet ? ReferenceEquals(context,result.PetDefense) ? "Defense preference" : result.PetDefense!=null ? "Damage preference" : "Equipment" : context.Name, 14, "Accent", true));
                lines.Children.Add(UI.Text(RatingExplanation.Status(context), 11, context.Delta.HasValue ? "SecondaryText" : "Violet"));
                foreach (bool gains in new[] { true, false })
                {
                    var changes = RatingExplanation.Ordered(context.Changes).Where(c => gains ? c.ItemChange > 0 : c.ItemChange < 0).ToList();
                    if (changes.Count == 0) continue;
                    lines.Children.Add(new Separator { Margin=new(0,8,0,6) });
                    lines.Children.Add(UI.Text(gains ? "Gains:" : "Losses:", 13, gains ? "Mint" : "Violet", true));
                    foreach (string category in ItemStatGroups.Categories)
                    {
                        var rows = changes.Where(c => ItemStatGroups.Category(c.Stat) == category).ToList();
                        if (rows.Count == 0) continue;
                        lines.Children.Add(UI.Text(category + ":", 12, "Melee", true));
                        foreach (var change in rows) {
                            var row=new Grid { Margin=new(0,2,0,3) };row.ColumnDefinitions.Add(new(){Width=new(100)});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
                            row.Children.Add(UI.Text(RatingExplanation.GroupedLabel(change.Stat)+":",11));var explanation=UI.Text(RatingExplanation.Change(change,resolved,true),11);Grid.SetColumn(explanation,1);row.Children.Add(explanation);lines.Children.Add(row);
                        }
                    }
                }
                lines.Children.Add(UI.Text("Result: " + RatingExplanation.Equation(context), 13, !context.Delta.HasValue || context.Delta < 0 ? "Violet" : "Mint", true));
                if (context.Delta.HasValue) lines.Children.Add(FocusedLayout.Disclosure("Exact arithmetic", UI.Text(string.Join(" + ", RatingExplanation.Ordered(context.Changes).Where(c => c.Points != 0).Select(c => c.Points.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + RatingExplanation.Label(c.Stat))) + " = " + context.Delta.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " points", 11), "Unrounded stored contributions. The compact equation identifies any rounding adjustment."));
                if (context.CurrentScore.HasValue && context.CandidateScore.HasValue) lines.Children.Add(UI.Text("Whole-loadout points: " + RatingExplanation.Number(context.CurrentScore.Value) + " → " + RatingExplanation.Number(context.CandidateScore.Value), 11));
                if (!context.Delta.HasValue && !pet && statProfiles != null) lines.Children.Add(UI.Button("Review Stat profiles…", statProfiles));
                var notes = result.Assumptions.Concat(context.Notes).Distinct().ToArray();
                if (notes.Length > 0) lines.Children.Add(FocusedLayout.Disclosure("Evidence & assumptions", UI.Stack(notes.Select(n => (UIElement)UI.Text("• " + n, 11, "SecondaryText")).ToArray()), "Evidence, missing metadata, confirmed caps and uncertainty for this context."));
            }
            if (result.EffectsGained.Count > 0) lines.Children.Add(UI.Text("Effects gained: " + string.Join(", ", result.EffectsGained), 12, "Mint"));
            if (result.EffectsLost.Count > 0) lines.Children.Add(UI.Text("Effects lost: " + string.Join(", ", result.EffectsLost), 12, "Violet"));
            if (result.WeaponEvidence.Length > 0) lines.Children.Add(UI.Text(result.WeaponEvidence, 11));
            else if (result.WeaponNote.Length > 0) lines.Children.Add(UI.Text(result.WeaponNote, 11));
            if (result.ActivationNote.Length > 0) lines.Children.Add(UI.Text(result.ActivationNote, 11));
            if (result.Status == "Conditional" || result.IsHypothetical) lines.Children.Add(UI.Text(result.Status + " · " + string.Join("; ", result.Assumptions), 12, "Violet", true));
        }
        else lines.Children.Add(UI.Text("A complete loadout comparison is unavailable. The stat card remains inspectable.", 12));
        lines.Children.Add(FocusedLayout.Disclosure("Limits, ranks & separate effects", UI.Stack(
            UI.Text("Only confirmed limits adjust effective changes. Worn haste uses the strongest item. Observed buff/AA totals are not added twice; unknown overflow remains uncertain.", 11),
            UI.Text("Ranks: weapon damage uses the adopted 10% per-tier model, rounded down without the ordinary-stat minimum. Published 5% and 10% guides disagree; exact client-observed values take precedence at their documented rank. Delay stays fixed. Ordinary positive stats use 10% with the completed-tier minimum. Catalog haste and negative values are retained unless observed; unresolved inputs are explained separately.", 11),
            UI.Text("Physical weight: WT (base) is catalog weight when no exact ranked observation is available. Weight does not affect preference points. Catalog baseline, owned rank and fractional tier progress are separate facts; a preview is not ownership evidence.", 11),
            UI.Text("Points are not DPS, healing, proc rate or damage reduction. Attribute benefits are priorities without invented conversion formulas. Damage and delay are represented through ratio. Effects, procs and unresolved off-hand mechanics remain separate.", 11),
            UI.Text("Arithmetic uses full-precision contributions; the inspector rounds the final display. Unchanged rows are omitted; changed zero-weight stats remain visible.", 11)), "Calculation boundaries and rank estimates."));
        Editor.Children.Add(lines); Actions.Children.Add(UI.Button("Close", Close));
    }
    internal static FrameworkElement RoleExplanationContent(SelfRoleResolution role)
    {
        var content = UI.Stack(role.Reasons.Select(n => (UIElement)UI.Text(n, 11, "SecondaryText")).ToArray());
        if (role.Automatic) content.Children.Add(UI.Button("Casting-family source", () => UI.OpenUrl(CastingPriority.Source)));
        return content;
    }
}
