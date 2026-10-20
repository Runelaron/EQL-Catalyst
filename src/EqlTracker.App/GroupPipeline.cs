using System.Text.Json;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private readonly object groupingSync = new();
    private GroupingEvidence grouping = new();
    private IReadOnlyList<GroupPeriod> groupPeriods = [];
    private long groupRevision;
    public IReadOnlyList<GroupPeriod> GroupPeriods => Volatile.Read(ref groupPeriods);
    public string GroupImportStatus { get; private set; } = "Indexing grouping evidence…";
    private void InitializeGroups()
    {
        grouping = store.Get<GroupingEvidence>("groupingEvidence") ?? new();
        if (grouping.ParserVersion != GroupingEvidence.Version) grouping = new();
        Volatile.Write(ref groupPeriods, EquipmentPositions.Copy(grouping.Periods));
        groupRevision = grouping.Revision;
    }
    private void ReadGroups()
    {
        if (!File.Exists(GameLogPath)) return;
        var batch = LogTailer.Read(GameLogPath, grouping.Checkpoint, 1024 * 1024);
        if (batch.Lines.Count == 0 && !batch.Reset)
        {
            if(GroupPeriods.Any(p=>p.Live!=(gameSessionLive&&!p.End.HasValue))){var refreshed=EquipmentPositions.Copy(grouping.Periods);foreach(var p in refreshed)p.Live=gameSessionLive&&!p.End.HasValue;Volatile.Write(ref groupPeriods,refreshed);}
            GroupImportStatus="Retained grouping evidence indexed; unavailable membership intervals remain unknown";return;
        }
        using var measure = UpdateDiagnostics.Measure("Grouping evidence batch");
        GroupingEvidence saved;
        lock(groupingSync)
        {
            foreach(var line in batch.Lines){cancel.Token.ThrowIfCancellationRequested();grouping.Observe(line.Text,line.Id);}
            grouping.Checkpoint=batch.Checkpoint;saved=EquipmentPositions.Copy(grouping);
            foreach(var p in saved.Periods)p.Live=gameSessionLive&&!p.End.HasValue;
            QueuePersistence(()=>store.Set("groupingEvidence",saved));Volatile.Write(ref groupPeriods,saved.Periods);Interlocked.Exchange(ref groupRevision,saved.Revision);
        }
        GroupImportStatus = batch.CaughtUp ? "Retained grouping evidence indexed; unavailable membership intervals remain unknown" : $"Indexing grouping evidence · {batch.Checkpoint.Offset / 1048576d:N1} MB";
        lock (Sync)
        {
            if (Profile.Meter.GroupPeriodId.Length > 0 && saved.Periods.FirstOrDefault(p => p.Id == Profile.Meter.GroupPeriodId) is { } period)
                Profile.Meter.Until = period.End ?? period.LastObserved;
        }
    }
    internal void SelectGroupPeriod(string id)
    {
        var period = GroupPeriods.FirstOrDefault(p => p.Id == id); if (period == null) return;
        var session = SessionEvidence.For(Sessions?.Sessions ?? [], period.Start);
        lock (Sync)
        {
            var p = Profile.Meter; p.GroupPeriodId = id; p.Scope = "Session"; p.SessionId = session?.Id ?? "";
            p.EncounterId = ""; p.From = period.Start; p.Until = period.End ?? period.LastObserved;
            p.ClassCombinations.Clear(); Save();
        }
        EnsureSessionLoaded();
    }
    internal void ExitGroupPeriod(bool latest = false)
    {
        if (latest && Profile.Meter.GroupPeriodId.Length > 0) Profile.Meter.From = Profile.Meter.Until = null;
        Profile.Meter.GroupPeriodId = "";
    }
    internal void CorrectGroupProjection(string? periodId,string players,string pets)
    {
        var names=players.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Where(n=>!CombatIdentity.Same(n,Profile.Character)).ToArray();
        var links=pets.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(value=>value.Split('=',2,StringSplitOptions.TrimEntries)).ToArray();
        if(links.Any(parts=>parts.Length!=2||parts.Any(string.IsNullOrWhiteSpace)||!names.Contains(parts[1],StringComparer.OrdinalIgnoreCase)&&!CombatIdentity.Same(parts[1],Profile.Character)))throw new InvalidDataException("Use Pet=Owner with an explicitly listed player owner (or yourself).");
        if(periodId!=null)
        {
            lock(groupingSync) { var period=grouping.Periods.FirstOrDefault(p=>p.Id==periodId)??throw new InvalidDataException("Wait for grouping evidence indexing.");
            period.Members.RemoveAll(m=>m.Source=="Manual");
            foreach(string name in names)period.Members.Add(new(){Name=name,From=period.Start,Until=period.End,Source="Manual"});
            foreach(var parts in links)period.Members.Add(new(){Name=parts[0],Owner=parts[1],IsPet=true,From=period.Start,Until=period.End,Source="Manual"});
            grouping.Revision++;var saved=EquipmentPositions.Copy(grouping);QueuePersistence(()=>store.Set("groupingEvidence",saved));Volatile.Write(ref groupPeriods,saved.Periods);Interlocked.Exchange(ref groupRevision,grouping.Revision); } Save();
        }
        else
        {
            CorrectGroup(names.Select(name=>(name,false)).Concat(links.Select(parts=>(parts[0],true))));
            foreach(var parts in links)foreach(var member in Profile.GroupMembers.Where(m=>m.Until==null&&m.IsPet&&CombatIdentity.Same(m.Name,parts[0])))member.Owner=parts[1];
            Save();
        }
    }
    internal void SetGroupPeriodsForValidation(IReadOnlyList<GroupPeriod> values)
    { if (!Demo) throw new InvalidOperationException("Demo only"); Volatile.Write(ref groupPeriods, values); Interlocked.Increment(ref groupRevision); }
}
