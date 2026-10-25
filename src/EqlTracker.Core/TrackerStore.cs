using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EqlTracker.Core;

public sealed partial class TrackerStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object sync = new();
    private readonly Dictionary<string, string> savedOwners = [];
    private readonly List<(string Id, string Owner)> pendingOwners = [];
    private readonly Dictionary<string, string> savedHealing = [], pendingHealing = [];
    public TrackerStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString()); connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS encounters(id TEXT PRIMARY KEY, start TEXT NOT NULL, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY, encounter TEXT NOT NULL, time TEXT NOT NULL, json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS events_encounter ON events(encounter); CREATE INDEX IF NOT EXISTS events_time ON events(time); CREATE TABLE IF NOT EXISTS observations(id TEXT PRIMARY KEY, time TEXT NOT NULL, json TEXT NOT NULL); PRAGMA user_version=2;");
        EnsureHealingSchema();
        Execute("CREATE TABLE IF NOT EXISTS ability_outcomes(id TEXT PRIMARY KEY,time TEXT NOT NULL,json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ability_outcomes_time ON ability_outcomes(time);");
        Execute("CREATE TABLE IF NOT EXISTS activity_journal(id TEXT PRIMARY KEY,time TEXT NOT NULL,json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS activity_journal_time ON activity_journal(time DESC,id DESC);");
    }
    public void EnsureHealingSchema() => Execute("CREATE TABLE IF NOT EXISTS healing(id TEXT PRIMARY KEY, encounter TEXT NOT NULL, time TEXT NOT NULL, json TEXT NOT NULL); CREATE INDEX IF NOT EXISTS healing_encounter ON healing(encounter); CREATE INDEX IF NOT EXISTS healing_time ON healing(time); PRAGMA user_version=2;");
    public List<AbilityOutcome> LoadAbilityOutcomes()
    {
        lock (sync) { using var c = connection.CreateCommand(); c.CommandText = "SELECT json FROM ability_outcomes ORDER BY time,id"; using var r = c.ExecuteReader(); var result = new List<AbilityOutcome>(); while (r.Read()) if (JsonSerializer.Deserialize<AbilityOutcome>(r.GetString(0), Json.Options) is { } e) result.Add(e); return result; }
    }
    public void SaveAbilityOutcomes(IEnumerable<AbilityOutcome> events, FileCheckpoint? checkpoint = null, DateTime? first = null, DateTime? last = null)
    {
        lock (sync) { using var tx = connection.BeginTransaction(); foreach (var e in events) Execute("INSERT OR IGNORE INTO ability_outcomes VALUES($id,$time,$json)", tx, ("$id", e.Id), ("$time", e.Time.ToString("O")), ("$json", JsonSerializer.Serialize(e, Json.Options))); Execute("DELETE FROM ability_outcomes WHERE time<$cutoff", tx, ("$cutoff", DateTime.Now.AddDays(-30).ToString("O"))); if (checkpoint != null) State("abilityOutcomeCheckpoint", checkpoint, tx); if (first.HasValue) State("abilityOutcomeFirst", first, tx); if (last.HasValue) State("abilityOutcomeLast", last, tx); tx.Commit(); }
    }
    private static string HealingVersion(HealingEvent e) => e.EncounterId + "|" + e.HealerOwner + "|" + e.RecipientOwner + "|" + e.AttributionEvidence;
    private void CommitHealingVersions() { foreach (var (id, version) in pendingHealing) savedHealing[id] = version; pendingHealing.Clear(); }
    public void SaveHealing(HealingEvent e, SqliteTransaction? transaction = null)
    {
        lock (sync)
        {
            string version = HealingVersion(e);
            if (savedHealing.GetValueOrDefault(e.Id) == version || transaction != null && pendingHealing.GetValueOrDefault(e.Id) == version) return;
            Execute("INSERT INTO healing VALUES($id,$encounter,$time,$json) ON CONFLICT(id) DO UPDATE SET encounter=excluded.encounter,json=excluded.json", transaction, ("$id", e.Id), ("$encounter", e.EncounterId), ("$time", e.Time.ToString("O")), ("$json", JsonSerializer.Serialize(e, Json.Options)));
            if (transaction == null) savedHealing[e.Id] = version; else pendingHealing[e.Id] = version;
        }
    }
    public List<HealingEvent> LoadHealing(string? encounter = null)
    {
        lock (sync)
        {
            using var c = connection.CreateCommand(); c.CommandText = "SELECT json,encounter FROM healing WHERE ($encounter IS NULL OR encounter=$encounter) ORDER BY time,id"; c.Parameters.AddWithValue("$encounter", (object?)encounter ?? DBNull.Value);
            using var reader = c.ExecuteReader(); var result = new List<HealingEvent>(); while (reader.Read()) if (JsonSerializer.Deserialize<HealingEvent>(reader.GetString(0), Json.Options) is { } e) { e.EncounterId = reader.GetString(1); result.Add(e); } return result;
        }
    }
    public void CommitHealingBackfill(FileCheckpoint checkpoint, IEnumerable<HealingEvent> events, IEnumerable<Encounter> fights, string? zone = null, DateTime? first = null, DateTime? last = null)
    {
        lock (sync) { pendingHealing.Clear(); using var tx = connection.BeginTransaction(); foreach (var e in events) SaveHealing(e, tx); foreach (var f in fights) SaveEncounter(f, tx); State("healingBackfill", checkpoint, tx); if (zone != null) State("healingBackfillZone", zone, tx); if (first.HasValue) State("healingBackfillFirst", first, tx); if (last.HasValue) State("healingBackfillLast", last, tx); tx.Commit(); CommitHealingVersions(); }
    }
    private void Execute(string sql, SqliteTransaction? transaction = null, params (string, object?)[] parameters)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
    public T? Get<T>(string key)
    {
        lock (sync) { using var c = connection.CreateCommand(); c.CommandText = "SELECT json FROM state WHERE key=$key"; c.Parameters.AddWithValue("$key", key); return c.ExecuteScalar() is string s ? JsonSerializer.Deserialize<T>(s, Json.Options) : default; }
    }
    public void Set<T>(string key, T value)
    { lock (sync) Execute("INSERT INTO state VALUES($key,$json) ON CONFLICT(key) DO UPDATE SET json=excluded.json", null, ("$key", key), ("$json", JsonSerializer.Serialize(value, Json.Options))); }
    public void Commit(FileCheckpoint checkpoint, CharacterProfile profile, InventorySnapshot inventory, IEnumerable<Encounter> fights, Encounter? current, IEnumerable<(string Id, ItemObservation Item)> observations, IEnumerable<HealingEvent>? healing = null, bool saveProfile = true, bool saveInventory = true, IEnumerable<JournalActivity>? activity = null)
    {
        lock (sync)
        {
            pendingOwners.Clear(); pendingHealing.Clear();
            using var tx = connection.BeginTransaction();
            foreach (var fight in fights.Concat(current == null ? [] : [current])) SaveEncounter(fight, tx);
            if (healing != null) foreach (var e in healing) SaveHealing(e, tx);
            State("checkpoint", checkpoint, tx); if (saveProfile) State("profile", profile, tx); if (saveInventory) State("inventory", inventory, tx); State("liveEncounter", current == null ? null : Summary(current), tx);
            foreach (var (id, item) in observations) Execute("INSERT OR IGNORE INTO observations VALUES($id,$time,$json)", tx, ("$id", id), ("$time", item.Time.ToString("O")), ("$json", JsonSerializer.Serialize(item, Json.Options)));
            if (activity != null) foreach (var item in activity) SaveActivity(item, tx);
            tx.Commit(); CommitHealingVersions();
            foreach (var (id, owner) in pendingOwners) savedOwners[id] = owner;
            pendingOwners.Clear();
        }
    }
    private void State<T>(string key, T value, SqliteTransaction tx) => Execute("INSERT INTO state VALUES($key,$json) ON CONFLICT(key) DO UPDATE SET json=excluded.json", tx, ("$key", key), ("$json", JsonSerializer.Serialize(value, Json.Options)));
    public void SaveEncounter(Encounter fight, SqliteTransaction? transaction = null)
    {
        lock (sync)
        {
            var summary = Summary(fight);
            foreach (var e in fight.Heals) SaveHealing(e, transaction);
            Execute("INSERT INTO encounters VALUES($id,$start,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json", transaction, ("$id", fight.Id), ("$start", fight.Start.ToString("O")), ("$json", JsonSerializer.Serialize(summary, Json.Options)));
            foreach (var e in fight.Events)
            {
                if (savedOwners.GetValueOrDefault(e.Id) == e.Owner) continue;
                Execute("INSERT INTO events VALUES($id,$encounter,$time,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json", transaction, ("$id", e.Id), ("$encounter", fight.Id), ("$time", e.Time.ToString("O")), ("$json", JsonSerializer.Serialize(e, Json.Options)));
                if (transaction == null) savedOwners[e.Id] = e.Owner; else pendingOwners.Add((e.Id, e.Owner));
            }
        }
    }
    private static Encounter Summary(Encounter fight) => new() { Id = fight.Id, Start = fight.Start, End = fight.End, Zone = fight.Zone, Loadout = fight.Loadout, PetType = fight.PetType, PetName = fight.PetName, IdleResumable = fight.IdleResumable, ResumeCount = fight.ResumeCount, ResolvedTargets = fight.ResolvedTargets, EndReason = fight.EndReason, PetElementEvidence = fight.PetElementEvidence, Peaks = fight.Peaks, Weapons = fight.Weapons, BuffNotes = fight.BuffNotes, PlayerDamage = fight.PlayerDamage, PetDamage = fight.PetDamage, UnknownDamage = fight.UnknownDamage, Finished = fight.Finished, DetailAvailable = fight.DetailAvailable, HealingCoverage = fight.HealingCoverage, HealingDetailAvailable = fight.HealingDetailAvailable, HealingSummaries = fight.HealingSummaries };
    public List<Encounter> LoadEncounters(DateTime? before = null, int limit = 2000, DateTime? from = null, DateTime? until = null)
    {
        lock (sync)
        {
            var fights = new List<Encounter>();
            using (var c = connection.CreateCommand()) { c.CommandText = "SELECT json FROM encounters WHERE ($before IS NULL OR start < $before) AND ($from IS NULL OR start >= $from) AND ($until IS NULL OR start < $until) ORDER BY start DESC LIMIT $limit"; c.Parameters.AddWithValue("$before", before?.ToString("O") ?? (object)DBNull.Value); c.Parameters.AddWithValue("$limit", limit); c.Parameters.AddWithValue("$from", from?.ToString("O") ?? (object)DBNull.Value); c.Parameters.AddWithValue("$until", until?.ToString("O") ?? (object)DBNull.Value); using var reader = c.ExecuteReader(); while (reader.Read()) if (JsonSerializer.Deserialize<Encounter>(reader.GetString(0), Json.Options) is { } f) fights.Add(f); }
            foreach (var fight in fights)
            {
                using var c = connection.CreateCommand(); c.CommandText = "SELECT json FROM events WHERE encounter=$id ORDER BY time,id"; c.Parameters.AddWithValue("$id", fight.Id); using var r = c.ExecuteReader(); while (r.Read()) if (JsonSerializer.Deserialize<CombatEvent>(r.GetString(0), Json.Options) is { } e) { fight.Events.Add(e); savedOwners[e.Id] = e.Owner; }
                fight.Heals = LoadHealing(fight.Id);
                fight.HealingDetailAvailable = fight.HealingDetailAvailable && (fight.HealingSummaries.Sum(s => s.Count) == fight.Heals.Sum(e => (e.HealerOwner is "Player" or "Pet" ? 1 : 0) + (e.RecipientOwner is "Player" or "Pet" ? 1 : 0)));
                fight.DetailAvailable = fight.Events.Count > 0 && fight.Events.Where(x => x.Owner is "Pet" or "Player").Sum(x => x.Damage) == fight.Damage;
            }
            return fights;
        }
    }
    public List<Encounter> EncounterSummaries()
    { lock (sync) { using var c = connection.CreateCommand(); c.CommandText = "SELECT json FROM encounters ORDER BY start"; using var r = c.ExecuteReader(); var result = new List<Encounter>(); while (r.Read()) if (JsonSerializer.Deserialize<Encounter>(r.GetString(0), Json.Options) is { } f) result.Add(f); return result; } }
    public void SaveSessionEvidence(SessionEvidence evidence)
    { lock (sync) { using var tx = connection.BeginTransaction(); State("sessionEvidence", evidence, tx); tx.Commit(); } }
    public List<ItemObservation> ObservationsAfter(DateTime time)
    {
        lock (sync) { var items = new List<ItemObservation>(); using var c = connection.CreateCommand(); c.CommandText = "SELECT json FROM observations WHERE time > $time ORDER BY time,rowid"; c.Parameters.AddWithValue("$time", time.ToString("O")); using var r = c.ExecuteReader(); while (r.Read()) if (JsonSerializer.Deserialize<ItemObservation>(r.GetString(0), Json.Options) is { } i) items.Add(i); return items; }
    }
    public List<(string Id, ItemObservation Item)> ObservationRecordsAfter(DateTime time)
    {
        lock (sync)
        {
            var items = new List<(string, ItemObservation)>(); using var c = connection.CreateCommand();
            c.CommandText = "SELECT id,json FROM observations WHERE time >= $time ORDER BY time,rowid"; c.Parameters.AddWithValue("$time", time.ToString("O"));
            using var r = c.ExecuteReader(); while (r.Read()) if (JsonSerializer.Deserialize<ItemObservation>(r.GetString(1), Json.Options) is { } item) items.Add((r.GetString(0), item)); return items;
        }
    }
    public void DeleteEncounter(string id)
    { lock (sync) { using var tx = connection.BeginTransaction(); Execute("DELETE FROM events WHERE encounter=$id", tx, ("$id", id)); Execute("DELETE FROM encounters WHERE id=$id", tx, ("$id", id)); Execute("DELETE FROM healing WHERE encounter=$id", tx, ("$id", id)); tx.Commit(); } }
    public void ReplaceEncounters(IEnumerable<string> remove, List<Encounter> replacements, EncounterEditUndo? undo)
    {
        lock (sync)
        {
            using var tx = connection.BeginTransaction();
            foreach (var id in remove) { Execute("UPDATE healing SET encounter='' WHERE encounter=$id", tx, ("$id", id)); Execute("DELETE FROM events WHERE encounter=$id", tx, ("$id", id)); Execute("DELETE FROM encounters WHERE id=$id", tx, ("$id", id)); }
            savedOwners.Clear(); pendingOwners.Clear(); savedHealing.Clear(); pendingHealing.Clear();
            foreach (var fight in replacements) SaveEncounter(fight, tx);
            State("encounterEditUndo", undo, tx); tx.Commit(); CommitHealingVersions();
            foreach (var (id, owner) in pendingOwners) savedOwners[id] = owner;
            pendingOwners.Clear();
        }
    }
    public void Retain(DateTime cutoff) { lock (sync) { Execute("DELETE FROM events WHERE time < $time", null, ("$time", cutoff.ToString("O"))); Execute("DELETE FROM healing WHERE time < $time", null, ("$time", cutoff.ToString("O"))); } }
    public void Dispose() => connection.Dispose();
}
