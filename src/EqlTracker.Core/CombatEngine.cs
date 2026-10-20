namespace EqlTracker.Core;

public sealed class CombatEngine(CharacterProfile profile)
{
    public List<Encounter> Encounters { get; } = [];
    public Encounter? Current { get; private set; }
    public bool RosterKnown { get; set; }
    public PetElementDetector Elements { get; } = new();
    public HealingLedger Healing { get; } = new(profile);
    public long Revision { get; private set; }
    private readonly RecentIds processed = new();
    public event Action<Encounter>? Saved;
    public event Action<CombatEvent>? EventAdded;
    public event Action? ProfileChanged;
    public void Restore(IEnumerable<Encounter> fights, Encounter? current)
    { Encounters.AddRange(fights.Where(x => x.Finished)); Current = current; }

    public void Process(ParsedLine line)
    {
        if (line.Boundary) Finish("Zone, death or session boundary");
        if (line.JoinedGroup || line.LeftGroup || line.GroupRosterReset)
        {
            foreach (var member in profile.GroupMembers.Where(x => x.Until == null))
                if (!line.JoinedGroup || member.Source != "Accepted invitation" || Math.Abs((line.Time - member.From).TotalSeconds) > 2) member.Until = line.Time;
            profile.InGroup = line.JoinedGroup; profile.GroupRosterIncomplete = line.JoinedGroup || line.GroupRosterReset; ProfileChanged?.Invoke();
        }
        if (line.GroupJoined is { } joined)
        {
            profile.InGroup = true; profile.GroupRosterIncomplete = true;
            if (!profile.GroupMembers.Any(x => x.Contains(joined, line.Time))) profile.GroupMembers.Add(new() { Name = joined, From = line.Time });
            ProfileChanged?.Invoke();
        }
        if (line.AcceptedInviter is { } inviter)
        {
            profile.InGroup = true; profile.GroupRosterIncomplete = true;
            if (!profile.GroupMembers.Any(x => x.Contains(inviter, line.Time))) profile.GroupMembers.Add(new() { Name = inviter, From = line.Time, Source = "Accepted invitation" });
            ProfileChanged?.Invoke();
        }
        if (line.GroupLeft is { } left) { foreach (var member in profile.GroupMembers.Where(x => x.Name.Equals(left, StringComparison.OrdinalIgnoreCase) && x.Until == null)) member.Until = line.Time; ProfileChanged?.Invoke(); }
        if (line.PetLost || line.SlainActor != null)
        {
            foreach (var identity in profile.PetIdentities.Where(x => x.Until == null && (line.PetLost || x.Name == line.SlainActor))) identity.Until = line.Time;
        }
        if (line.Zone != null) { profile.Zone = line.Zone; ProfileChanged?.Invoke(); }
        if (line.Classes != null)
        {
            if (!profile.ClassesObservedAt.HasValue || line.Time > profile.ClassesObservedAt) profile.ClassesObservedAt = line.Time;
            profile.CurrentClasses = line.Classes; profile.ActiveLoadout.Classes = line.Classes.ToList(); RosterKnown = true;
            if (line.Level >= 50) foreach (var c in line.Classes) profile.Level50Classes.Add(c);
            ProfileChanged?.Invoke();
        }
        if (line.Level is { } level)
        {
            profile.ActiveLoadout.Level = level;
            if (level >= 50 && RosterKnown) foreach (var c in profile.CurrentClasses) profile.Level50Classes.Add(c);
            ProfileChanged?.Invoke();
        }
        if (line.PetName is { } name)
        {
            var current = profile.PetIdentities.LastOrDefault(x => x.Until == null);
            if (current?.Name != name)
            {
                if (current != null) current.Until = line.Time;
                profile.PetIdentities.Add(new() { Name = name, From = line.Time, Confirmed = false });
                profile.ActiveLoadout.PetName = name; ProfileChanged?.Invoke(); foreach (var affected in Healing.Reattribute(Encounters.Concat(Current == null ? [] : [Current]))) { Saved?.Invoke(affected); Revision++; }
                // Attack messages can follow the pet's first hits in the same second.
                if (Current != null)
                    foreach (var e in Current.Events.Where(e => e.Actor == name && e.Time >= line.Time.AddSeconds(-2) && e.Owner == "Unknown"))
                    { e.Owner = "Pet"; Current.UnknownDamage -= e.Damage; Current.PetDamage += e.Damage; if (e.Time > Current.End) Current.End = e.Time; EventAdded?.Invoke(e); }
            }
        }
        if (Elements.Process(line, profile)) ProfileChanged?.Invoke();
        if (line.SlainActor is { } slain && Current != null) { Current.ResolvedTargets.Add(CombatIdentity.Key(slain)); Revision++; }
        if (line.Healing is { } heal)
        {
            Healing.Accept(heal, profile.Zone, Current);
            if (Current != null && heal.EncounterId == Current.Id) Saved?.Invoke(Current);
        }
        if (line.Combat is not { } damage) return;
        damage.Zone = profile.Zone;
        if (damage.Owner != "Player" && profile.PetIdentities.Any(p => p.Name.Equals(damage.Actor, StringComparison.OrdinalIgnoreCase) && damage.Time >= p.From.AddSeconds(-2) && (!p.Until.HasValue || damage.Time < p.Until))) damage.Owner = "Pet";
        bool own = damage.Owner is "Player" or "Pet";
        bool group = profile.GroupMembers.Any(x => x.Contains(damage.Actor, damage.Time));
        bool incoming = damage.Target.Equals(profile.Character, StringComparison.OrdinalIgnoreCase) || damage.Target.Equals("you", StringComparison.OrdinalIgnoreCase) || profile.PetIdentities.Any(p => p.Name == damage.Target && damage.Time >= p.From && (!p.Until.HasValue || damage.Time < p.Until)) || profile.GroupMembers.Any(x => x.Contains(damage.Target, damage.Time));
        bool unattributed = damage.Actor == "Unattributed";
        bool sharedTarget = Current?.Events.Any(x => CombatIdentity.Same(x.Target, damage.Target) && x.Owner is "Pet" or "Player") == true;
        if (!own && !incoming && !group && !unattributed && !sharedTarget) return;
        if (!processed.Accept(damage.Id)) return;
        if (Current != null && (damage.Time - Current.End).TotalSeconds > profile.EncounterTimeoutSeconds)
        {
            double gap = (damage.Time - Current.End).TotalSeconds;
            bool unresolved = !Current.ResolvedTargets.Contains(CombatIdentity.Key(damage.Target)) &&
                Current.Events.Any(x => !x.ActivityOnly && (x.Owner is "Player" or "Pet") && CombatIdentity.Same(x.Target, damage.Target));
            if (incoming && !unresolved) unresolved = !Current.ResolvedTargets.Contains(CombatIdentity.Key(damage.Actor)) &&
                Current.Events.Any(x => !x.ActivityOnly && (x.Owner is "Player" or "Pet") && CombatIdentity.Same(x.Target, damage.Actor));
            if (gap > Math.Max(profile.EncounterTimeoutSeconds, profile.EncounterResumeSeconds) || !unresolved)
                Finish(gap > profile.EncounterResumeSeconds ? "Resume window expired" : "Different pull after idle");
            else if (own || incoming || group) { Current.IdleResumable = false; Current.ResumeCount++; }
        }
        if (Current == null && !own && !incoming && !group) return;
        Current ??= new() { Start = damage.Time, End = damage.Time, Zone = profile.Zone, Loadout = profile.ActiveLoadout.Name, PetType = profile.EffectivePetElement, PetName = profile.ActiveLoadout.PetName, PetElementEvidence = profile.PetIdentities.LastOrDefault(x => x.Until == null)?.ElementEvidence ?? "", Weapons = profile.ActiveLoadout.Weapons, BuffNotes = profile.ActiveLoadout.BuffNotes };
        // Nearby actors may provide ambiguous evidence, but only our party extends duration.
        if ((own || incoming || group) && damage.Time > Current.End) Current.End = damage.Time;
        damage.Relevance = incoming ? "Incoming" : own ? damage.Owner : group ? "Group" : "Unattributed";
        if (incoming || group) { damage.ActivityOnly = true; damage.Owner = incoming ? "Incoming" : "Group"; }
        Revision++; Current.Events.Add(damage);
        if (damage.Owner == "Player") Current.PlayerDamage += damage.Damage;
        else if (damage.Owner == "Pet") Current.PetDamage += damage.Damage;
        else if (!damage.ActivityOnly) Current.UnknownDamage += damage.Damage;
        if (own && !damage.ActivityOnly && damage.Outcome == "Hit")
        {
            string channel = DamageChannels.From(damage); var peak = Current.Peaks.FirstOrDefault(x => x.Actor == damage.Owner && x.Ability == damage.Ability && x.Channel == channel);
            if (peak == null || damage.Damage > peak.Damage || damage.Damage == peak.Damage && damage.Time < peak.Time)
            { if (peak != null) Current.Peaks.Remove(peak); Current.Peaks.Add(new(damage.Owner, damage.Ability, channel, damage.Damage, damage.Time, Current.Id)); }
        }
        if (Current.HealingCoverage == "Unavailable") Current.HealingCoverage = "Complete";
        Healing.Attach(Current);
        EventAdded?.Invoke(damage);
    }
    public void Tick(DateTime now)
    {
        if (Current == null) return;
        double idle = (now - Current.End).TotalSeconds;
        if (idle > Math.Max(profile.EncounterTimeoutSeconds, profile.EncounterResumeSeconds)) Finish("Resume window expired");
        else if (idle > profile.EncounterTimeoutSeconds && !Current.IdleResumable)
        { Current.IdleResumable = true; Revision++; Saved?.Invoke(Current); }
    }
    public void Finish(string reason = "Explicit boundary")
    {
        if (Current == null) return;
        Current.Finished = true; Current.IdleResumable = false; Current.EndReason = reason; Current.Peaks = CombatMetrics.Peaks([Current], new("Combined")); Encounters.Add(Current); Saved?.Invoke(Current); Current = null; Revision++;
    }
    public double RollingDps(string owner, DateTime now) => Current?.Events.Where(x => x.Time > now.AddSeconds(-5) && x.Time <= now && (owner == "Combined" ? x.Owner is "Pet" or "Player" : x.Owner == owner)).Sum(x => x.Damage) / 5d ?? 0;
    public double RollingDps(CombatFilter filter, DateTime now) => Current?.Events.Where(x => CombatMetrics.Matches(x, filter) && x.Time > now.AddSeconds(-5) && x.Time <= now).Sum(x => x.Damage) / 5d ?? 0;
    public void ConfirmPet(string name, DateTime from, DateTime? until)
    {
        Revision++;
        profile.PetIdentities.Add(new() { Name = name, From = from, Until = until, Confirmed = true });
        foreach (var fight in Encounters.Concat(Current == null ? [] : [Current]))
        {
            foreach (var e in fight.Events.Where(x => !x.ActivityOnly && x.Actor == name && x.Time >= from && (!until.HasValue || x.Time < until))) e.Owner = "Pet";
            Recount(fight); Saved?.Invoke(fight);
        }
        Healing.Reattribute(Encounters.Concat(Current == null ? [] : [Current]));
        foreach (var fight in Encounters.Concat(Current == null ? [] : [Current])) Saved?.Invoke(fight);
        ProfileChanged?.Invoke();
    }
    public static void Recount(Encounter fight)
    { fight.PlayerDamage = fight.Events.Where(x => !x.ActivityOnly && x.Owner == "Player").Sum(x => x.Damage); fight.PetDamage = fight.Events.Where(x => !x.ActivityOnly && x.Owner == "Pet").Sum(x => x.Damage); fight.UnknownDamage = fight.Events.Where(x => !x.ActivityOnly && x.Owner == "Unknown").Sum(x => x.Damage); fight.Peaks = CombatMetrics.Peaks([fight], new("Combined")); }
}
public sealed record CombatFilter(string Owner = "Combined", string Target = "", string Ability = "", string Kind = "", bool CriticalOnly = false, DateTime? From = null, DateTime? Until = null);
public sealed record DamageRow(string Ability, string Kind, long Damage, double Dps, int Hits, long MaxHit, double Average, int Criticals, double CriticalPercent, int Misses, double Percent, string Channel = "Unknown", DateTime? PeakTime = null)
{
    public bool MonkKick { get; init; }
 public string Unit { get; init; } = "dps"; public string? CustomTooltip { get; init; } public long? LinkedHealing { get; init; } public int LinkedHealCount { get; init; } }
public sealed record PeakHit(string Actor, string Ability, string Channel, long Damage, DateTime Time, string EncounterId);
public sealed record TargetRow(string Target, long Damage, double Dps, double Percent, int Hits, long MaxHit, double Average, double CriticalPercent, long MeleeDamage, long SpellDamage)
{ public string Details => $"{Dps:N1} dps · {Percent:N1}% share\n{Hits:N0} hits · {CriticalPercent:N1}% crit · avg {Average:N1} · max {MaxHit:N0}\nMelee {MeleeDamage:N0} · Spells {SpellDamage:N0}"; }
public static class DamageChannels
{
    public static string From(CombatEvent e) => e.Kind == "Melee" ? "Physical" : e.DamageType.Trim().ToLowerInvariant() switch { "magic" => "Magic", "cold" => "Cold", "fire" => "Fire", "disease" => "Disease", "poison" => "Poison", "corruption" => "Corruption", _ => "Unknown" };
}
public static class CombatMetrics
{
    public static bool Matches(CombatEvent e, CombatFilter filter) => !e.ActivityOnly && (filter.Owner == "Combined" ? e.Owner is "Player" or "Pet" : e.Owner == filter.Owner) && (filter.Target == "" || CombatIdentity.Same(e.Target, filter.Target)) && (filter.Ability == "" || e.Ability.Contains(filter.Ability, StringComparison.OrdinalIgnoreCase)) && (filter.Kind == "" || e.Kind == filter.Kind || filter.Kind == "Spell" && e.Kind == "DoT") && (!filter.CriticalOnly || e.Critical) && (!filter.From.HasValue || e.Time >= filter.From) && (!filter.Until.HasValue || e.Time <= filter.Until);
    public static List<CombatEvent> SelectEvents(IEnumerable<Encounter> fights, CombatFilter filter) => fights.Where(x => (!filter.From.HasValue || x.End >= filter.From) && (!filter.Until.HasValue || x.Start <= filter.Until)).SelectMany(x => x.Events).Where(x => Matches(x, filter)).ToList();
    public static double Duration(IEnumerable<Encounter> fights, CombatFilter filter) => fights.Where(x => (!filter.From.HasValue || x.End >= filter.From) && (!filter.Until.HasValue || x.Start <= filter.Until)).Sum(f => Math.Max(1, ((filter.Until.HasValue && filter.Until < f.End ? filter.Until.Value : f.End) - (filter.From.HasValue && filter.From > f.Start ? filter.From.Value : f.Start)).TotalSeconds));
    public static List<DamageRow> Breakdown(IEnumerable<Encounter> fights, CombatFilter filter)
    {
        var list = fights.Where(x => (!filter.From.HasValue || x.End >= filter.From) && (!filter.Until.HasValue || x.Start <= filter.Until)).ToList();
        double seconds = Duration(list, filter);
        var events = SelectEvents(list, filter);
        var total = events.Sum(x => x.Damage);
        return events.GroupBy(x => (x.Ability, x.Kind, Channel: DamageChannels.From(x))).Select(g =>
        {
            var hits = g.Where(x => x.Outcome == "Hit").ToList(); var damage = g.Sum(x => x.Damage); int crit = hits.Count(x => x.Critical);
            long max = hits.Select(x => x.Damage).DefaultIfEmpty(0).Max();
            return new DamageRow(g.Key.Ability, g.Key.Kind, damage, damage / Math.Max(1, seconds), hits.Count, max, hits.Count == 0 ? 0 : hits.Average(x => x.Damage), crit, hits.Count == 0 ? 0 : crit * 100d / hits.Count, g.Count(x => x.Outcome != "Hit"), total == 0 ? 0 : damage * 100d / total, g.Key.Channel, hits.Where(x => x.Damage == max).OrderBy(x => x.Time).FirstOrDefault()?.Time);
        }).OrderByDescending(x => x.Damage).ToList();
    }
    public static List<TargetRow> Targets(IEnumerable<Encounter> fights, CombatFilter filter)
    {
        var list = fights.ToList(); double seconds = Math.Max(1, Duration(list, filter)); var events = SelectEvents(list, filter); long total = events.Sum(x => x.Damage);
        return events.GroupBy(x => x.Target, StringComparer.OrdinalIgnoreCase).Select(g => { var hits = g.Where(x => x.Outcome == "Hit").ToList(); long damage = g.Sum(x => x.Damage); return new TargetRow(g.Key, damage, damage / seconds, total > 0 ? damage * 100d / total : 0, hits.Count, hits.Select(x => x.Damage).DefaultIfEmpty(0).Max(), hits.Count > 0 ? hits.Average(x => x.Damage) : 0, hits.Count > 0 ? hits.Count(x => x.Critical) * 100d / hits.Count : 0, g.Where(x => x.Kind == "Melee").Sum(x => x.Damage), g.Where(x => x.Kind is "Spell" or "DoT").Sum(x => x.Damage)); }).OrderByDescending(x => x.Damage).ToList();
    }
    public static List<PeakHit> Peaks(IEnumerable<Encounter> fights, CombatFilter filter)
    {
        var all = fights.ToList();
        var peaks = all.Where(x => x.DetailAvailable).SelectMany(f => f.Events.Where(e => Matches(e, filter) && e.Outcome == "Hit").Select(e => new PeakHit(e.Owner, e.Ability, DamageChannels.From(e), e.Damage, e.Time, f.Id))).ToList();
        if (filter.Target == "" && filter.Ability == "" && filter.Kind == "" && !filter.CriticalOnly)
            peaks.AddRange(all.Where(x => !x.DetailAvailable && (!filter.From.HasValue || x.Start >= filter.From) && (!filter.Until.HasValue || x.End <= filter.Until)).SelectMany(x => x.Peaks).Where(x => filter.Owner == "Combined" ? x.Actor is "Pet" or "Player" : x.Actor == filter.Owner));
        return peaks.GroupBy(x => (x.Actor, x.Ability, x.Channel)).Select(g => g.OrderByDescending(x => x.Damage).ThenBy(x => x.Time).First()).OrderByDescending(x => x.Damage).ToList();
    }
}
