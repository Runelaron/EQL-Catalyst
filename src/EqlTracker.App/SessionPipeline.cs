using EqlTracker.Core;
using System.Text.Json;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private SessionEvidence sessionEvidence = new();
    private SessionView? sessionView;
    private IReadOnlyList<Encounter> sessionSummaries = [];
    private long sessionSummaryRevision = -1;
    private bool validationAllSessions;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> hydratedSessions = new();
    public SessionView? Sessions => Volatile.Read(ref sessionView);
    internal void SetPreviewSessions(SessionView? value) { if (!Demo) throw new InvalidOperationException("Demo only"); Volatile.Write(ref sessionView, value); }
    private readonly object sessionEvidenceSync = new();
    private bool gameSessionLive;
    public string SessionImportStatus { get; private set; } = "Reading saved sessions…";
    private void InitializeSessions()
    {
        sessionEvidence = store.Get<SessionEvidence>("sessionEvidence") ?? new();
        sessionSummaries = store.EncounterSummaries();
        SessionImportStatus = sessionEvidence.Checkpoint != null ? "Saved sessions available offline · uncertain logout times use last observed activity" : "Indexing saved login evidence…";
        PublishSessions();
    }
    private void PublishSessions()
    {
        lock (sessionEvidenceSync)
        {
        var records = EquipmentPositions.Copy(sessionEvidence.Sessions);
        if (sessionSummaries.FirstOrDefault() is { } first && (records.Count == 0 || first.Start < records[0].Start))
            records.Insert(0, new() { Id = "inferred:stored", Start = first.Start, LastObserved = records.Count == 0 ? sessionSummaries.Max(f => f.End) : records[0].Start.AddTicks(-1), NextLogin = records.FirstOrDefault()?.Start, Inferred = true, Evidence = "Saved encounters preceding available login evidence" });
        var choices = records.Select(s => new SessionChoice(s.Id, $"{s.Start:MMM/dd HH:mm} · {sessionSummaries.Count(f => f.Start >= s.Start && (!s.NextLogin.HasValue || f.Start < s.NextLogin))} fights · {(s.Inferred ? "inferred · " : "")}{(gameSessionLive && s.Id == records.LastOrDefault()?.Id ? "live" : "last observed")} {s.LastObserved:MMM/dd HH:mm}")).Reverse().ToArray();
        Volatile.Write(ref sessionView, new(sessionEvidence.Revision, records, choices) { ContentRevision = sessionEvidence.ContentRevision });
        }
    }
    private void ReadSessions(LogParser parser)
    {
        if (!File.Exists(GameLogPath)) return;
        var saved = sessionEvidence.Checkpoint;
        var batch = LogTailer.Read(GameLogPath, saved, 1024 * 1024);
        bool live = false;
        if (WorldEvidenceAt >= DateTime.Now.AddMinutes(-2)) foreach (var process in System.Diagnostics.Process.GetProcessesByName("eqgame")) using (process) { try { if (process.MainModule?.FileName is { } path && Path.GetDirectoryName(path)?.Equals(Profile.GameFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true) live = true; } catch (System.ComponentModel.Win32Exception) { } }
        if (gameSessionLive != live) { gameSessionLive = live; sessionEvidence.Revision++; PublishSessions(); }
        if (batch.Lines.Count == 0 && !batch.Reset) { if (sessionSummaryRevision != CombatRevision) { sessionSummaries = store.EncounterSummaries(); sessionSummaryRevision = CombatRevision; sessionEvidence.Revision++; PublishSessions(); } return; }
        lock (sessionEvidenceSync)
        {
        foreach (var line in batch.Lines)
        {
            sessionEvidence.Observe(line.Text, line.Id);
            if (line.Text.Contains("] [", StringComparison.Ordinal) || line.Text.Contains("group", StringComparison.OrdinalIgnoreCase) || line.Text.EndsWith("Welcome to EverQuest Legends!") || line.Text.EndsWith("Welcome to EverQuest!"))
                if (parser.Parse(line.Text, line.Id) is { } parsed) sessionEvidence.ObserveDetails(parsed, line.Id, line.Text);
        }
        sessionEvidence.Checkpoint = batch.Checkpoint;
        if (sessionSummaryRevision != CombatRevision) { sessionSummaries = store.EncounterSummaries(); sessionSummaryRevision = CombatRevision; }
        var copy = EquipmentPositions.Copy(sessionEvidence); QueuePersistence(() => store.SaveSessionEvidence(copy)); PublishSessions();
        }
        SessionImportStatus = batch.CaughtUp ? "Saved sessions available offline · uncertain logout times use last observed activity" : $"Indexing sessions · {batch.Checkpoint.Offset / 1048576d:N1} MB";
    }
    private IReadOnlyList<PlaySession>? SessionRecords => validationAllSessions ? null : Sessions?.Sessions;
    internal PlaySession? SelectedSession => Sessions?.Sessions.FirstOrDefault(s => s.Id == Profile.Meter.SessionId) ?? Sessions?.Sessions.LastOrDefault();
    internal void EnsureSessionLoaded()
    {
        EnsureSessionLoaded(Profile.Meter);
        EnsureSessionLoaded(Profile.OverlayMeter!);
    }
    private void EnsureSessionLoaded(DpsSelection selection)
    {
        if (validationAllSessions || Demo) return;
        DateTime from; DateTime? until; string key;
        if (selection.Scope == "History" && sessionSummaries.FirstOrDefault(f => f.Id == selection.EncounterId) is { } pinned)
        { key = "fight:" + pinned.Id; from = pinned.Start; until = pinned.Start.AddTicks(1); }
        else if (selection.Scope == "Session" && (Sessions?.Sessions.FirstOrDefault(s => s.Id == selection.SessionId) ?? Sessions?.Sessions.LastOrDefault()) is { } selected)
        { key = selected.Id + ":" + selected.Start.Ticks + ":" + selected.NextLogin?.Ticks; from = selected.Start; until = selected.NextLogin; }
        else return;
        if (!hydratedSessions.TryAdd(key, 0)) return;
        _ = Task.Run(() =>
        {
            try
            {
                var loaded = store.LoadEncounters(limit: int.MaxValue, from: from, until: until);
                lock (Sync) { var ids = Combat.Encounters.Select(f => f.Id).ToHashSet(); bool added = false; foreach (var f in loaded.Where(f => f.Finished && ids.Add(f.Id))) { Combat.Encounters.Add(f); added = true; } if (added) historyRevision++; }
                Updated?.Invoke();
            }
            catch (Exception error) when (error is not OutOfMemoryException) { hydratedSessions.TryRemove(key, out _); if (!cancel.IsCancellationRequested) File.AppendAllText(Path.Combine(DataFolder,"errors.log"), DateTime.Now + " Session hydration: " + error + Environment.NewLine); }
        }, cancel.Token);
    }
    internal void CorrectClasses(string sessionId, DateTime from, DateTime until, string[] classes)
    {
        if (classes.Length is < 1 or > 3 || classes.Any(c => !CatalogClasses.Contains(c))) throw new InvalidDataException("Enter 1–3 recognized class abbreviations.");
        lock (sessionEvidenceSync) { var s = sessionEvidence.Sessions.SingleOrDefault(s => s.Id == sessionId) ?? throw new InvalidDataException("Wait for saved session indexing."); if (from < s.Start || until <= from || s.NextLogin.HasValue && until > s.NextLogin) throw new InvalidDataException("Use a time range within this session."); s.Classes.Add(new("manual:" + Guid.NewGuid().ToString("N"), from, SessionEvidence.Normalize(classes), "Explicit user correction", "Manual correction") { Until = until }); sessionEvidence.Revision++; sessionEvidence.ContentRevision++; var copy = EquipmentPositions.Copy(sessionEvidence); QueuePersistence(() => store.SaveSessionEvidence(copy)); PublishSessions(); inspectionIdentityRevision++; }
        Updated?.Invoke();
    }
    private static readonly string[] CatalogClasses = ["WAR", "CLR", "PAL", "RNG", "SHD", "DRU", "MNK", "BRD", "ROG", "SHM", "NEC", "WIZ", "MAG", "ENC", "BST", "BER"];
    internal void PinFight(string id) { lock (Sync) { Profile.Meter.Scope = "History"; Profile.Meter.EncounterId = id; Save(); } }
    internal async Task ValidateSessionImportAsync(string folder)
    {
        var parser = new LogParser(Profile.Character); int i = 0;
        while (i++ < 2048 && File.Exists(GameLogPath) && sessionEvidence.Checkpoint?.Offset != new FileInfo(GameLogPath).Length) { ReadSessions(parser); await Task.Delay(1); }
        await DrainPersistenceAsync();
        Json.Write(Path.Combine(folder, "session-import.json"), new { Sessions = Sessions?.Sessions.Count, Offset = sessionEvidence.Checkpoint?.Offset, Bytes = File.Exists(GameLogPath) ? new FileInfo(GameLogPath).Length : 0, ClassObservations = sessionEvidence.Sessions.Sum(s => s.Classes.Count), Members = sessionEvidence.Sessions.Sum(s => s.Members.Count), SessionImportStatus });
    }
}
