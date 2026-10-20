using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public enum OverlayCombatMode { Personal, Group }
public enum OverlayMenuSize { Compact, Comfortable }
public enum PartyKind { Group, Raid }
public sealed record PartySignal(string Id, DateTime Time, string Evidence, bool Inferred = false);
public sealed class GroupPeriod
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime LastObserved { get; set; }
    public DateTime? End { get; set; }
    public bool InferredStart { get; set; }
    public bool UncertainEnd { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Live { get; set; }
    public List<GroupMembership> Members { get; set; } = [];
    public List<PartySignal> RaidEvidence { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public PartyKind Kind => RaidEvidence.Count > 0 ? PartyKind.Raid : PartyKind.Group;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => $"{(InferredStart?"~":"")}{Start:MMM/dd HH:mm} · {Kind} · {1+Members.Where(m => !m.IsPet).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()} known · {(End.HasValue ? UncertainEnd ? "last observed" : "ended" : Live ? "live" : "last observed")} {LastObserved:HH:mm}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Description => $"{Label}\n{(InferredStart?"Start inferred from first retained grouping evidence":"Observed join boundary")}\n{(UncertainEnd?"End uncertain; last observed activity":End.HasValue?"Observed leave/disband boundary":"No verified leave; last observed activity")}\n"+string.Join("\n",RaidEvidence.GroupBy(e=>e.Evidence).Select(g=>$"{g.Min(e=>e.Time):HH:mm:ss} · {g.Key} · {g.Count()} observations"));
    public bool Overlaps(DateTime from, DateTime until) => Start <= until && (End ?? LastObserved) >= from;
}

// A separate evidence index: reading it never invokes CombatEngine or ingestion.
public sealed class GroupingEvidence
{
    public const int Version = 1;
    public int ParserVersion { get; set; } = Version;
    public FileCheckpoint? Checkpoint { get; set; }
    public List<GroupPeriod> Periods { get; set; } = [];
    public string SessionId { get; set; } = "";
    public string Generation { get; set; } = "";
    public long Revision { get; set; }
    private static readonly Regex Join = new(@"^(\S+) has joined the group\.$", RegexOptions.Compiled);
    private static readonly Regex Leave = new(@"^(\S+) has left the group\.$", RegexOptions.Compiled);
    private static readonly Regex Accepted = new(@"^You notify (\S+) that you agree to join the group\.$", RegexOptions.Compiled);
    private static readonly Regex RaidChat = new(@"^(\S+) tells the raid, '", RegexOptions.Compiled);
    public void Observe(string raw, string id)
    {
        int close = raw.IndexOf("] ", StringComparison.Ordinal);
        if (close < 0 || !DateTime.TryParseExact(raw[1..close], ["ddd MMM dd HH:mm:ss yyyy", "ddd MMM d HH:mm:ss yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time)) return;
        time = DateTime.SpecifyKind(time, DateTimeKind.Local);
        string text = raw[(close + 2)..], generation = id.Split(':')[0];
        var active = Periods.LastOrDefault(p => !p.End.HasValue);
        bool login = text is "Welcome to EverQuest Legends!" or "Welcome to EverQuest!";
        if (Generation != generation || login)
        {
            if (SessionId == id) return;
            if (active != null) { Close(active, active.LastObserved, true); active = null; }
            SessionId = id; Generation = generation; Revision++;
        }
        DateTime priorActivity=active?.LastObserved??time;
        if (active != null && time > active.LastObserved) active.LastObserved = time;
        if (text is "You have left the group." or "You are no longer in a group." or "Your group has been disbanded." or "You have been removed from the group." or "You have disbanded the group.")
        { if (active != null) Close(active, time, false); return; }
        var joined = Join.Match(text); var accepted = Accepted.Match(text); var left = Leave.Match(text); var raidChat = RaidChat.Match(text);
        bool selfJoin = text == "You have joined the group.";
        bool raid = text is "You receive no experience for defeating this creature as you are in a raid." or "You receive no loot for defeating this creature as you are in a raid." || text.StartsWith("You tell your raid, '", StringComparison.Ordinal) || raidChat.Success;
        if(active!=null&&selfJoin&&(time-active.Start).TotalSeconds>2){Close(active,priorActivity,true);active=null;}
        if (active == null && (selfJoin || joined.Success || accepted.Success || raid))
        {
            // A replayed boundary must reuse its stable period ID.
            active = Periods.FirstOrDefault(p => p.Id == id);
            if (active == null) { active = new() { Id = id, SessionId = SessionId, Start = time, LastObserved = time, InferredStart = !selfJoin && !accepted.Success }; Periods.Add(active); Revision++; }
        }
        if (active == null) return;
        if (selfJoin && active.InferredStart && Math.Abs((time - active.Start).TotalSeconds) <= 2) { active.InferredStart = false; Revision++; }
        string name = joined.Success ? joined.Groups[1].Value : accepted.Success ? accepted.Groups[1].Value : "";
        if (name.Length > 0 && !active.Members.Any(m => !m.PointOnly && m.Contains(name, time)))
        { active.Members.Add(new() { Name = name, From = time, Source = accepted.Success ? "Accepted invitation" : "Logged join" }); Revision++; }
        if (left.Success) foreach (var member in active.Members.Where(m => !m.PointOnly && m.Contains(left.Groups[1].Value, time))) { member.Until = time; Revision++; }
        if (raid && !active.RaidEvidence.Any(e => e.Id == id))
        {
            active.RaidEvidence.Add(new(id, time, raidChat.Success ? "Raid-channel participation; roster incomplete" : "Raid-only client message; roster incomplete")); Revision++;
            // A channel header proves presence at this instant, not an interval.
            if (raidChat.Success && !active.Members.Any(m => m.Name.Equals(raidChat.Groups[1].Value, StringComparison.OrdinalIgnoreCase)))
                active.Members.Add(new() { Name = raidChat.Groups[1].Value, From = time, Until = time, PointOnly = true, Source = "Raid-channel observation; membership interval unavailable" });
        }
        if (active.RaidEvidence.Count == 0 && 1 + active.Members.Where(m => !m.IsPet && !m.PointOnly && m.Contains(m.Name, time)).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 4)
        { active.RaidEvidence.Add(new(id, time, "More than four concurrently verified players", true)); Revision++; }
    }
    private void Close(GroupPeriod period, DateTime at, bool uncertain)
    { period.End = at; period.LastObserved = at; period.UncertainEnd = uncertain; foreach (var m in period.Members.Where(m => !m.PointOnly && !m.Until.HasValue)) m.Until = at; Revision++; }
}

public sealed record GroupProjection(string Key, IReadOnlyList<GroupMemberRow> Members, double Seconds, string Coverage)
{
    public long Total => Members.Sum(m => m.Total ?? 0);
    public double Rate => Total / Math.Max(1, Seconds);
    public static GroupProjection Build(DpsViewState view, HealingViewState? healing, CharacterProfile profile, IReadOnlyList<GroupPeriod> periods, string key, CancellationToken cancel = default)
    {
        var f = view.Filter; double seconds = Math.Max(1, view.Seconds); bool heals = healing != null;
        DateTime from=f.From??view.Session?.Start??view.Fights.Select(e=>e.Start).DefaultIfEmpty(DateTime.MaxValue).Min();
        DateTime until=f.Until??view.Session?.LastObserved??view.Fights.Select(e=>e.End).DefaultIfEmpty(DateTime.MinValue).Max();
        var selectedPeriods = periods.Where(p => p.Overlaps(from,until)).ToArray();
        var roster = selectedPeriods.SelectMany(p => p.Members).Concat(view.Session?.Members ?? []).Concat(profile.GroupMembers.Where(m => m.Source == "Manual" || selectedPeriods.Length == 0)).Where(m=>m.From<=until&&(!m.Until.HasValue||m.Until>=from)).ToArray();
        bool Time(DateTime t) => (!f.From.HasValue || t >= f.From) && (!f.Until.HasValue || t <= f.Until);
        bool OwnPet(string actor, DateTime t) => profile.PetIdentities.Any(p => CombatIdentity.Same(p.Name, actor) && t >= p.From.AddSeconds(-2) && (!p.Until.HasValue || t < p.Until));
        bool Member(string actor, DateTime t) => roster.Any(m => !m.IsPet && !m.PointOnly && m.Contains(actor, t));
        string Owner(string actor, DateTime t)
        {
            if (CombatIdentity.Same(actor, profile.Character) || CombatIdentity.Same(actor, "you")) return profile.Character;
            if (OwnPet(actor, t)) return profile.Character;
            var pet = roster.FirstOrDefault(m => m.IsPet && !m.PointOnly && m.Contains(actor, t) && m.Owner.Length > 0 && (CombatIdentity.Same(m.Owner,profile.Character) || Member(m.Owner, t)));
            if (pet != null) return pet.Owner;
            return Member(actor, t) ? roster.First(m => !m.IsPet && m.Contains(actor, t)).Name : "";
        }
        var damage = view.Fights.SelectMany(fight => fight.Events.Where(e=>e.Time>=fight.Start&&e.Time<=fight.End)).DistinctBy(e => e.Id).Where(e => Time(e.Time) && e.Owner != "Incoming" && Owner(e.Actor,e.Time).Length > 0 && Owner(e.Target,e.Time).Length == 0 && (f.Target.Length == 0 || CombatIdentity.Same(e.Target, f.Target)) && (f.Ability.Length == 0 || e.Ability.Contains(f.Ability, StringComparison.OrdinalIgnoreCase)) && (f.Kind.Length == 0 || e.Kind == f.Kind || f.Kind == "Spell" && e.Kind == "DoT") && (!f.CriticalOnly || e.Critical)).ToArray();
        var hf = profile.Healing.Filter(f.From, f.Until);
        string HealActor(HealingEvent e) => hf.Direction == "Received" ? e.Recipient : e.Healer;
        var restored = !heals ? [] : view.Fights.SelectMany(fight => fight.Heals.Where(e=>e.Time>=fight.Start&&e.Time<=fight.End)).DistinctBy(e => e.Id).Where(e => Time(e.Time) && Owner(HealActor(e), e.Time).Length > 0 && (hf.Healer.Length==0 || CombatIdentity.Same(hf.Healer,e.Healer)) && (hf.Recipient.Length==0 || CombatIdentity.Same(hf.Recipient,e.Recipient)) && (hf.Ability.Length==0 || e.Ability.Contains(hf.Ability,StringComparison.OrdinalIgnoreCase)) && (hf.Kind.Length==0 || e.OverTime==(hf.Kind=="HoT")) && (!hf.CriticalOnly || e.Critical) && (!hf.LifetapOnly || e.Lifetap)).ToArray();
        var names = new[] { profile.Character }.Concat(roster.Where(m => !m.IsPet).Select(m => m.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new List<GroupMemberRow>();
        foreach (string name in names)
        {
            cancel.ThrowIfCancellationRequested();
            var es = damage.Where(e => CombatIdentity.Same(Owner(e.Actor,e.Time),name)).ToArray(); var hs = restored.Where(e => CombatIdentity.Same(Owner(HealActor(e),e.Time),name)).ToArray();
            long amount = heals ? hs.Sum(e => e.Effective) : es.Sum(e => e.Damage); int count = heals ? hs.Length : es.Length;
            long self = heals ? hs.Where(e => CombatIdentity.Same(HealActor(e),name)).Sum(e => e.Effective) : es.Where(e => CombatIdentity.Same(e.Actor,name)).Sum(e => e.Damage);
            var abilities = heals ? hs.GroupBy(e => e.Ability).Select(g => new DamageRow(g.Key,"Healing",g.Sum(e=>e.Effective),g.Sum(e=>e.Effective)/seconds,g.Count(),g.Max(e=>e.Effective),g.Average(e=>e.Effective),g.Count(e=>e.Critical),100d*g.Count(e=>e.Critical)/g.Count(),0,amount>0?100d*g.Sum(e=>e.Effective)/amount:0,"Healing"){Unit="hps"}).ToArray() : es.GroupBy(e => (e.Ability,e.Kind)).Select(g=>new DamageRow(g.Key.Ability,g.Key.Kind,g.Sum(e=>e.Damage),g.Sum(e=>e.Damage)/seconds,g.Count(),g.Max(e=>e.Damage),g.Average(e=>e.Damage),g.Count(e=>e.Critical),100d*g.Count(e=>e.Critical)/g.Count(),0,amount>0?100d*g.Sum(e=>e.Damage)/amount:0)).ToArray();
            var details = heals ? hs.GroupBy(e=>hf.Direction=="Received"?e.Healer:e.Recipient).Select(g=>new InspectionLine(g.Key,$"{g.Sum(e=>e.Effective):N0} HP · {g.Count()} records")).ToArray() : es.GroupBy(e=>e.Target).Select(g=>new InspectionLine(g.Key,$"{g.Sum(e=>e.Damage):N0} damage · {g.Count()} records")).ToArray();
            string evidence = CombatIdentity.Same(name,profile.Character)?"Character and timestamp-qualified owned pet":string.Join(" / ",roster.Where(m=>CombatIdentity.Same(m.Name,name)).Select(m=>m.Source).Distinct());
            result.Add(new(Identity.Key(name),CombatIdentity.Same(name,profile.Character)?"Self":name,count>0?amount:null,count>0?amount/seconds:null,null,count,evidence,abilities.OrderByDescending(a=>a.Dps).ThenBy(a=>a.Ability,StringComparer.OrdinalIgnoreCase).ToArray(),details){Seconds=seconds,CharacterTotal=count>0?self:null,PetTotal=count>0?amount-self:null});
        }
        long total=result.Sum(m=>m.Total??0); result=result.Select(m=>m with{Share=m.Total.HasValue&&total>0?100d*m.Total/total:null}).ToList();
        string coverage = "Observed log coverage; unseen events and unknown membership intervals are unavailable." + (view.Fights.Any(e=>heals?!e.HealingDetailAvailable:!e.DetailAvailable)?" Summary-only encounters omit member details.":"") + (selectedPeriods.Any(p=>p.Kind==PartyKind.Raid)?" Raid roster coverage is partial where only channel/system evidence exists.":"");
        return new(key,result.OrderByDescending(m=>m.Total??-1).ThenBy(m=>m.Id,StringComparer.Ordinal).ToArray(),seconds,coverage);
    }
}
