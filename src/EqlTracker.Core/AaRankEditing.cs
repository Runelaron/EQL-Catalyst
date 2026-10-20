namespace EqlTracker.Core;

public static class AaRankEditing
{
    public static bool Set(GearAaModifier aa, int? rank, out string error)
    {
        error = "";
        if (rank < 0 || aa.VerifiedMaxRank is { } max && rank > max) { error = "Rank must be nonnegative" + (aa.VerifiedMaxRank is { } maximum ? " and no greater than verified maximum " + maximum : "") + "."; return false; }
        if (rank == aa.Rank) return true;
        aa.Rank = rank; aa.RankObservedAt = null; aa.RankEvidence = "Manual rank override";
        if (!aa.ValueManuallyOverridden && aa.Kind != AaModifierKind.Information)
        {
            if (rank is { } r && aa.RankRuleSource.Length > 0 && aa.VerifiedRankBonuses.TryGetValue(r, out double value)) { aa.Value = value; aa.Confirmed = true; aa.Source = aa.RankRuleSource; }
            else { aa.Confirmed = false; error = "Rank saved; numeric bonus needs confirmation. No verified rule was supplied."; }
        }
        return true;
    }
}
