using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class ClassicItemCard : Border
{
    private ItemDefinition? previous;
    private int previousRank = -1;
    private string previousInstance = "";
    internal ClassicItemCard(ItemDefinition item, int rank, InventoryEntry? instance = null)
    {
        Update(item, rank, instance, false);
    }
    internal void Update(ItemDefinition item, int rank, InventoryEntry? instance, bool animate)
    {
        string instanceKey = PhysicalItemChoices.Identity(instance);
        if (ReferenceEquals(item, previous) && previousRank == rank && previousInstance == instanceKey) return;
        previous = item; previousRank = rank; previousInstance = instanceKey;
        using var measure = UpdateDiagnostics.Measure("Classic item values apply");
        SetResourceReference(BackgroundProperty, "DetailSurface"); SetResourceReference(BorderBrushProperty, "Border");
        BorderThickness = new(1); Padding = new(7); Margin = new(0, 4, 0, 5);
        var text = Child as StackPanel ?? new StackPanel(); int line = 0;
        void Line(string value, string color = "Foreground")
        {
            if (value.Length == 0) return;
            if (line < text.Children.Count && text.Children[line] is TextBlock block) { NumericTransition.Set(block, value, animate); if (block.Tag?.ToString() != color) { block.SetResourceReference(TextBlock.ForegroundProperty, color); block.Tag = color; } }
            else { var added = UI.Text(value,11,color); added.Tag = color; text.Children.Add(added); }
            line++;
        }
        var stats = GearScorer.EffectiveStats(item, rank);
        string Number(string key, string label = "") => stats.TryGetValue(key, out double value) ? (label.Length > 0 ? label : key) + ": " + value.ToString("0.###") : "";
        string Signed(string key, string label = "") => stats.TryGetValue(key, out double value) ? (label.Length > 0 ? label : key) + ": " + value.ToString("+0.##;-0.##;0") : "";
        string Join(params string[] values) => string.Join("  ", values.Where(v => v.Length > 0));
        Line("Type: " + PetItemRules.Type(item), "Accent");
        Line(string.Join(" ", item.Display.Flags));
        if (item.Slots.Count > 0) Line("Slot: " + ItemPresentation.Slots(item.Slots));
        Line(Join(item.Display.Skill.Length > 0 ? "Skill: " + item.Display.Skill : "", Number("Delay", "Atk Delay")));
        Line(Join(Number("Damage", "DMG"), Number("Ratio")));
        Line(Number("AC"));
        Line(Join(new[] { "STR", "DEX", "STA", "CHA", "WIS", "INT", "AGI", "HP", "Mana", "ATK", "Haste" }.Select(s => Signed(s, s == "Mana" ? "MANA" : s)).ToArray()));
        Line(Join(new[] { "Fire", "Disease", "Cold", "Magic", "Poison", "Corruption" }.Select(s => Signed("Resist" + s, "SV " + s.ToUpperInvariant())).ToArray()));
        foreach (string effect in ItemEffects.Active(item, instance?.TransferredOutEffects)) Line("Effect: " + effect, "Mint");
        foreach (var effect in instance?.Augments ?? [])
        {
            string type = ExaltationPlanner.EffectType(effect.EffectText);
            string locked = type != "Unknown" && rank < ExaltationPlanner.Unlock(type) ? " · locked at this preview rank; requires +" + ExaltationPlanner.Unlock(type) : "";
            Line("Exaltation: " + (effect.EffectText.Length > 0 ? effect.EffectText : effect.Name) + locked, locked.Length > 0 ? "Melee" : "Mint");
        }
        if (item.RequiredLevel > 0) Line("Required level: " + item.RequiredLevel);
        if (item.EffectActivationLevel is { } level) Line("Effect activation: level " + level, "Mint");
        Line(Join(item.Display.Weight is { } weight ? "WT: " + weight.ToString("0.0") : "", item.Display.Range is { } range ? "Range: " + range : "", item.Display.Size.Length > 0 ? "Size: " + item.Display.Size : ""));
        if (item.Classes.Count > 0) Line("Class: " + string.Join(" ", item.Classes));
        if (item.Races.Count > 0) Line("Race: " + string.Join(" ", item.Races));
        if (item.Deity.Length > 0) Line("Deity: " + item.Deity);
        if (ItemPresentation.ExpansionNote(item).Length > 0) Line(ItemPresentation.ExpansionNote(item), "Muted");
        if (item.ContentAvailability == ContentAvailability.Future) Line("Future content · " + item.AvailabilityEvidence, "Melee");
        while (text.Children.Count > line) text.Children.RemoveAt(text.Children.Count - 1);
        if (Child == null) Child = text;
        ToolTip = "Known item facts at +" + rank + (rank > 0 ? " · tier-floor estimate; fractional XP unavailable." : ".") + "\n" + (item.Display.Source.Length > 0 ? item.Display.Source + " revision " + item.Display.Revision : "Display metadata incomplete; undocumented facts are omitted.");
    }
}

internal sealed class ItemDetailsInspector : StackPanel
{
    private readonly ItemHeaderIcons headerIcons;
    private readonly TextBlock title = UI.Text("Select an item to view its stats.", 16, "Accent", true), previewLabel = UI.Text("", 11, "Mint");
    private readonly ContentControl facts = new();
    private readonly TextBlock pending = UI.Text("", 10, "Muted");
    private readonly ComboBox copies = new() { DisplayMemberPath = "Label", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
    private Action<InventoryEntry?>? copyChanged;
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
        var reset = UI.Button("Reset preview", () => rankChanged?.Invoke(null)); reset.ToolTip = "Restore this candidate's recorded/list rank without editing inventory.";
        rankArea = UI.Stack(UI.Label("Preview +Rank"), RankSlider, previewLabel, reset);
        copies.Visibility = Visibility.Collapsed;
        copies.SelectionChanged += (_, _) => { if (!updating && copies.SelectedItem is CopyChoice copy) copyChanged?.Invoke(copy.Instance); };
        headerIcons=new(title);Children.Add(headerIcons); Children.Add(copies); Children.Add(facts); Children.Add(rankArea); Children.Add(pending); Children.Add(rating);
        RankSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => PreviewCommitted?.Invoke()));
        RankSlider.PreviewKeyUp += (_, _) => PreviewCommitted?.Invoke();
        RankSlider.ValueChanged += (_, _) => { if (!updating) rankChanged?.Invoke((int)RankSlider.Value); };
        Clear();
    }
    internal void Clear() { headerIcons.Clear(); copiesKey = ""; itemKey = ""; pending.Text = ""; copies.Visibility = Visibility.Collapsed; title.Text = "Select an item to view its stats and comparison."; facts.Content = null; rankArea.Visibility = rating.Visibility = Visibility.Collapsed; }
    internal void SetOwnership(ItemDefinition item, InventorySnapshot inventory) => headerIcons.Show(item, ItemOwnership.For(item, inventory));
    internal void ShowItem(ItemDefinition item, int rank, int listRank, bool preview, InventoryEntry? instance, GearComparison? comparison, string role, ItemPairComparison? pair, string reason, Action<int?> changed, Action formula)
    {
        rankChanged = changed; updating = true;
        bool sameItem = itemKey == item.Key; itemKey = item.Key;
        title.Text = item.Name + (rank > 0 ? " +" + rank : "");
        headerIcons.Show(item, new(false, "", false, null));
        if (sameItem && facts.Content is ClassicItemCard card) card.Update(item, rank, instance, AnimateValues); else facts.Content = new ClassicItemCard(item, rank, instance);
        RankSlider.Value = Math.Clamp(rank, 0, 10);
        previewLabel.Text = preview ? $"Preview +{rank} · list +{listRank}" : rank > 10 ? $"Recorded +{rank} · move slider to preview +0–10" : $"Candidate +{rank}";
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
    internal RatingFormulaDialog(string role, Dictionary<string, double> weights, GearComparison? comparison, bool pet = true) : base("How this rating works", 620, 480)
    {
        var result = comparison?.Evaluation;
        var lines = UI.Stack(UI.Text("Upgrade rating · " + role, 16, "Accent", true),
            UI.Text("Rating change = Σ (usable stat change × your selected weight). The whole loadout is evaluated before and after replacing the selected item, including displaced equipment.", 12),
            UI.Text(string.Join(" + ", weights.Where(w => w.Value != 0).Select(w => $"{w.Value:0.##} × Δ{w.Key}")), 12, "Mint"),
            UI.Text("Item change → usable change → preference points", 13, "Melee", true));
        if (result != null)
        {
            foreach (var context in pet ? new[] { result.Unbuffed } : new[] { result.Unbuffed, result.Buffed })
            {
                lines.Children.Add(UI.Text(pet ? "Equipment" : context.Name, 13, "Accent", true));
                foreach (var change in context.Changes) lines.Children.Add(UI.Text(change.Explanation, 11));
                lines.Children.Add(UI.Text("Net: " + (context.Delta?.ToString("+0.##;-0.##;0") ?? result.Status + " · " + context.Quality) + " points", 13, "Mint", true));
                foreach (string note in result.Assumptions.Concat(context.Notes)) lines.Children.Add(UI.Text(note, 11, "Muted"));
            }
            if (result.IsHypothetical) lines.Children.Add(UI.Text("Hypothetical compatibility assumptions apply only to this Theorycraft workspace.", 12, "Melee", true));
        }
        else lines.Children.Add(UI.Text("A complete loadout rating is unavailable. Raw item stats and differences remain inspectable.", 12));
        lines.Children.Add(UI.Text("Worn haste uses the strongest listed item. Only confirmed limits apply. Observed totals already including buffs or AAs are not added twice. Unknown base stats and bonuses are not invented. Item ranks use the existing tier-floor estimates: ordinary stats +10% per tier, weapon damage +5%, fixed delay/haste, with the scorer's rounding/minimum increment. Fractional item XP is unknown.", 11, "Muted"));
        lines.Children.Add(UI.Text("Effects and procs are separate. DEX points are a preference, not predicted proc rate. Points are not DPS. Two-handed versus dual-wield results remain Conditional when off-hand contribution is unknown. Unscored stats still appear in item differences.", 11, "Muted"));
        if (pet) lines.Children.Add(UI.Text("Rules: " + PetItemRules.Version + " · official ratio guidance: October 13, 2026; no published complete pet DPS formula.", 10, "Muted"));
        Editor.Children.Add(lines); Actions.Children.Add(UI.Button("Close", Close));
    }
}
