using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class RoleWeightsDialog : FrostDialog
{
    internal readonly Dictionary<string, TextBox> Inputs = [];
    internal readonly TextBox RoleName = UI.Input("", 240);
    private readonly CharacterProfile profile;
    private readonly string selected;
    private readonly bool adding;
    private bool automaticReset, loading;
    private readonly bool initiallyAutomatic;
    private readonly Dictionary<string, (string Text, double Value)> originalValues = [];
    private readonly TextBlock error = UI.Text("", 11, "Violet");
    internal string SavedName { get; private set; } = "";
    internal RoleWeightsDialog(CharacterProfile profile, string role, bool adding = false) : base(adding ? "Add role" : "Edit " + role + " weights", 740, 600)
    {
        this.profile = profile; selected = role; this.adding = adding; initiallyAutomatic = !adding && SelfRoleRatings.Goals.Contains(role) && !profile.SelfRoleOverrides.ContainsKey(role);
        var root = UI.Stack(UI.Text("Preference points per effective stat change. Negative weights are allowed. These values do not predict DPS, mana conversion, or proc rates.", 12));
        if (adding) root.Children.Add(UI.Field("Role name", RoleName));
        var weights = SelfRoleRatings.Resolve(profile, profile.ActiveLoadout, role).Weights;
        var columns = new WrapPanel();
        string[][] groups = [["AC", "HP", "Mana", "Endurance", "Vitality", "HPRegen", "ManaRegen"], ["STR", "DEX", "STA", "CHA", "WIS", "INT", "AGI", "ATK", "Haste", "Ratio", "Damage", "Delay", "Backstab"], ["ResistFire", "ResistDisease", "ResistCold", "ResistMagic", "ResistPoison", "ResistCorruption"]];
        string[] headings = ["AC & Resources", "Stats & weapons", "Resists"];
        for (int i = 0; i < groups.Length; i++)
        {
            var column = UI.Stack(UI.Text(headings[i], 13, "Melee", true)); column.Width = 220; column.Margin = new(0, 0, 8, 0);
            var keys = i == 1 ? groups[i].Concat(weights.Keys.Except(groups.SelectMany(g => g))).ToArray() : groups[i];
            foreach (string stat in keys)
            {
                var row = new Grid { Margin = new(0, 0, 0, 6) }; row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new(86) });
                var label = UI.Text(stat == "Endurance" ? "END (raw)" : RatingExplanation.Label(stat), 12); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new(0);
                double initialValue = weights.GetValueOrDefault(stat); string initialText = initialValue.ToString("0.####", CultureInfo.InvariantCulture); originalValues[stat] = (initialText, initialValue);
                var input = UI.Input(initialText); input.Height = 28; input.Margin = new(0); input.ToolTip = stat == "Endurance" ? "Points for documented raw item Endurance only; no STR/STA-to-Endurance conversion is inferred." : "Preference points per effective " + RatingExplanation.Label(stat) + ". Zero keeps changes visible without scoring them.";
                System.Windows.Automation.AutomationProperties.SetName(input, RatingExplanation.Label(stat) + " weight");
                input.TextChanged += (_, _) => { if (!loading) automaticReset = false; }; Inputs[stat] = input; Grid.SetColumn(input, 1); row.Children.Add(label); row.Children.Add(input); column.Children.Add(row);
            }
            columns.Children.Add(column);
        }
        root.Children.Add(columns); root.Children.Add(error); Editor.Children.Add(root);
        if (!adding && (SelfRoleRatings.Goals.Contains(role) || GearScorer.Defaults.ContainsKey(role))) Actions.Children.Add(UI.Button("Reset weights to default", ResetDefaults));
        Actions.Children.Add(UI.Button(adding ? "Add role" : "Save weights", Save)); Actions.Children.Add(UI.Button("Cancel", Close));
    }
    internal void ResetDefaults()
    {
        var copy = EquipmentPositions.Copy(profile); copy.SelfRoleOverrides.Remove(selected);
        var defaults = SelfRoleRatings.Goals.Contains(selected) ? SelfRoleRatings.Resolve(copy, copy.ActiveLoadout, selected).Weights : GearScorer.Defaults[selected];
        loading = true; foreach (var (stat, input) in Inputs) { double value = defaults.GetValueOrDefault(stat); input.Text = value.ToString("0.####", CultureInfo.InvariantCulture); originalValues[stat] = (input.Text, value); } loading = false; automaticReset = SelfRoleRatings.Goals.Contains(selected);
        error.Text = automaticReset ? "Default automatic class priorities will resume when you save." : "Default weights restored in this editor. Save to apply.";
    }
    internal void Save()
    {
        string name = adding ? RoleName.Text.Trim() : selected;
        if (name.Length == 0 || name.Length > 60 || SelfRoleRatings.IsRetired(name) || adding && SelfRoleRatings.Names(profile).Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))) { error.Text = "Choose a unique role name from 1–60 characters; retired preset names are reserved."; return; }
        var weights = new Dictionary<string, double>();
        foreach (var (stat, input) in Inputs)
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) { error.Text = "Enter a finite number for " + RatingExplanation.Label(stat) + "."; input.Focus(); return; }
            if (input.Text == originalValues[stat].Text) value = originalValues[stat].Value;
            if (value != 0) weights[stat] = value;
        }
        if (automaticReset || initiallyAutomatic && Inputs.All(row => row.Value.Text == originalValues[row.Key].Text)) profile.SelfRoleOverrides.Remove(name); else profile.SelfRoleOverrides[name] = weights;
        SavedName = name; DialogResult = true;
    }
}
