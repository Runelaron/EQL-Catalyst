using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EqlTracker.Core;

public sealed class CatalogSyncState
{
    public int Version { get; set; } = 1;
    public string Continuation { get; set; } = "";
    public DateTime? IndexedAt { get; set; }
    public int Discovered { get; set; }
    public int Checked { get; set; }
    public int Equipment { get; set; }
    public string Status { get; set; } = "Local catalog ready";
}
public sealed class EquipmentSnapshot
{
    public int Version { get; set; } = 1;
    public DateTime GeneratedAt { get; set; }
    public List<string> Titles { get; set; } = [];
    public List<ItemDefinition> Items { get; set; } = [];
}

public sealed class EquipmentCatalog : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    public EquipmentCatalog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        db = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS titles(title TEXT PRIMARY KEY, checked TEXT); CREATE TABLE IF NOT EXISTS items(key TEXT PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS sync(id INTEGER PRIMARY KEY,json TEXT NOT NULL); PRAGMA user_version=1;");
        using var columns = db.CreateCommand(); columns.CommandText = "SELECT count(*) FROM pragma_table_info('titles') WHERE name='parser_version'";
        if (Convert.ToInt32(columns.ExecuteScalar()) == 0) Execute("ALTER TABLE titles ADD COLUMN parser_version INTEGER NOT NULL DEFAULT 0;");
        Execute("PRAGMA user_version=2;");
    }
    private void Execute(string sql) { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    public CatalogSyncState State()
    {
        lock (gate) { using var c = db.CreateCommand(); c.CommandText = "SELECT json FROM sync WHERE id=1"; return c.ExecuteScalar() is string s ? JsonSerializer.Deserialize<CatalogSyncState>(s, Json.Options)! : new(); }
    }
    public void SaveState(CatalogSyncState state)
    {
        lock (gate) { using var c = db.CreateCommand(); c.CommandText = "INSERT INTO sync VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json"; c.Parameters.AddWithValue("$json", JsonSerializer.Serialize(state, Json.Options)); c.ExecuteNonQuery(); }
    }
    public List<ItemDefinition> Items()
    {
        lock (gate) { using var c = db.CreateCommand(); c.CommandText = "SELECT json FROM items"; using var r = c.ExecuteReader(); var items = new List<ItemDefinition>(); while (r.Read()) items.Add(JsonSerializer.Deserialize<ItemDefinition>(r.GetString(0), Json.Options)!); return items; }
    }
    public void Index(IEnumerable<string> titles)
    {
        lock (gate) { using var tx = db.BeginTransaction(); foreach (var title in titles) { using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT OR IGNORE INTO titles(title) VALUES($title)"; c.Parameters.AddWithValue("$title", title); c.ExecuteNonQuery(); } tx.Commit(); }
    }
    public List<string> Pending(DateTime before, int limit = 50, IEnumerable<string>? priority = null)
    {
        lock (gate) { using var c = db.CreateCommand(); c.CommandText = "SELECT title FROM titles WHERE checked IS NULL OR checked < $before OR parser_version < $version ORDER BY CASE WHEN instr($priority,'|'||lower(title)||'|')>0 THEN 0 ELSE 1 END,checked,title LIMIT $limit"; c.Parameters.AddWithValue("$priority", "|" + string.Join('|', (priority ?? []).Select(n => n.ToLowerInvariant())) + "|"); c.Parameters.AddWithValue("$before", before.ToUniversalTime().ToString("O")); c.Parameters.AddWithValue("$version", PetItemRules.MetadataVersion); c.Parameters.AddWithValue("$limit", limit); using var r = c.ExecuteReader(); var names = new List<string>(); while (r.Read()) names.Add(r.GetString(0)); return names; }
    }
    public List<string> SearchPending(string query)
    {
        lock (gate) { using var c = db.CreateCommand(); c.CommandText = "SELECT title FROM titles WHERE checked IS NULL AND instr(lower(title),lower($query))>0 ORDER BY title LIMIT 20"; c.Parameters.AddWithValue("$query", query); using var r = c.ExecuteReader(); var names = new List<string>(); while (r.Read()) names.Add(r.GetString(0)); return names; }
    }
    public void Store(IEnumerable<ItemDefinition> items, IEnumerable<string> checkedTitles, int checkedVersion = PetItemRules.MetadataVersion)
    {
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            foreach (var item in items) { using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO items VALUES($key,$json) ON CONFLICT(key) DO UPDATE SET json=excluded.json WHERE coalesce(json_extract(excluded.json,'$.MetadataVersion'),0) >= coalesce(json_extract(items.json,'$.MetadataVersion'),0) AND coalesce(json_extract(excluded.json,'$.Revision'),0) >= coalesce(json_extract(items.json,'$.Revision'),0)"; c.Parameters.AddWithValue("$key", item.Key); c.Parameters.AddWithValue("$json", JsonSerializer.Serialize(item, Json.Options)); c.ExecuteNonQuery(); }
            foreach (var title in checkedTitles) { using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "UPDATE titles SET checked=$time,parser_version=$version WHERE title=$title"; c.Parameters.AddWithValue("$title", title); c.Parameters.AddWithValue("$version", checkedVersion); c.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O")); c.ExecuteNonQuery(); }
            tx.Commit();
        }
    }
    public void Counts(CatalogSyncState state)
    {
        lock (gate) { using var c = db.CreateCommand(); c.CommandText = "SELECT count(*),count(checked),(SELECT count(*) FROM items) FROM titles"; using var r = c.ExecuteReader(); if (r.Read()) { state.Discovered = r.GetInt32(0); state.Checked = r.GetInt32(1); state.Equipment = r.GetInt32(2); } }
    }
    public void Dispose() { lock (gate) db.Dispose(); }
}
