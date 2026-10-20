using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed class HealingEvent
{
    public string Id { get; set; } = "";
    public DateTime Time { get; set; }
    public string Healer { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string HealerOwner { get; set; } = "Unknown";
    public string RecipientOwner { get; set; } = "Unknown";
    public string AttributionEvidence { get; set; } = "";
    public string Ability { get; set; } = "";
    public long Effective { get; set; }
    public long? Raw { get; set; }
    public long? Overheal => Raw >= Effective ? Raw - Effective : null;
    public bool Critical { get; set; }
    public bool OverTime { get; set; }
    public bool Lifetap { get; set; }
    public string Zone { get; set; } = "";
    public string EncounterId { get; set; } = "";
    public HealingEvent Copy() => (HealingEvent)MemberwiseClone();
}

public static class HealingIdentity
{
    // Verified from retained lifetap damage/heal pairs. Unknown spells still count as healing.
    private static readonly HashSet<string> Lifetaps = new(["Spirit Tap", "Vampiric Embrace", "Scream of Death Strike"], StringComparer.OrdinalIgnoreCase);
    public static bool IsLifetap(string ability) => Lifetaps.Contains(Regex.Replace(ability.Trim(), @"\s+(?:[IVX]+|\d+)(?:\s*\+\d+)?$", "", RegexOptions.IgnoreCase));
    public static string Owner(string name, DateTime time, CharacterProfile profile) => CombatIdentity.Same(name, profile.Character) || CombatIdentity.Same(name, "you") ? "Player" :
        profile.PetIdentities.Any(p => CombatIdentity.Same(name, p.Name) && time >= p.From.AddSeconds(-2) && (!p.Until.HasValue || time < p.Until)) ? "Pet" : "Unknown";
    public static void Attribute(HealingEvent e, CharacterProfile profile)
    {
        e.HealerOwner = Owner(e.Healer, e.Time, profile); e.RecipientOwner = Owner(e.Recipient, e.Time, profile);
        e.AttributionEvidence = $"Healer: {(e.HealerOwner == "Pet" ? "owned pet at event time" : e.HealerOwner == "Player" ? "character identity" : "logged name, ownership unknown")}; recipient: {(e.RecipientOwner == "Pet" ? "owned pet at event time" : e.RecipientOwner == "Player" ? "character identity" : "logged name, ownership unknown")}";
    }
}

public sealed record HealingSummary(string Direction, string Owner, long Effective, long KnownOverheal, int RawKnownCount, int Count, long MaxHeal);
public sealed record HealingFilter(string Direction = "Done", string Owner = "Combined", string Healer = "", string Recipient = "", string Ability = "", string Kind = "", bool LifetapOnly = false, bool CriticalOnly = false, DateTime? From = null, DateTime? Until = null);
public sealed class HealingSelection
{
    public string Direction { get; set; } = "Done";
    public string Actor { get; set; } = "Combined";
    public string Healer { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Ability { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool LifetapOnly { get; set; }
    public bool CriticalOnly { get; set; }
    public string Sort { get; set; } = "Healing";
    public HealingFilter Filter(DateTime? from = null, DateTime? until = null) => new(Direction, Actor, Healer, Recipient, Ability, Kind, LifetapOnly, CriticalOnly, from, until);
}
public sealed record HealingRow(string Ability, string Kind, bool Lifetap, long Effective, double Hps, int Count, double Average, long MaxHeal, double CriticalPercent, double Percent, long KnownOverheal, int RawKnownCount, DateTime? PeakTime);
public sealed record HealingPartyRow(string Name, string Role, long Effective, double Hps, int Count, double Average, long MaxHeal, double CriticalPercent, double Percent);
public sealed record HealingViewState(HealingFilter Filter, List<HealingRow> Rows, List<HealingPartyRow> Recipients, List<HealingPartyRow> Healers, List<HealingEvent> Events,
    double Seconds, long Effective, double Hps, long MaxHeal, long KnownOverheal, int RawKnownCount, int Count, long OutsideCombat, string Coverage, bool DetailComplete, string Context)
{ public CombatChart Chart { get; init; } = new(default, 1, 1, [], [], []) { Unit = "HPS" }; public List<string> AvailableHealers { get; init; } = []; public List<string> AvailableRecipients { get; init; } = []; public List<HealingEvent> OutsideEvents { get; init; } = []; public bool TotalsAvailable { get; init; } = true; public string Lifecycle { get; init; } = ""; }

public sealed class HealingLedger(CharacterProfile profile)
{
    private readonly Dictionary<string, HealingEvent> events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HealingEvent> pendingWrites = new(StringComparer.Ordinal);
    private readonly List<HealingEvent> pendingAssignment = [];
    public IReadOnlyCollection<HealingEvent> Events => events.Values;
    public IReadOnlyCollection<HealingEvent> PendingWrites => pendingWrites.Values;
    public long Revision { get; private set; }
    public void Restore(IEnumerable<HealingEvent> saved) { foreach (var e in saved) { events[e.Id] = e; if (e.EncounterId.Length == 0) pendingAssignment.Add(e); } Revision++; }
    public void Accept(HealingEvent e, string zone, Encounter? current)
    {
        if (e.Id.Length == 0) e.Id = Guid.NewGuid().ToString("N");
        if (events.ContainsKey(e.Id)) return;
        e.Zone = zone; HealingIdentity.Attribute(e, profile); events.Add(e.Id, e); pendingAssignment.Add(e); pendingWrites[e.Id] = e; Revision++;
        if (current != null) Attach(current);
    }
    public bool Attach(Encounter fight)
    {
        bool changed = false;
        for (int i = pendingAssignment.Count - 1; i >= 0; i--)
        {
            var e = pendingAssignment[i];
            if (e.Time < fight.Start.AddSeconds(-30)) { pendingAssignment.RemoveAt(i); continue; }
            if (e.Time < fight.Start || e.Time > fight.End || Identity.ZoneKey(e.Zone) != Identity.ZoneKey(fight.Zone)) continue;
            e.EncounterId = fight.Id; fight.Heals.Add(e); pendingAssignment.RemoveAt(i); pendingWrites[e.Id] = e; changed = true;
        }
        if (changed) { Summarize(fight); Revision++; }
        return changed;
    }
    public void AssignHistorical(HealingEvent e, string zone, Encounter? fight)
    {
        if (events.ContainsKey(e.Id)) return;
        HealingIdentity.Attribute(e, profile); e.Zone = zone;
        if (fight != null) { e.EncounterId = fight.Id; fight.Heals.Add(e); } else pendingAssignment.Add(e);
        events.Add(e.Id, e); pendingWrites[e.Id] = e; Revision++;
    }
    public void MarkSaved(IEnumerable<HealingEvent> saved) { foreach (var e in saved) pendingWrites.Remove(e.Id); }
    public List<Encounter> Reattribute(IEnumerable<Encounter> fights)
    {
        var changed = new HashSet<string>();
        foreach (var e in events.Values)
        {
            var previous = (e.HealerOwner, e.RecipientOwner, e.AttributionEvidence); HealingIdentity.Attribute(e, profile);
            if (previous == (e.HealerOwner, e.RecipientOwner, e.AttributionEvidence)) continue;
            pendingWrites[e.Id] = e; changed.Add(e.EncounterId);
        }
        var byEncounter = events.Values.Where(e => e.EncounterId.Length > 0).ToLookup(e => e.EncounterId);
        var affected = new List<Encounter>();
        foreach (var f in fights)
        {
            f.Heals = byEncounter[f.Id].ToList();
            if (f.HealingDetailAvailable && (changed.Contains(f.Id) || f.HealingSummaries.Count == 0)) { Summarize(f); affected.Add(f); }
        }
        if (changed.Count > 0) Revision++;
        return affected;
    }
    public void Replace(IEnumerable<string> beforeIds, IEnumerable<Encounter> after)
    {
        var ids = beforeIds.ToHashSet();
        foreach (var e in events.Values.Where(e => ids.Contains(e.EncounterId))) { e.EncounterId = ""; pendingWrites[e.Id] = e; }
        foreach (var f in after) foreach (var e in f.Heals) { e.EncounterId = f.Id; events[e.Id] = e; pendingWrites[e.Id] = e; }
        Revision++;
    }
    public void Delete(string encounter)
    { foreach (var e in events.Values.Where(e => e.EncounterId == encounter).ToList()) { events.Remove(e.Id); pendingWrites.Remove(e.Id); pendingAssignment.Remove(e); } Revision++; }
    public void Retain(DateTime cutoff)
    { foreach (var e in events.Values.Where(e => e.Time < cutoff).ToList()) { events.Remove(e.Id); pendingWrites.Remove(e.Id); pendingAssignment.Remove(e); } Revision++; }
    public static void Summarize(Encounter fight)
    {
        fight.HealingSummaries = new[] { "Done", "Received" }.SelectMany(direction => new[] { "Player", "Pet" }.Select(owner =>
        {
            var selected = fight.Heals.Where(e => (direction == "Done" ? e.HealerOwner : e.RecipientOwner) == owner).ToList();
            return new HealingSummary(direction, owner, selected.Sum(e => e.Effective), selected.Sum(e => e.Overheal ?? 0), selected.Count(e => e.Overheal.HasValue), selected.Count, selected.Select(e => e.Effective).DefaultIfEmpty(0).Max());
        })).ToList();
    }
}

public static class HealingMetrics
{
    public static bool Matches(HealingEvent e, HealingFilter f) =>
        (f.Owner == "Combined" ? (f.Direction == "Done" ? e.HealerOwner : e.RecipientOwner) is "Player" or "Pet" : (f.Direction == "Done" ? e.HealerOwner : e.RecipientOwner) == f.Owner) &&
        (f.Healer.Length == 0 || CombatIdentity.Same(e.Healer, f.Healer)) && (f.Recipient.Length == 0 || CombatIdentity.Same(e.Recipient, f.Recipient)) &&
        (f.Ability.Length == 0 || e.Ability.Contains(f.Ability, StringComparison.OrdinalIgnoreCase)) && (f.Kind.Length == 0 || e.OverTime == (f.Kind == "HoT")) &&
        (!f.LifetapOnly || e.Lifetap) && (!f.CriticalOnly || e.Critical) && (!f.From.HasValue || e.Time >= f.From) && (!f.Until.HasValue || e.Time <= f.Until);
    public static HealingViewState Build(IEnumerable<Encounter> fights, IEnumerable<HealingEvent> outside, HealingFilter filter, string sort = "Healing", string context = "")
    {
        var all = fights.ToList(); double seconds = CombatMetrics.Duration(all, new(From: filter.From, Until: filter.Until));
        var events = all.Where(f => f.HealingDetailAvailable).SelectMany(f => f.Heals).Where(e => Matches(e, filter)).DistinctBy(e => e.Id).OrderBy(e => e.Time).ToList();
        long effective = events.Sum(e => e.Effective), max = events.Select(e => e.Effective).DefaultIfEmpty(0).Max(), overheal = events.Sum(e => e.Overheal ?? 0); int count = events.Count, rawCount = events.Count(e => e.Overheal.HasValue);
        bool contentFilter = filter.Healer.Length > 0 || filter.Recipient.Length > 0 || filter.Ability.Length > 0 || filter.Kind.Length > 0 || filter.LifetapOnly || filter.CriticalOnly;
        foreach (var fight in all.Where(f => !f.HealingDetailAvailable && f.HealingCoverage != "Unavailable" && !contentFilter && !(filter.From > f.Start || filter.Until < f.End)))
        {
            foreach (var s in fight.HealingSummaries.Where(s => s.Direction == filter.Direction && (filter.Owner == "Combined" || s.Owner == filter.Owner)))
            { effective += s.Effective; count += s.Count; max = Math.Max(max, s.MaxHeal); overheal += s.KnownOverheal; rawCount += s.RawKnownCount; }
        }
        long detailedTotal = events.Sum(e => e.Effective); double denominator = Math.Max(1, seconds);
        var rows = events.GroupBy(e => (e.Ability, e.OverTime, e.Lifetap)).Select(g => { long total = g.Sum(e => e.Effective), peak = g.Max(e => e.Effective); return new HealingRow(g.Key.Ability, g.Key.OverTime ? "HoT" : "Direct", g.Key.Lifetap, total, total / denominator, g.Count(), g.Average(e => e.Effective), peak, g.Count(e => e.Critical) * 100d / g.Count(), detailedTotal > 0 ? total * 100d / detailedTotal : 0, g.Sum(e => e.Overheal ?? 0), g.Count(e => e.Overheal.HasValue), g.Where(e => e.Effective == peak).Min(e => e.Time)); });
        var ordered = sort switch { "Heals" => rows.OrderByDescending(r => r.Count), "Average" => rows.OrderByDescending(r => r.Average), "Max" => rows.OrderByDescending(r => r.MaxHeal), "HPS" => rows.OrderByDescending(r => r.Hps), _ => rows.OrderByDescending(r => r.Effective) };
        List<HealingPartyRow> Parties(bool recipient) => events.GroupBy(e => recipient ? e.Recipient : e.Healer, StringComparer.OrdinalIgnoreCase).Select(g => new HealingPartyRow(g.Key, recipient ? "Recipient" : "Healer", g.Sum(e => e.Effective), g.Sum(e => e.Effective) / denominator, g.Count(), g.Average(e => e.Effective), g.Max(e => e.Effective), g.Count(e => e.Critical) * 100d / g.Count(), detailedTotal > 0 ? g.Sum(e => e.Effective) * 100d / detailedTotal : 0)).OrderByDescending(r => r.Effective).ToList();
        string coverage = all.Count == 0 || all.All(f => f.HealingCoverage == "Complete") ? "Complete" : all.All(f => f.HealingCoverage == "Unavailable") ? "Unavailable" : "Partial";
        return new(filter, ordered.ThenBy(r => r.Ability, StringComparer.OrdinalIgnoreCase).ToList(), Parties(true), Parties(false), events, seconds, effective, effective / denominator, max, overheal, rawCount, count, outside.Where(e => e.EncounterId.Length == 0 && Matches(e, filter)).Sum(e => e.Effective), coverage, all.All(f => f.HealingDetailAvailable) && coverage == "Complete", context) { Chart = CombatChart.BuildHealing(all, events, filter), Lifecycle = string.Join('|', all.Select(f => $"{f.Id}:{f.Finished}:{f.IdleResumable}:{f.ResolvedTargets.Count}")), AvailableHealers = all.SelectMany(f => f.Heals).Where(e => Matches(e, filter with { Healer = "", Recipient = "", Ability = "", Kind = "", LifetapOnly = false, CriticalOnly = false })).Select(e => e.Healer).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), AvailableRecipients = all.SelectMany(f => f.Heals).Where(e => Matches(e, filter with { Healer = "", Recipient = "", Ability = "", Kind = "", LifetapOnly = false, CriticalOnly = false })).Select(e => e.Recipient).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), OutsideEvents = outside.Where(e => e.EncounterId.Length == 0 && Matches(e, filter)).Select(e => e.Copy()).ToList(), TotalsAvailable = all.All(f => f.HealingCoverage == "Complete" && (f.HealingDetailAvailable || !contentFilter && !(filter.From > f.Start || filter.Until < f.End))) };
    }
}
