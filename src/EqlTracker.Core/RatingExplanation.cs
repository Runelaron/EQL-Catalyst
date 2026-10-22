using System.Globalization;

namespace EqlTracker.Core;

public static class RatingExplanation
{
    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Signed(double value) => value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);
    private static string Weight(double value) => (double.TryParse(Number(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double shown) && Math.Abs(shown - value) > 0.0000000001 ? "≈" : "") + Number(value);
    public static string Label(string stat) => stat switch { "ResistFire" => "Fire resist", "ResistCold" => "Cold resist", "ResistMagic" => "Magic resist", "ResistDisease" => "Disease resist", "ResistPoison" => "Poison resist", "ResistCorruption" => "Corruption resist", "ResistVoid" => "Void resist", "Ratio" => "Weapon ratio", "Mana" => "Mana", _ => stat };
    public static string GroupedLabel(string stat) => stat.StartsWith("Resist",StringComparison.Ordinal) ? stat[6..] : Label(stat);
    public static IEnumerable<BuildContribution> Ordered(IEnumerable<BuildContribution> changes) => changes.Where(c => c.ItemChange != 0).OrderBy(c => Array.IndexOf(ItemStatGroups.Categories, ItemStatGroups.Category(c.Stat))).ThenBy(c => ItemStatGroups.Order(c.Stat)).ThenBy(c => c.Stat, StringComparer.Ordinal);
    public static string Change(BuildContribution change, SelfRoleResolution? role = null, bool grouped = false)
    {
        string result = (change.ItemChange > 0 ? "Gain " : "Lose ") + Number(Math.Abs(change.ItemChange)) + " " + (grouped ? GroupedLabel(change.Stat) : Label(change.Stat));
        if (change.Stat == "Delay") result = (change.ItemChange < 0 ? "Reduce delay by " : "Increase delay by ") + Number(Math.Abs(change.ItemChange)) + (change.ItemChange < 0 ? " (faster)" : " (slower)");
        if (change.Weight == 0) result += " · 0 points. " + (role?.Exclusion(change.Stat) ?? "Not included in this role's preference score; the stat change remains visible.");
        else result += " · " + (Math.Abs(change.UsableChange - change.ItemChange) > .0000001 ? "effective change " + Signed(change.UsableChange) + " × " : Signed(change.UsableChange) + " × ") + Weight(change.Weight) + " = " + Signed(change.Points) + " points";
        if (change.Note.Length > 0) result += " · " + change.Note;
        return result;
    }
    public static string Equation(ContextRating context)
    {
        if (!context.Delta.HasValue) return "Rating unavailable · " + context.Quality;
        var terms = Ordered(context.Changes).Where(c => c.Points != 0).ToList();
        double displayedSum = terms.Sum(c => Math.Round(c.Points, 3));
        double rounding = Math.Round(Math.Round(context.Delta.Value, 3) - displayedSum, 3);
        return (terms.Count == 0 ? "0" : string.Join(" ", terms.Select(c => Signed(c.Points) + " " + Label(c.Stat)))) + (rounding != 0 ? " " + Signed(rounding) + " rounding" : "") + " = " + Signed(context.Delta.Value) + " points";
    }
    public static string Formula(IReadOnlyDictionary<string, double> weights) => string.Join(" + ", weights.Where(w => w.Value != 0).OrderBy(w => Array.IndexOf(ItemStatGroups.Categories, ItemStatGroups.Category(w.Key))).ThenBy(w => ItemStatGroups.Order(w.Key)).ThenBy(w => w.Key).Select(w => Weight(w.Value) + " × Δ" + Label(w.Key)));
    public static string Status(ContextRating context) => !context.Delta.HasValue ? "Unavailable · " + context.Quality : context.MarginalEstimate.HasValue ? "Estimate · known replacement change; some other equipment data is incomplete." : context.Quality.StartsWith("Confirmed") ? "Confirmed stat context · preference estimate" : "Estimate · equipment stats; total stats and caps are not confirmed.";
}
