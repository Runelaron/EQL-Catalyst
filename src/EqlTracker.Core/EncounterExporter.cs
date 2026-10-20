using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EqlTracker.Core;

public sealed record EncounterExportDocument(int Version, DateTime CreatedAt, string Character, string Server, string Scope, CombatFilter Filter, double Seconds, long Damage, double Dps, bool DetailComplete, List<Encounter> Encounters, List<DamageRow> Abilities, List<TargetRow> Targets, List<PeakHit> PeakHits)
{
    public List<PetIdentity> PetIdentities { get; init; } = [];
    public List<GroupMembership> GroupMembership { get; init; } = [];
    public string TimingPolicy { get; init; } = "First-to-last relevant combat, including resumed pauses; idle closing tail excluded. Session durations are summed.";
    public int IdleSeconds { get; init; }
    public int ResumeSeconds { get; init; }
    public string PetElementOverride { get; init; } = "";
    public bool GroupCoverageIncomplete { get; init; }
    public string SessionId { get; init; } = "";
    public List<string> ClassCombinations { get; init; } = [];
    public HealingViewState? Healing { get; init; }
}
public static class EncounterExporter
{
    public static EncounterExportDocument Snapshot(DpsViewState view, CharacterProfile profile, HealingViewState? healing = null)
    {
        // The copy also freezes an active encounter without closing or modifying it.
        var fights = JsonSerializer.Deserialize<List<Encounter>>(JsonSerializer.Serialize(view.Fights, Json.Options), Json.Options)!;
        healing ??= HealingMetrics.Build(fights, [], profile.Healing.Filter(view.Filter.From, view.Filter.Until), profile.Healing.Sort);
        healing = healing with { Events = healing.Events.Select(e => e.Copy()).ToList(), OutsideEvents = healing.OutsideEvents.Select(e => e.Copy()).ToList(), Rows = healing.Rows.ToList(), Healers = healing.Healers.ToList(), Recipients = healing.Recipients.ToList(), AvailableHealers = healing.AvailableHealers.ToList(), AvailableRecipients = healing.AvailableRecipients.ToList() };
        return new(3, DateTime.Now, profile.Character, profile.Server, profile.Meter.Scope, view.Filter, view.Seconds, view.Damage, view.Dps, view.DetailComplete, fights, view.Rows.ToList(), view.Targets.ToList(), view.Peaks.ToList()) { SessionId = view.Session?.Id ?? "", ClassCombinations = profile.Meter.ClassCombinations.ToList(), Healing = healing ?? HealingMetrics.Build(fights, [], profile.Healing.Filter(view.Filter.From, view.Filter.Until), profile.Healing.Sort), PetIdentities = JsonSerializer.Deserialize<List<PetIdentity>>(JsonSerializer.Serialize(profile.PetIdentities))!, GroupMembership = JsonSerializer.Deserialize<List<GroupMembership>>(JsonSerializer.Serialize(profile.GroupMembers))!, PetElementOverride = profile.PetElementOverride, GroupCoverageIncomplete = profile.InGroup && profile.GroupRosterIncomplete, IdleSeconds = profile.EncounterTimeoutSeconds, ResumeSeconds = profile.EncounterResumeSeconds };
    }
    public static void Write(string path, EncounterExportDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); string staging = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var zip = ZipFile.Open(staging, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("encounters.json").Open(), new UTF8Encoding(false))) writer.Write(JsonSerializer.Serialize(document, Json.Options));
                Csv(zip, "summary.csv", ["Encounter", "Start", "End", "Zone", "Actor", "Seconds", "Damage", "DPS", "Detail", "Finished", "Pet", "Element", "Element evidence", "Loadout", "Weapons"], document.Encounters.SelectMany(f => new[] { "Pet", "Player", "Combined" }.Select(actor =>
                {
                    var filter = document.Filter with { Owner = actor }; double seconds = CombatMetrics.Duration([f], filter);
                    bool clipped = filter.From > f.Start || filter.Until < f.End;
                    bool filtered = filter.Target.Length > 0 || filter.Ability.Length > 0 || filter.Kind.Length > 0 || filter.CriticalOnly || clipped;
                    long? damage = f.DetailAvailable ? CombatMetrics.SelectEvents([f], filter).Sum(x => x.Damage) : filtered ? null : actor == "Pet" ? f.PetDamage : actor == "Player" ? f.PlayerDamage : f.Damage;
                    return new object?[] { f.Id, f.Start, f.End, f.Zone, actor, seconds, damage, damage.HasValue ? damage / Math.Max(1, seconds) : null, f.DetailAvailable ? "Complete" : "Summary only", f.Finished, f.PetName, f.PetType, f.PetElementEvidence, f.Loadout, f.Weapons };
                })));
                Csv(zip, "abilities.csv", ["Ability", "Kind", "Channel", "Damage", "DPS", "Hits", "Average", "Max", "Peak time", "Crits", "Crit percent", "Misses", "Share percent"], document.Abilities.Select(x => new object?[] { x.Ability, x.Kind, x.Channel, x.Damage, x.Dps, x.Hits, x.Average, x.MaxHit, x.PeakTime, x.Criticals, x.CriticalPercent, x.Misses, x.Percent }));
                Csv(zip, "targets.csv", ["Target name", "Damage", "DPS", "Share percent", "Hits", "Average", "Max", "Crit percent", "Melee", "Spells"], document.Targets.Select(x => new object?[] { x.Target, x.Damage, x.Dps, x.Percent, x.Hits, x.Average, x.MaxHit, x.CriticalPercent, x.MeleeDamage, x.SpellDamage }));
                Csv(zip, "events.csv", ["Encounter", "Event ID", "Time", "Actor", "Attribution", "Target", "Ability", "Kind", "Channel", "Damage", "Critical", "Outcome", "Activity only", "Relevance", "In selected view"], document.Encounters.SelectMany(f => f.Events.Select(e => new object?[] { f.Id, e.Id, e.Time, e.Actor, e.Owner, e.Target, e.Ability, e.Kind, DamageChannels.From(e), e.Damage, e.Critical, e.Outcome, e.ActivityOnly, e.Relevance, CombatMetrics.Matches(e, document.Filter) })));
                if (document.Healing is { } healing)
                {
                    Csv(zip, "healing_summary.csv", ["Encounter", "Direction", "Actor", "Seconds", "Effective healing", "HPS", "Largest heal", "Known overheal", "Raw amounts known", "Heals", "Coverage", "Details", "Totals available"], document.Encounters.SelectMany(f => new[] { "Done", "Received" }.SelectMany(direction => new[] { "Player", "Pet" }.Select(actor =>
                    {
                        var h = HealingMetrics.Build([f], [], healing.Filter with { Direction = direction, Owner = actor });
                        return new object?[] { f.Id, direction, actor == "Player" ? "Self" : actor, h.Seconds, h.TotalsAvailable ? h.Effective : null, h.TotalsAvailable ? h.Hps : null, h.TotalsAvailable ? h.MaxHeal : null, h.RawKnownCount > 0 ? h.KnownOverheal : null, h.RawKnownCount, h.TotalsAvailable ? h.Count : null, h.Coverage, h.DetailComplete ? "Complete" : "Summary only / incomplete", h.TotalsAvailable };
                    }))));
                    Csv(zip, "healing_spells.csv", ["Spell", "Type", "Lifetap", "Effective healing", "HPS", "Heals", "Average", "Max", "Peak time", "Crit percent", "Share percent", "Known overheal", "Raw amounts known"], healing.Rows.Select(r => new object?[] { r.Ability, r.Kind, r.Lifetap, r.Effective, r.Hps, r.Count, r.Average, r.MaxHeal, r.PeakTime, r.CriticalPercent, r.Percent, r.RawKnownCount > 0 ? r.KnownOverheal : null, r.RawKnownCount }));
                    Csv(zip, "healing_recipients.csv", ["Recipient", "Effective healing", "HPS", "Heals", "Average", "Max", "Crit percent", "Share percent"], healing.Recipients.Select(r => new object?[] { r.Name, r.Effective, r.Hps, r.Count, r.Average, r.MaxHeal, r.CriticalPercent, r.Percent }));
                    Csv(zip, "healing_healers.csv", ["Healer", "Effective healing", "HPS", "Heals", "Average", "Max", "Crit percent", "Share percent"], healing.Healers.Select(r => new object?[] { r.Name, r.Effective, r.Hps, r.Count, r.Average, r.MaxHeal, r.CriticalPercent, r.Percent }));
                    Csv(zip, "healing_events.csv", ["Encounter", "Event ID", "Time", "Healer", "Healer owner", "Recipient", "Recipient owner", "Spell", "Effective healing", "Raw amount", "Overheal", "HoT", "Lifetap", "Critical", "Zone", "Attribution evidence", "In selected healing view", "Outside combat"], document.Encounters.SelectMany(f => f.Heals).Concat(healing.OutsideEvents).DistinctBy(e => e.Id).Select(e => new object?[] { e.EncounterId, e.Id, e.Time, e.Healer, e.HealerOwner, e.Recipient, e.RecipientOwner, e.Ability, e.Effective, e.Raw, e.Overheal, e.OverTime, e.Lifetap, e.Critical, e.Zone, e.AttributionEvidence, HealingMetrics.Matches(e, healing.Filter), e.EncounterId.Length == 0 }));
                }
                using var notes = new StreamWriter(zip.CreateEntry("README.txt").Open());
                notes.Write("Version 3 EQL Catalyst encounter export. Healing files use restored HP and the shared combat-duration denominator; healing done and received are overlapping perspectives, never additive. Outside-combat healing is excluded from HPS. Missing raw healing/overheal and unavailable history stay unknown. CSV uses invariant numbers and ISO local timestamps. Summary has Pet/Player/Combined rows using the same filtered encounter-duration denominator. Ability and target tables match the selected actor/filters. Events and JSON preserve the selected encounters' recorded combat evidence, with an In selected view flag; activity-only events never add outgoing DPS. No chat is included. Identical target names can represent multiple NPCs. Summary-only records do not contain invented events, filtered damage, or peak hits. Active encounters are frozen at export time, not ended.");
            }
            File.Move(staging, path, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    private static void Csv(ZipArchive zip, string name, string[] header, IEnumerable<object?[]> rows)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)); writer.WriteLine(string.Join(',', header.Select(Quote)));
        foreach (var row in rows) writer.WriteLine(string.Join(',', row.Select(x => Quote(x switch { null => "", DateTime date => date.ToString("O"), IFormattable number => number.ToString(null, CultureInfo.InvariantCulture), _ => x.ToString() ?? "" }))));
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
