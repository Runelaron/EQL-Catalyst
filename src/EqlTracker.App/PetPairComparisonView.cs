using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class PetPairComparisonView : StackPanel
{
    public PetPairComparisonView(ItemPairComparison value)
    {
        var sides = new Grid(); sides.ColumnDefinitions.Add(new()); sides.ColumnDefinitions.Add(new());
        Border Side(string label, ItemPairSide item)
        {
            var heading = new StackPanel();
            if (item.Definition is { } definition) { var icons = new ItemHeaderIcons(UI.Text(item.Name+" +"+item.Rank,16,"Accent",true)); icons.Show(definition, Application.Current is App app ? ItemOwnership.For(definition, app.Controller.Inventory) : new(false, "", false, null)); heading.Children.Add(icons); }
            else heading.Children.Add(UI.Text(item.Name + " +" + item.Rank, 16, "Accent", true));
            return UI.Card(UI.Stack(UI.Label(label), heading, item.Definition != null ? new ClassicItemCard(item.Definition, item.Rank, item.Instance) : UI.Text("Metadata unknown", 12)));
        }
        var current = Side("Current item", value.Current); var candidate = Side("Candidate", value.Candidate); candidate.Margin = new(5, 0, 0, 0); Grid.SetColumn(candidate, 1); sides.Children.Add(current); sides.Children.Add(candidate); Children.Add(sides);
        if (value.Warning.Length > 0) Children.Add(UI.Text(value.Warning, 12, "Melee", true));
        var table = UI.Table(("Stat", "Stat", 70, null), ("Current", "Current", 85, "0.###"), ("Candidate", "Candidate", 85, "0.###"), ("Change", "Change", 85, "+0.###;-0.###;0")); table.Height = 220;
        TableLayout.SetKey(table, "pet.item-pair.differences"); UI.SetRows(table, value.Differences.ToList());
        Children.Add(UI.Card(UI.Stack(UI.Text("Differences", 14, "Accent", true), UI.Text("Ranked item contributions · effects are compared separately · — means unknown", 11, "Muted"), table)));
        SizeChanged += (_, _) => { bool narrow = ActualWidth < 470; sides.RowDefinitions.Clear(); if (narrow) { sides.RowDefinitions.Add(new() { Height = GridLength.Auto }); sides.RowDefinitions.Add(new() { Height = GridLength.Auto }); } Grid.SetRow(candidate, narrow ? 1 : 0); Grid.SetColumn(candidate, narrow ? 0 : 1); Grid.SetColumnSpan(current, narrow ? 2 : 1); Grid.SetColumnSpan(candidate, narrow ? 2 : 1); };
    }
}
