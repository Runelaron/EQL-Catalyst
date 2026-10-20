using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace EqlTracker.App;

internal static class NumericTransition
{
    private sealed record Transition(string Target, MatchCollection Numbers, double[] From, double[] To, DateTime Started);
    private static readonly Regex numbers = new(@"[+-]?\d[\d,]*(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Dictionary<TextBlock, Transition> active = [];
    internal static void Set(TextBlock text, string target, bool animate)
    {
        if (text.Text == target && !active.ContainsKey(text)) return;
        if (animate && active.TryGetValue(text, out var existing) && existing.Target == target) return;
        AutomationProperties.SetName(text, target);
        var before = numbers.Matches(text.Text); var after = numbers.Matches(target);
        bool same = before.Count == after.Count && after.Count > 0 && numbers.Replace(text.Text, "#") == numbers.Replace(target, "#");
        if (!animate || !same) { active.Remove(text); text.Text = target; StopIfIdle(); return; }
        var from = new double[before.Count]; var to = new double[after.Count];
        for (int i = 0; i < before.Count; i++)
            if (!double.TryParse(before[i].Value, NumberStyles.Number, CultureInfo.CurrentCulture, out from[i]) || !double.TryParse(after[i].Value, NumberStyles.Number, CultureInfo.CurrentCulture, out to[i])) { active.Remove(text); text.Text = target; StopIfIdle(); return; }
        if (active.Count == 0) CompositionTarget.Rendering += Frame;
        active[text] = new(target, after, from, to, DateTime.UtcNow);
    }
    private static void Frame(object? sender, EventArgs e)
    {
        foreach (var (text, state) in active.ToArray())
        {
            double t = Math.Clamp((DateTime.UtcNow - state.Started).TotalMilliseconds / 140, 0, 1);
            if (t >= 1 || !text.IsVisible) { text.Text = state.Target; active.Remove(text); continue; }
            double eased = 1 - Math.Pow(1 - t, 3); int i = 0;
            text.Text = numbers.Replace(state.Target, m =>
            {
                double value = state.From[i] + (state.To[i] - state.From[i++]) * eased;
                int point = m.Value.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal); int decimals = point < 0 ? 0 : m.Value.Length - point - 1;
                string formatted = value.ToString((m.Value.Contains(CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator, StringComparison.Ordinal) ? "N" : "F") + decimals, CultureInfo.CurrentCulture);
                return m.Value.StartsWith('+') && value >= 0 ? "+" + formatted : formatted;
            });
        }
        StopIfIdle();
    }
    private static void StopIfIdle() { if (active.Count == 0) CompositionTarget.Rendering -= Frame; }
}
