using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class AaRankEditor : StackPanel
{
    public event Action<GearAaModifier>? Changed;
    private readonly TextBox value = UI.Input("", 38);
    private bool loading;
    public AaRankEditor()
    {
        Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center;
        var minus = UI.Button("−", () => Step(-1)); var plus = UI.Button("+", () => Step(1));
        foreach (var button in new[] { minus, plus }) { button.Width = 23; button.MinWidth = 0; button.Padding = new(0); button.Margin = new(1, 0, 1, 0); }
        minus.ToolTip = "Decrease the confirmed AA rank; zero means untrained. Clear the value to restore Unknown."; plus.ToolTip = "Increase AA rank. The first increment from Unknown establishes rank 1. Only verified rank rules update numeric bonuses.";
        value.Margin = new(0); value.Height = 23; value.ToolTip = "AA rank; blank means Unknown. Enter commits; Escape restores the previous value. Unknown is never treated as zero.";
        Children.Add(minus); Children.Add(value); Children.Add(plus);
        DataContextChanged += (_, _) => Load(); value.LostKeyboardFocus += (_, _) => Commit();
        value.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } if (e.Key == Key.Escape) { Load(); e.Handled = true; } if (e.Key == Key.Up) { Step(1); e.Handled = true; } if (e.Key == Key.Down) { Step(-1); e.Handled = true; } };
    }
    private void Load() { loading = true; value.Text = (DataContext as GearAaModifier)?.Rank?.ToString() ?? ""; loading = false; }
    private void Step(int direction) { if (DataContext is GearAaModifier aa) Set(aa.Rank == null ? direction > 0 ? 1 : 0 : Math.Max(0, aa.Rank.Value + direction)); }
    private void Commit()
    {
        if (loading) return; string input = value.Text.Trim(); if (input.Length == 0) Set(null); else if (int.TryParse(input, out int rank)) Set(rank); else { value.ToolTip = "Use a nonnegative whole rank or blank for Unknown."; Load(); }
    }
    private void Set(int? rank)
    {
        if (DataContext is not GearAaModifier aa) return;
        bool changed = aa.Rank != rank;
        bool accepted = AaRankEditing.Set(aa, rank, out string error); value.ToolTip = error.Length > 0 ? error : "Rank saved manually; numeric effects use verified rules only.";
        if (accepted && changed) Changed?.Invoke(aa); Load();
    }
}
