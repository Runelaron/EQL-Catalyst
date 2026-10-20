namespace EqlTracker.Core;

public sealed record EncounterEditUndo(List<Encounter> Before, List<string> AfterIds, string Description);

public static class EncounterEdits
{
    public static Encounter Merge(Encounter first, Encounter second)
    {
        if (!first.Finished || !second.Finished || !Identity.ZoneKey(first.Zone).Equals(Identity.ZoneKey(second.Zone)))
            throw new InvalidOperationException("Merge two finished encounters in the same zone.");
        var merged = CombatSnapshots.Copy(first.Start <= second.Start ? first : second);
        merged.Id = Guid.NewGuid().ToString("N"); merged.Start = first.Start < second.Start ? first.Start : second.Start;
        merged.End = first.End > second.End ? first.End : second.End;
        merged.Events = first.Events.Concat(second.Events).DistinctBy(e => e.Id).OrderBy(e => e.Time).ToList();
        merged.PlayerDamage = first.PlayerDamage + second.PlayerDamage; merged.PetDamage = first.PetDamage + second.PetDamage;
        merged.UnknownDamage = first.UnknownDamage + second.UnknownDamage;
        merged.Heals = first.Heals.Concat(second.Heals).DistinctBy(e => e.Id).Select(e => e.Copy()).OrderBy(e => e.Time).ToList();
        foreach (var e in merged.Heals) e.EncounterId = merged.Id;
        merged.HealingCoverage = first.HealingCoverage == second.HealingCoverage ? first.HealingCoverage : "Partial";
        merged.HealingDetailAvailable = first.HealingDetailAvailable && second.HealingDetailAvailable;
        if (merged.HealingDetailAvailable) HealingLedger.Summarize(merged);
        else merged.HealingSummaries = first.HealingSummaries.Concat(second.HealingSummaries).GroupBy(s => (s.Direction, s.Owner)).Select(g => new HealingSummary(g.Key.Direction, g.Key.Owner, g.Sum(s => s.Effective), g.Sum(s => s.KnownOverheal), g.Sum(s => s.RawKnownCount), g.Sum(s => s.Count), g.Max(s => s.MaxHeal))).ToList();
        merged.DetailAvailable = first.DetailAvailable && second.DetailAvailable;
        merged.Peaks = first.Peaks.Concat(second.Peaks).GroupBy(p => (p.Actor, p.Ability, p.Channel)).Select(g => g.OrderByDescending(p => p.Damage).ThenBy(p => p.Time).First() with { EncounterId = merged.Id }).ToList();
        merged.IdleResumable = false; merged.EndReason = "Manual merge"; return merged;
    }
    public static List<Encounter> Split(Encounter original, DateTime at)
    {
        if (!original.Finished || !original.DetailAvailable || !original.HealingDetailAvailable) throw new InvalidOperationException("Splitting requires a finished encounter with complete event details.");
        var groups = new[] { original.Events.Where(e => e.Time < at).ToList(), original.Events.Where(e => e.Time >= at).ToList() };
        if (groups.Any(g => g.Count == 0)) throw new InvalidOperationException("Choose a time with combat events on both sides.");
        return groups.Select(events =>
        {
            var split = CombatSnapshots.Copy(original); split.Id = Guid.NewGuid().ToString("N"); split.Events = events;
            split.Start = events.Min(e => e.Time); split.End = events.Max(e => e.Time); split.EndReason = "Manual split";
            split.Heals = original.Heals.Where(e => e.Time >= split.Start && e.Time <= split.End && (ReferenceEquals(events, groups[0]) ? e.Time < at : e.Time >= at)).Select(e => e.Copy()).ToList();
            foreach (var e in split.Heals) e.EncounterId = split.Id;
            HealingLedger.Summarize(split);
            split.IdleResumable = false; CombatEngine.Recount(split); return split;
        }).ToList();
    }
}
