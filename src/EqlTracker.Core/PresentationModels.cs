using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed class QueuedGoal
{
    public string Kind { get; set; } = "Item";
    public string EffectId { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string ItemKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Id => QuestId + "|" + ItemKey + (EffectId.Length > 0 ? "|" + EffectId : "");
}
public sealed record QueueEntry(QueuedGoal Goal, ObjectiveRow? Objective, string Status, string Destination)
{ public List<ObjectiveRow> Ingredients { get; init; } = []; public string Display => $"{Goal.Name} · {Status}" + (Destination.Length > 0 ? " · " + Destination : ""); }
public static class ObjectiveQueue
{
    public static List<QueueEntry> Build(CharacterProfile profile, Catalog catalog, IEnumerable<ObjectiveRow> objectives, InventorySnapshot? inventory = null)
    {
        var rows = objectives.ToDictionary(x => x.QuestId + "|" + x.Key);
        return profile.GoalQueue.Select(g =>
        {
            rows.TryGetValue(g.QuestId + "|" + g.ItemKey, out var row);
            var quest = catalog.Quests.FirstOrDefault(q => q.Id == g.QuestId);
            if (g.Kind == "Exaltation")
            {
                var option = inventory == null ? null : ExaltationPlanner.Build(new Catalog { Items = catalog.Items.Where(i => i.Key == g.ItemKey).ToList() }, inventory).FirstOrDefault(e => e.MatchesId(g.EffectId));
                var source = catalog.FindKey(g.ItemKey);
                var objective = ExaltationPlanner.Objective(g, option);
                return new QueueEntry(g, objective, option?.State ?? "Unresolved", objective?.Zone ?? "") { Ingredients = objective == null ? [] : [objective] };
            }
            var ingredients = g.Kind == "Quest" ? rows.Values.Where(r => r.QuestId == g.QuestId).ToList() : row == null ? new List<ObjectiveRow>() : new List<ObjectiveRow> { row };
            if (g.Kind == "Quest") row = ingredients.FirstOrDefault(r => r.Remaining > 0) ?? ingredients.FirstOrDefault();
            var wish = profile.Wishlist.FirstOrDefault(w => w.Id == g.QuestId);
            string status = row != null ? row.Remaining == 0 ? "Owned" : "Need " + row.Remaining :
                quest != null && (!QuestTracking.Selected(quest, profile) || !ExpansionRules.Planned(quest, profile)) ? "Paused" :
                quest != null && catalog.FindKey(g.ItemKey) != null || wish is { Completed: true } ? "Satisfied / covered" : "Unresolved";
            if (quest != null && inventory != null && g.Kind == "Quest" && ingredients.Count == 0 && !QuestTracking.Fulfilled(quest, profile, inventory, catalog)) status = "Incomplete · details pending";
            if (quest != null && !QuestTracking.Selected(quest, profile)) status = "Paused";
            if (g.Kind == "Quest" && ingredients.Any(r => r.Remaining > 0) && status != "Paused") status = "Need " + ingredients.Sum(r => r.Remaining);
            return new QueueEntry(g, row, status, row?.Sources.FirstOrDefault(s => s.Zone.Length > 0 && (catalog.Zones.Count == 0 || catalog.Zones.Any(z => Identity.ZoneKey(z) == Identity.ZoneKey(s.Zone))))?.Zone ?? row?.Zone ?? "") { Ingredients = ingredients };
        }).ToList();
    }
    public static List<ObjectiveRow> Fallback(CharacterProfile profile, Catalog catalog, IEnumerable<ObjectiveRow> objectives, InventorySnapshot? inventory = null)
    {
        var rows = objectives.Where(r => r.Remaining > 0).ToList();
        if (profile.ObjectiveMode == "My queue") return Build(profile, catalog, rows, inventory).Where(e => e.Status != "Paused").SelectMany(e => e.Ingredients).Where(r => r.Remaining > 0).DistinctBy(r => r.QuestId + "|" + r.Key).ToList();
        if (profile.ObjectiveMode != "Suggestions") return [];
        return rows.Where(r => r.Sources.Any(s => ExpansionRules.Available(s.Era, ExpansionRules.Current(profile)) && s.Availability != "Unavailable"))
            .OrderBy(r => { int custom = profile.GoalPriority.IndexOf(r.Class); return custom >= 0 ? custom : 100 + (profile.CurrentClasses.Contains(r.Class) ? 0 : profile.Level50Classes.Contains(r.Class) ? 20 : 40); })
            .ThenBy(r => r.Availability == "Observed drop" ? 0 : r.Availability == "Wiki documented" ? 1 : 2)
            .ThenBy(r => r.Instructions.Contains("prerequisite", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(r => r.Item).Take(5).ToList();
    }
}

public sealed record LinkedHeal(string HealingId, string DamageId, long Effective, string Evidence);
public static class DamageHealing
{
    public static string SpellKey(string spell) => Regex.Replace(spell.Trim(), @"\s+(?:[IVX]+|\d+)(?:\s*\+\d+)?$", "", RegexOptions.IgnoreCase).ToLowerInvariant();
    public static List<LinkedHeal> Link(IEnumerable<Encounter> fights)
    {
        var result = new List<LinkedHeal>();
        foreach (var fight in fights.Where(f => f.DetailAvailable && f.HealingDetailAvailable))
        {
            var damage = fight.Events.Where(e => e.Owner is "Pet" or "Player" && e.Kind is "Spell" or "DoT").ToLookup(e => (CombatIdentity.Key(e.Actor), SpellKey(e.Ability), e.Time));
            foreach (var group in fight.Heals.Where(h => HealingIdentity.IsLifetap(h.Ability) && !h.OverTime).GroupBy(h => (CombatIdentity.Key(h.Healer), SpellKey(h.Ability), h.Time)))
            {
                var candidates = damage[group.Key].ToList(); var heals = group.ToList();
                // Multiple same-second casts need a strictly alternating damage/heal log order.
                if (candidates.Count == heals.Count && candidates.Count > 1)
                {
                    var records = candidates.Select(d => (d.Id, Damage: (CombatEvent?)d, Heal: (HealingEvent?)null)).Concat(heals.Select(h => (h.Id, Damage: (CombatEvent?)null, Heal: (HealingEvent?)h))).ToList();
                    int colon = records[0].Id.LastIndexOf(':'); if (colon < 1) continue; string prefix = records[0].Id[..(colon + 1)];
                    if (records.Any(r => !r.Id.StartsWith(prefix, StringComparison.Ordinal) || !long.TryParse(r.Id[prefix.Length..], out _))) continue;
                    records = records.OrderBy(r => long.Parse(r.Id[prefix.Length..])).ToList();
                    if (records.Where((r, i) => i % 2 == 0 ? r.Damage == null : r.Heal == null).Any()) continue;
                    for (int i = 0; i < records.Count; i += 2) { var damageEvent = records[i].Damage!; var healEvent = records[i + 1].Heal!; if (damageEvent.Owner == healEvent.HealerOwner) result.Add(new(healEvent.Id, damageEvent.Id, healEvent.Effective, "Verified lifetap alias, actor and alternating same-second log order")); }
                    continue;
                }
                if (candidates.Count != 1 || heals.Count != 1) continue;
                var d = candidates[0]; var h = heals[0];
                if (h.HealerOwner != d.Owner || d.Id.Length == 0 || h.Id.Length == 0) continue;
                int separator = d.Id.LastIndexOf(':');
                if (separator > 0 && h.Id.StartsWith(d.Id[..(separator + 1)], StringComparison.Ordinal) && !LogPosition.After(h.Id, d.Id)) continue;
                result.Add(new(h.Id, d.Id, h.Effective, "Verified lifetap alias, actor and unique same-second pair"));
            }
        }
        return result.DistinctBy(x => x.HealingId).ToList();
    }
    public static List<DamageRow> Apply(List<DamageRow> rows, IEnumerable<Encounter> fights, CombatFilter filter)
    {
        var all = fights.ToList(); var links = Link(all).ToDictionary(l => l.DamageId);
        var totals = CombatMetrics.SelectEvents(all, filter).Where(e => links.ContainsKey(e.Id)).GroupBy(e => (e.Ability, e.Kind, Channel: DamageChannels.From(e)))
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(e => links[e.Id].Effective), Count: g.Count()));
        return rows.Select(r => totals.TryGetValue((r.Ability, r.Kind, r.Channel), out var total) ? r with { LinkedHealing = total.Amount, LinkedHealCount = total.Count } : r).ToList();
    }
}

public sealed record ChartPeak(string Actor, double Dps, DateTime Time, int Index);
public sealed record TimelineCursor(DateTime Time, double Elapsed, double Self, double Pet, double Interval);
public sealed record CombatChart(DateTime Start, double Duration, double BinSeconds, double[] Pet, double[] Self, List<ChartPeak> Peaks)
{
    public static string ElapsedLabel(double seconds)
    {
        var elapsed = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return elapsed.TotalHours >= 1 ? $"{(long)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
    public TimelineCursor? Sample(double elapsed)
    {
        if (Start == default || Pet.Length + Self.Length == 0 || BinSeconds <= 0 || !double.IsFinite(elapsed) || !DetailComplete && !HasEvents) return null;
        int index = Math.Clamp((int)(Math.Clamp(elapsed, 0, Duration) / BinSeconds), 0, Math.Max(Pet.Length, Self.Length) - 1);
        double time = Math.Min(Duration, index * BinSeconds);
        return new(Start.AddSeconds(time), time, index < Self.Length ? Self[index] : 0, index < Pet.Length ? Pet[index] : 0, BinSeconds);
    }
    public IReadOnlyList<ChartPeak> LocalPeaks { get; init; } = [];
    public static IReadOnlyList<ChartPeak> FindLocalPeaks(DateTime start, double duration, double bin, double[] pet, double[] self)
    {
        var result = new List<ChartPeak>();
        foreach (var (actor, series) in new[] { ("Pet", pet), ("Self", self) })
            for (int i = 0; i < series.Length; i++)
            {
                int end = i; while (end + 1 < series.Length && series[end + 1] == series[i]) end++;
                if (series[i] > 0 && (i == 0 || series[i] > series[i - 1]) && (end == series.Length - 1 || series[i] > series[end + 1])) result.Add(new(actor, series[i], start.AddSeconds(Math.Min(duration, i * bin)), i));
                i = end;
            }
        return result;
    }
    public string Unit { get; init; } = "DPS";
    public bool DetailComplete { get; init; } = true; public bool HasEvents { get; init; } = true;
    public static CombatChart BuildHealing(List<Encounter> fights, List<HealingEvent> events, HealingFilter filter)
    {
        var bounds = fights.Where(f => !(filter.From > f.End || filter.Until < f.Start)).ToList();
        if (bounds.Count == 0) return new(default, 1, 1, [], [], []) { Unit = "HPS" };
        DateTime start = bounds.Min(f => filter.From > f.Start ? filter.From.Value : f.Start), end = bounds.Max(f => filter.Until < f.End ? filter.Until.Value : f.End);
        double duration = Math.Max(1, (end - start).TotalSeconds), bin = Math.Max(1, duration / 80);
        int count = (int)Math.Ceiling(duration / bin) + 1; var pet = new double[count]; var self = new double[count];
        foreach (var e in events.Where(e => e.Time >= start && e.Time <= end))
        { int i = Math.Clamp((int)((e.Time - start).TotalSeconds / bin), 0, count - 1); string owner = filter.Direction == "Received" ? e.RecipientOwner : e.HealerOwner; if (owner == "Pet") pet[i] += e.Effective / bin; else if (owner == "Player") self[i] += e.Effective / bin; }
        var peaks = new List<ChartPeak>();
        foreach (var (actor, series) in new[] { ("Pet", pet), ("Self", self) }) { double max = series.Max(); int index = Array.IndexOf(series, max); if (max > 0) peaks.Add(new(actor, max, start.AddSeconds(Math.Min(duration, index * bin)), index)); }
        return new(start, duration, bin, pet, self, peaks) { LocalPeaks = FindLocalPeaks(start, duration, bin, pet, self), Unit = "HPS", DetailComplete = fights.All(f => f.HealingDetailAvailable && f.HealingCoverage == "Complete"), HasEvents = events.Count > 0 };
    }

    public static CombatChart Build(List<Encounter> fights, CombatFilter filter)
    {
        if (fights.Count == 0) return new(default, 1, 1, [], [], []);
        DateTime start = fights.Min(f => filter.From > f.Start ? filter.From.Value : f.Start), end = fights.Max(f => filter.Until < f.End ? filter.Until.Value : f.End);
        double duration = Math.Max(1, (end - start).TotalSeconds), bin = Math.Max(1, duration / 80);
        int count = (int)Math.Ceiling(duration / bin) + 1; var pet = new double[count]; var self = new double[count];
        var events = CombatMetrics.SelectEvents(fights, filter);
        foreach (var e in events) { int i = Math.Clamp((int)((e.Time - start).TotalSeconds / bin), 0, count - 1); if (e.Owner == "Pet") pet[i] += e.Damage / bin; else if (e.Owner == "Player") self[i] += e.Damage / bin; }
        var peaks = new List<ChartPeak>();
        foreach (var (actor, series) in new[] { ("Pet", pet), ("Self", self) }) { double max = series.Max(); int index = Array.IndexOf(series, max); if (max > 0) peaks.Add(new(actor, max, start.AddSeconds(Math.Min(duration, index * bin)), index)); }
        return new(start, duration, bin, pet, self, peaks) { LocalPeaks = FindLocalPeaks(start, duration, bin, pet, self), DetailComplete = fights.All(f => f.DetailAvailable), HasEvents = events.Count > 0 };
    }
}

public sealed class NativeSyncSettings
{
    public int Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Verified { get; set; }
    public bool Login { get; set; }
    public bool Periodic { get; set; }
    public int IntervalHours { get; set; } = 2;
    public int? IntervalMinutes { get; set; }
    public DateTime? IntervalChangedAt { get; set; }
    public DateTime? LastSuccess { get; set; }
    public string Shortcut => (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + (Key == 0 ? "Not linked" : Key is >= 112 and <= 135 ? "F" + (Key - 111) : ((char)Key).ToString());
}
public sealed class SyncRequest
{
    public string State { get; set; } = "Ready";
    public string Message { get; set; } = "Link the native Sync shortcut to enable companion sync.";
    public DateTime RequestedAt { get; set; }
    public long BaselineLength { get; set; }
    public string Generation { get; set; } = "";
    public DateTime InventoryWriteUtc { get; set; }
    public bool Who { get; set; }
    public bool Export { get; set; }
    public bool Logging { get; set; }
    public bool Imported { get; set; }
    public bool Active => State is "Requested" or "Awaiting evidence";
    public string Outcome(DateTime now) => Logging && Who && Export && Imported ? "Complete" : (now - RequestedAt).TotalSeconds < 15 ? "Awaiting evidence" : Logging || Who || Export ? "Partial" : "Failed";
}
public static class NativeSyncPolicy
{
    public static int Minutes(NativeSyncSettings settings) => settings.IntervalMinutes is >= 1 and <= 1440 ? settings.IntervalMinutes.Value : Math.Clamp(settings.IntervalHours,1,24)*60;
    public static void Migrate(NativeSyncSettings settings) => settings.IntervalMinutes = Minutes(settings);
    public static bool SetMinutes(NativeSyncSettings settings,int minutes,DateTime? now=null)
    { if(minutes is < 1 or > 1440)return false;if(Minutes(settings)!=minutes)settings.IntervalChangedAt=now??DateTime.Now;settings.IntervalMinutes=minutes;return true; }
    public static DateTime? NextPeriodic(NativeSyncSettings settings)
    { var baseline=settings.LastSuccess;if(settings.IntervalChangedAt is {} changed&&(!baseline.HasValue||changed>baseline))baseline=changed;return baseline?.AddMinutes(Minutes(settings)); }
    public static string Schedule(NativeSyncSettings settings) => !settings.Periodic ? "Off" : !settings.Verified || settings.Key==0 ? "Unverified · link and test Sync" : "Every " + (Minutes(settings)%60==0 ? Minutes(settings)/60+" hours" : Minutes(settings)+" minutes") + " · deferred until eligible";
    public static string AutoUpdateHelp(NativeSyncSettings settings) => "Apply the latest complete inventory capture to Current and keep Current updated from subsequent captures. Named builds stay unchanged.\nScheduled capture: " + Schedule(settings) + ". Default interval: 2 hours.\nFresh capture files are checked about every 3 seconds. Capture creation and Auto Update are independent; loot does not establish equipped items.";
    public static bool Due(NativeSyncSettings settings, DateTime now, bool newWorldSession) => settings.Verified && settings.Key != 0 &&
        (settings.Login && newWorldSession || settings.Periodic && (NextPeriodic(settings) is not {} due || now >= due));
    public static bool Eligible(bool foreground, double idleSeconds, bool heldKeys, bool combatOrResumable, bool capture) => foreground && idleSeconds >= 5 && !heldKeys && !combatOrResumable && !capture;
}
public static class LogPosition
{
    public static bool After(string id, string baseline)
    {
        int i = id.LastIndexOf(':'), b = baseline.LastIndexOf(':');
        return i > 0 && b > 0 && id[..i] == baseline[..b] && long.TryParse(id[(i + 1)..], out long offset) && long.TryParse(baseline[(b + 1)..], out long before) && offset > before;
    }
}
