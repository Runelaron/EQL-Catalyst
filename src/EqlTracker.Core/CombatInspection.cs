using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public enum CombatLabMode { Damage, Healing }
public enum CombatLabTab { Breakdown, Metrics, Group, History, Comparison }
public enum ChartPeakMode { Off, Highest, All }
public sealed class CombatLabPreferences
{
    public CombatLabMode Mode { get; set; }
    public CombatLabTab DamageTab { get; set; }
    public CombatLabTab HealingTab { get; set; }
    public ChartPeakMode Peaks { get; set; } = ChartPeakMode.Highest;
    public bool CompactOverviewExpanded { get; set; }
    public bool ShowContribution { get; set; } = true;
}
public sealed record AbilityOutcome(string Id, DateTime Time, string Actor, string Target, string Ability, string Outcome, string Raw);
public static class AbilityOutcomeParser
{
    private static readonly Regex self = new(@"^(?<target>.+?) resisted your (?<spell>.+)!$", RegexOptions.Compiled);
    private static readonly Regex other = new(@"^(?<target>.+?) resisted (?<actor>[^\s]+)'s (?<spell>.+)!$", RegexOptions.Compiled);
    public static AbilityOutcome? Parse(string message, DateTime time, string character, string id)
    {
        if (!message.EndsWith('!') || !message.Contains(" resisted ", StringComparison.Ordinal)) return null;
        if (message.Contains(" tells ", StringComparison.Ordinal) || message.Contains(" says,", StringComparison.Ordinal) || message.Contains(" shouts,", StringComparison.Ordinal)) return null;
        var match = self.Match(message); bool own = match.Success; if (!own) match = other.Match(message);
        return match.Success ? new(id, time, own ? character : match.Groups["actor"].Value, match.Groups["target"].Value, match.Groups["spell"].Value, "Full resist", message) : null;
    }
}
public sealed record SpellReference(string Name, string Url, long Revision, DateTime FetchedAt, string Classes, string Description, string Effects, string Details, string Limitation)
{
    public int ParserVersion { get; init; }
    public Dictionary<string,string> Fields { get; init; }=[];
    public static string BaseName(string ability) => Regex.Replace(ability.Trim(), @"\s+(?:[IVX]+|\d+)(?:\s*\+\d+)?$", "", RegexOptions.IgnoreCase);
    public static SpellReference? Parse(string title, string text, long revision)
    {
        if (!text.Contains("{{Spellpage", StringComparison.OrdinalIgnoreCase)) return null;
        var fields=EffectReferences.Fields(text);
        string Field(string name)=>fields.GetValueOrDefault(name,"");
        static string Plain(string value)=>EffectReferences.Plain(value);
        string description = Plain(Field("description")), effects = Plain(Field("slots"));
        string limitation = "Base-spell reference; rank, focus, caster level and target modifiers are not an expected-damage formula.";
        var amount = Regex.Match(description, @"(?:siphoning|inflicting|healing|for)\s+(\d+)\s+(?:hit points|damage)", RegexOptions.IgnoreCase);
        if (amount.Success && effects.Length > 0 && !Regex.IsMatch(effects, @"\b" + amount.Groups[1].Value + @"\b")) limitation = "Conflicting source values: overview and effect table differ. Expected damage is unresolved. " + limitation;
        var details = new[] { "mana", "casting_time", "recast_time", "duration", "target_type", "resist" }.Select(k => (k, Value: Plain(Field(k)))).Where(x => x.Value.Length > 0).Select(x => x.k.Replace('_', ' ') + ": " + x.Value);
        return new(title, "https://eqlwiki.com/" + Uri.EscapeDataString(title.Replace(' ', '_')), revision, DateTime.UtcNow, Plain(Field("classes")), description, effects, string.Join(" · ", details), limitation) { ParserVersion=EffectReferences.ParserVersion,Fields=fields.ToDictionary(p=>p.Key,p=>Plain(p.Value),StringComparer.OrdinalIgnoreCase) };
    }
}
public sealed record InspectionLine(string Label, string Value);
public sealed record AbilityInspection(string Key, string Ability, string Kind, IReadOnlyList<InspectionLine> Lines, SpellReference? Reference);
public sealed record GroupMemberRow(string Id, string Name, long? Total, double? Rate, double? Share, int Count, string Evidence, IReadOnlyList<DamageRow> Abilities, IReadOnlyList<InspectionLine> Details)
{ public double Seconds { get; init; } public long? CharacterTotal { get; init; } public long? PetTotal { get; init; } }
public sealed record CombatInspectionSnapshot(string Key, IReadOnlyList<DamageRow> Rows, IReadOnlyDictionary<string, AbilityInspection> Abilities, IReadOnlyList<GroupMemberRow> Members, string Coverage)
{ public IReadOnlyList<string> Healers { get; init; } = []; public IReadOnlyList<string> Recipients { get; init; } = []; }
public static class CombatInspection
{
    public static string Key(DamageRow row) => row.Kind + "\0" + row.Ability + "\0" + row.Channel;
    private static string Range(IEnumerable<long> values) { var v = values.ToArray(); return v.Length == 0 ? "Not observed" : $"{v.Min():N0}–{v.Max():N0} · avg {v.Average():N1} ({v.Length:N0} records)"; }
    public static CombatInspectionSnapshot Build(DpsViewState damage, HealingViewState? healing, CharacterProfile profile, IReadOnlyList<AbilityOutcome> outcomes, IReadOnlyDictionary<string, SpellReference> references, string key, bool healingMode, string outcomeCoverage, bool includeGroup = true, bool includeAbilities = true, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var filter = damage.Filter; var fights = damage.Fights; double seconds = Math.Max(1, damage.Seconds);
        var intervals = new List<(DateTime Start, DateTime End)>();
        foreach (var f in fights.OrderBy(f => f.Start)) { if (intervals.Count > 0 && f.Start <= intervals[^1].End) intervals[^1] = (intervals[^1].Start, f.End > intervals[^1].End ? f.End : intervals[^1].End); else intervals.Add((f.Start, f.End)); }
        var starts = intervals.Select(i => i.Start.Ticks).ToArray();
        bool InTime(DateTime at) { cancellation.ThrowIfCancellationRequested(); if (filter.From > at || filter.Until < at) return false; int i = Array.BinarySearch(starts, at.Ticks); if (i < 0) i = ~i - 1; return i >= 0 && at <= intervals[i].End; }
        string Identity(string actor, DateTime time) => CombatIdentity.Same(actor, profile.Character) || CombatIdentity.Same(actor, "you") ? "Self" : profile.PetIdentities.Any(p => CombatIdentity.Same(p.Name, actor) && time >= p.From.AddSeconds(-2) && (!p.Until.HasValue || time < p.Until)) ? "Pet" : profile.GroupMembers.FirstOrDefault(m => m.Contains(actor, time)) is { } member ? member.Name : "";
        var selectedOutcomes = outcomes.Where(o => InTime(o.Time) && (filter.Target.Length == 0 || CombatIdentity.Same(filter.Target, o.Target)) && (filter.Ability.Length == 0 || o.Ability.Contains(filter.Ability, StringComparison.OrdinalIgnoreCase)) && filter.Kind is "" or "Spell").ToList();
        var ownOutcomes = selectedOutcomes.Where(o => filter.Owner == "Combined" ? Identity(o.Actor, o.Time) is "Self" or "Pet" : filter.Owner == "Player" ? Identity(o.Actor, o.Time) == "Self" : filter.Owner == "Pet" && Identity(o.Actor, o.Time) == "Pet").ToList();
        var healRows = healing?.Rows.Select(r => new DamageRow(r.Ability, "Healing", r.Effective, r.Hps, r.Count, r.MaxHeal, r.Average, 0, r.CriticalPercent, 0, r.Percent, "Healing", r.PeakTime) { Unit = "hps" }).ToList() ?? [];
        var rows = healingMode ? healRows : damage.Rows.ToList();
        if (!healingMode && !filter.CriticalOnly) foreach (var g in ownOutcomes.GroupBy(o => o.Ability)) if (!rows.Any(r => r.Ability == g.Key)) rows.Add(new(g.Key, "Spell", 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var inspections = new Dictionary<string, AbilityInspection>();
        var selectedEvents = (includeAbilities && !healingMode ? CombatMetrics.SelectEvents(fights, filter) : new List<CombatEvent>()).ToLookup(e => (e.Ability, e.Kind, Channel: DamageChannels.From(e)));
        var selectedHeals = (healing?.Events ?? []).ToLookup(h => h.Ability);
        foreach (var row in includeAbilities ? rows : [])
        {
            cancellation.ThrowIfCancellationRequested();
            var lines = new List<InspectionLine> { new("Scope", $"{profile.Meter.Scope} · {damage.Seconds:N1}s combat denominator"), new("Recorded ability", row.Ability) };
            if (row.Kind != "Melee") { var rank = Regex.Match(row.Ability, @"\s([IVX]+|\d+)(?:\s*\+\d+)?$"); lines.Add(new("Logged spell rank", rank.Success ? rank.Groups[1].Value : "Not stated")); lines.Add(new("Caster level", "Not recorded per combat event; class-required levels below describe the base spell.")); }
            if (healingMode)
            {
                var events = selectedHeals[row.Ability].ToList();
                lines.AddRange([new("Effective healing", $"{row.Damage:N0} HP · {row.Dps:N1} HPS"), new("Heals", (row.Hits > 0 ? $"{row.Hits:N0} · {row.CriticalPercent:N1}% critical" : "0 landed records · critical percentage unavailable")), new("Normal range", Range(events.Where(e => !e.Critical).Select(e => e.Effective))), new("Critical range", Range(events.Where(e => e.Critical).Select(e => e.Effective))), new("Type", $"{events.Count(e => !e.OverTime)} direct · {events.Count(e => e.OverTime)} HoT ticks"), new("Known overheal", events.Any(e => e.Raw.HasValue) ? $"{events.Sum(e => e.Overheal ?? 0):N0} HP · {events.Count(e => e.Raw.HasValue)}/{events.Count} raw amounts known" : "Unknown · raw amounts not logged")]);
                lines.Add(new("Recipients", string.Join(" · ", events.GroupBy(e => e.Recipient).OrderByDescending(g => g.Sum(e => e.Effective)).Take(8).Select(g => $"{g.Key}: {g.Sum(e => e.Effective):N0}"))));
            }
            else
            {
                var events = selectedEvents[(row.Ability, row.Kind, row.Channel)].ToList(); var hits = events.Where(e => e.Outcome == "Hit").ToList();
                lines.AddRange([new("Damage", $"{row.Damage:N0} · {row.Dps:N1} DPS · {row.Percent:N1}% share"), new("Hits", (row.Hits > 0 ? $"{row.Hits:N0} · {row.CriticalPercent:N1}% critical" : "0 landed records · critical percentage unavailable")), new("Normal range", Range(hits.Where(e => !e.Critical).Select(e => e.Damage))), new("Critical range", Range(hits.Where(e => e.Critical).Select(e => e.Damage))), new("Avoidance", string.Join(" · ", events.Where(e => e.Outcome != "Hit").GroupBy(e => e.Outcome).Select(g => $"{g.Key}: {g.Count():N0}")) is { Length: > 0 } avoidance ? avoidance : "No logged melee avoidance")]);
                lines.Add(new("Full resists", filter.CriticalOnly ? "Not comparable under Critical only; clear that filter to see logged failures." : $"{ownOutcomes.Count(o => o.Ability == row.Ability):N0} recorded · {outcomeCoverage}"));
                lines.Add(new("Resistance amount", row.Kind == "Melee" ? "Not applicable" : "Unknown · lower hits alone do not establish partial resistance. Counts are logged outcomes, not cast success rates."));
                lines.Add(new("Targets", string.Join(" · ", hits.GroupBy(e => e.Target).OrderByDescending(g => g.Sum(e => e.Damage)).Take(8).Select(g => $"{g.Key}: {g.Sum(e => e.Damage):N0}"))));
                if (row.LinkedHealing.HasValue) lines.Add(new("Linked healing", $"{row.LinkedHealing:N0} HP restored · {row.LinkedHealCount} verified pairs"));
            }
            lines.Add(new("Largest event", row.PeakTime.HasValue ? $"{row.MaxHit:N0} at {row.PeakTime:MMM/dd HH:mm:ss}" : "Unavailable"));
            references.TryGetValue(IdentityKey(row.Ability), out var reference);
            if (row.Kind != "Melee" && reference == null) lines.Add(new("Spell reference", "Not cached or not verified · observed data remains available."));
            inspections[Key(row)] = new(Key(row), row.Ability, row.Kind, lines, reference);
        }
        if (!includeGroup) return new(key, rows, inspections, [], "");
        var memberEvents = healingMode ? new List<CombatEvent>() : fights.SelectMany(f => f.Events).Where(e => InTime(e.Time) && Identity(e.Actor, e.Time).Length > 0 && Identity(e.Target, e.Time).Length == 0 && (filter.Target.Length == 0 || CombatIdentity.Same(e.Target, filter.Target)) && (filter.Ability.Length == 0 || e.Ability.Contains(filter.Ability, StringComparison.OrdinalIgnoreCase)) && (filter.Kind.Length == 0 || e.Kind == filter.Kind || filter.Kind == "Spell" && e.Kind == "DoT") && (!filter.CriticalOnly || e.Critical)).ToList();
        var hf = profile.Healing.Filter(filter.From, filter.Until);
        var memberHeals = !healingMode ? new List<HealingEvent>() : fights.SelectMany(f => f.Heals).DistinctBy(h => h.Id).Where(h => InTime(h.Time) && Identity(hf.Direction == "Received" ? h.Recipient : h.Healer, h.Time).Length > 0 && (hf.Healer.Length == 0 || CombatIdentity.Same(hf.Healer, h.Healer)) && (hf.Recipient.Length == 0 || CombatIdentity.Same(hf.Recipient, h.Recipient)) && (hf.Ability.Length == 0 || h.Ability.Contains(hf.Ability, StringComparison.OrdinalIgnoreCase)) && (hf.Kind.Length == 0 || h.OverTime == (hf.Kind == "HoT")) && (!hf.CriticalOnly || h.Critical) && (!hf.LifetapOnly || h.Lifetap)).ToList();
        var members = new List<GroupMemberRow>();
        long total = healingMode ? memberHeals.Sum(h => h.Effective) : memberEvents.Sum(e => e.Damage);
        bool InRoster(GroupMembership m) => damage.Session is { } s ? m.From <= (filter.Until ?? s.LastObserved) && (!m.Until.HasValue || m.Until > (filter.From ?? s.Start)) : fights.Any(f => m.From <= f.End && (!m.Until.HasValue || m.Until > f.Start));
        var names = new[] { "Self", "Pet" }.Concat(profile.GroupMembers.Where(InRoster).Select(m => m.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            cancellation.ThrowIfCancellationRequested();
            var es = memberEvents.Where(e => Identity(e.Actor, e.Time).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList(); var hs = memberHeals.Where(h => Identity(hf.Direction == "Received" ? h.Recipient : h.Healer, h.Time).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            long amount = healingMode ? hs.Sum(h => h.Effective) : es.Sum(e => e.Damage); int count = healingMode ? hs.Count : es.Count;
            var abilities = healingMode ? hs.GroupBy(h => h.Ability).Select(g => new DamageRow(g.Key, "Healing", g.Sum(h => h.Effective), g.Sum(h => h.Effective) / seconds, g.Count(), g.Max(h => h.Effective), g.Average(h => h.Effective), g.Count(h => h.Critical), 100d * g.Count(h => h.Critical) / g.Count(), 0, amount > 0 ? 100d * g.Sum(h => h.Effective) / amount : 0, "Healing") { Unit = "hps" }).ToList() : CombatMetrics.Breakdown([new Encounter { Start = fights.Select(f => f.Start).DefaultIfEmpty(default).Min(), End = fights.Select(f => f.Start).DefaultIfEmpty(default).Min().AddSeconds(seconds), Events = es.Select(e => new CombatEvent { Id = e.Id, Time = e.Time, Actor = e.Actor, Owner = "Player", Target = e.Target, Kind = e.Kind, Ability = e.Ability, DamageType = e.DamageType, Damage = e.Damage, Critical = e.Critical, Outcome = e.Outcome }).ToList() }], new("Player"));
            var details = healingMode ? hs.GroupBy(h => hf.Direction == "Received" ? h.Healer : h.Recipient).Select(g => new InspectionLine(g.Key, $"{g.Sum(h => h.Effective):N0} HP · {g.Count()} heals")).ToList() : es.GroupBy(e => e.Target).Select(g => new InspectionLine(g.Key, $"{g.Sum(e => e.Damage):N0} damage · {g.Count()} records")).ToList();
            string evidence = name is "Self" or "Pet" ? "Character / owned-pet identity at event time" : string.Join(" / ", profile.GroupMembers.Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && InRoster(m)).Select(m => m.Source).Distinct()) + " · membership at event time";
            members.Add(new(name.ToLowerInvariant(), name, count > 0 ? amount : null, count > 0 ? amount / seconds : null, count > 0 && total > 0 ? amount * 100d / total : null, count, evidence, abilities.OrderByDescending(r => r.Damage).ToArray(), details));
        }
        string coverage = "Observed group results · log range and client filters may omit events. Self/Pet totals are unchanged. " + (fights.Any(f => healingMode ? !f.HealingDetailAvailable : !f.DetailAvailable) ? "Historical member details unavailable for summary-only encounters." : "No logged events means unknown coverage, not verified zero.");
        return new(key, rows, inspections, members.OrderByDescending(m => m.Total ?? -1).ToArray(), coverage)
        { Healers = fights.SelectMany(f => f.Heals).Select(h => h.Healer).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(), Recipients = fights.SelectMany(f => f.Heals).Select(h => h.Recipient).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() };
    }
    public static string IdentityKey(string ability) => Identity.Key(SpellReference.BaseName(ability.Replace(" · Lifetap", "")));
}
