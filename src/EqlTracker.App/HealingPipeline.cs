using EqlTracker.Core;
using System.Text.Json;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    public bool HealingVisible { get; set; }
    public string HealingImportStatus { get; private set; } = "Historical healing pending";
    private FileCheckpoint? healingCheckpoint;
    private bool healingCaughtUp;
    private string healingZone = "Unknown";
    private DateTime? healingFirst, healingLast;
    private string HealingProjectionKey=>$"{Combat.Healing.Revision}|{Combat.Revision}|{historicalSnapshotRevision}|{historyRevision}|{JsonSerializer.Serialize(Profile.Healing)}|{JsonSerializer.Serialize(Profile.Meter)}|{Sessions?.ContentRevision}|{Volatile.Read(ref groupRevision)}|{Volatile.Read(ref inspectionIdentityRevision)}|{LabBrowseKey}";
    public Task ValidateHealingImportAsync(string folder) => Task.Run(() =>
    {
        var before = Combat.Encounters.ToDictionary(f => f.Id, f => (f.Start, f.End, f.Damage));
        var parser = new LogParser(Profile.Character); var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1024 && !healingCaughtUp; i++) { cancel.Token.ThrowIfCancellationRequested(); BackfillHealing(parser); if (i == 2) healingCheckpoint = null; }
        if (!healingCaughtUp) throw new InvalidOperationException("Healing backfill did not catch up.");
        bool unchanged = Combat.Encounters.All(f => before.TryGetValue(f.Id, out var prior) && prior == (f.Start, f.End, f.Damage));
        if (!unchanged) throw new InvalidOperationException("Healing import changed combat timing or damage.");
        var heals = Combat.Healing.Events; var cutoff = DateTime.Now.AddDays(-30);
        int expected = File.ReadLines(checkpoint!.Path).Select(text => parser.Parse(text)).Count(p => p?.Healing != null && p.Time >= cutoff);
        if (heals.Count != expected) throw new InvalidOperationException($"Healing replay expected {expected}, got {heals.Count}.");
        Json.Write(Path.Combine(folder, "healing-replay-report.json"), new { Passed = true, Encounters = before.Count, DamageAndTimingUnchanged = unchanged, Expected = expected, Imported = heals.Count, Seconds = watch.Elapsed.TotalSeconds, Criticals = heals.Count(e => e.Critical), HoTTicks = heals.Count(e => e.OverTime), Lifetaps = heals.Count(e => e.Lifetap), EffectiveHP = heals.Where(e => e.HealerOwner is "Player" or "Pet").Sum(e => e.Effective), ReceivedHP = heals.Where(e => e.RecipientOwner is "Player" or "Pet").Sum(e => e.Effective), CompleteEncounters = Combat.Encounters.Count(f => f.HealingCoverage == "Complete"), UniqueIds = heals.Select(e => e.Id).Distinct().Count(), RestartedBackfillCheckpoint = true });
    });
    public HealingViewState HealingView()
    {
        if (viewWorker != null && Published?.Healing is { } ready) return ready;
        lock (Sync) return BuildHealing(DpsView());
    }
    internal HealingViewState BuildHealing(DpsViewState damage)
    {
        var filter = Profile.Healing.Filter(damage.Filter.From, damage.Filter.Until);
        var outside = Profile.Meter.Scope == "Session" ? Combat.Healing.Events.Where(e => LabOutsideHealing(e)&&(damage.Session?.NextLogin == null || e.Time < damage.Session.NextLogin) && (Profile.Meter.ClassCombinations.Count == 0 || Profile.Meter.ClassCombinations.Contains(SessionEvidence.ClassesAt(damage.Session, e.Time)))).ToList() : [];
        return HealingMetrics.Build(damage.Fights, outside, filter, Profile.Healing.Sort, string.Join('|', damage.Fights.Select(f => f.Id)) + JsonSerializer.Serialize(filter));
    }
    private static Encounter HealingCopy(Encounter f) => new() { Id = f.Id, Start = f.Start, End = f.End, Zone = f.Zone, Finished = f.Finished, IdleResumable = f.IdleResumable, ResolvedTargets = new(f.ResolvedTargets), Heals = f.Heals.Select(e => e.Copy()).ToList(), HealingSummaries = f.HealingSummaries.ToList(), HealingCoverage = f.HealingCoverage, HealingDetailAvailable = f.HealingDetailAvailable };
    private void BackfillHealing(LogParser parser)
    {
        if (checkpoint == null || healingCaughtUp) return;
        healingCheckpoint ??= store.Get<FileCheckpoint>("healingBackfill");
        if (healingCheckpoint == null || healingCheckpoint.Generation != checkpoint.Generation)
        {
            healingCheckpoint = new() { Path = checkpoint.Path, CreationTicks = checkpoint.CreationTicks, PrefixHash = checkpoint.PrefixHash, PrefixLength = checkpoint.PrefixLength, Generation = checkpoint.Generation };
            healingFirst = healingLast = null; healingZone = "Unknown";
        }
        else
        {
            healingFirst ??= store.Get<DateTime?>("healingBackfillFirst"); healingLast ??= store.Get<DateTime?>("healingBackfillLast");
            if (healingZone == "Unknown") healingZone = store.Get<string>("healingBackfillZone") ?? "Unknown";
        }
        var batch = LogTailer.Read(checkpoint.Path, healingCheckpoint, 512 * 1024);
        if (batch.Reset) { healingCheckpoint = null; return; }
        var parsed = batch.Lines.Select(line => parser.Parse(line.Text, line.Id)).Where(p => p != null).ToList();
        lock (Sync)
        {
            var fights = Combat.Encounters.Concat(Combat.Current == null ? [] : [Combat.Current]).OrderBy(f => f.Start).ToList();
            var changed = new HashSet<Encounter>(); var cutoff = DateTime.Now.AddDays(-30);
            int index = 0;
            foreach (var line in parsed)
            {
                if (line!.Zone != null) healingZone = line.Zone;
                healingFirst = !healingFirst.HasValue || line.Time < healingFirst ? line.Time : healingFirst;
                healingLast = !healingLast.HasValue || line.Time > healingLast ? line.Time : healingLast;
                if (line.Healing is not { } heal || line.ParseWarning != null || heal.Time < cutoff) continue;
                while (index < fights.Count && fights[index].End < heal.Time) index++;
                var fight = index < fights.Count && fights[index].Start <= heal.Time && (healingZone == "Unknown" || Identity.ZoneKey(fights[index].Zone) == Identity.ZoneKey(healingZone)) ? fights[index] : null;
                int count = fight?.Heals.Count ?? 0;
                Combat.Healing.AssignHistorical(heal, healingZone == "Unknown" ? fight?.Zone ?? healingZone : healingZone, fight);
                if (fight != null && count != fight.Heals.Count) changed.Add(fight);
            }
            if (batch.CaughtUp)
            {
                foreach (var f in fights.Where(f => f.Start >= healingFirst && f.End <= healingLast && f.Start >= cutoff)) { f.HealingCoverage = "Complete"; changed.Add(f); }
            }
            foreach (var f in changed) HealingLedger.Summarize(f);
            var pending = Combat.Healing.PendingWrites.ToArray();
            var savedCheckpoint = EquipmentPositions.Copy(batch.Checkpoint); var savedHealing = pending.Select(e => e.Copy()).ToArray(); var savedFights = changed.Select(CombatSnapshots.Copy).ToArray(); var savedZone = healingZone; var savedFirst = healingFirst; var savedLast = healingLast; QueuePersistence(() => store.CommitHealingBackfill(savedCheckpoint, savedHealing, savedFights, savedZone, savedFirst, savedLast)); Combat.Healing.MarkSaved(pending);
            
            healingCheckpoint = batch.Checkpoint; healingCaughtUp = batch.CaughtUp;
            HealingImportStatus = healingCaughtUp ? "Retained-log healing imported" : $"Importing healing · {healingCheckpoint.Offset / 1048576d:N1} MB";
            if (changed.Count > 0) { foreach (var f in changed) changedHistoricalSnapshots.Add(f.Id); historicalSnapshotRevision++; }
        }
    }
}
