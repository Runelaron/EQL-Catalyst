using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EqlTracker.Core;

public sealed partial class TrackerStore
{
    internal List<JournalActivity> ActivitiesForSession(PlaySession session,CancellationToken token)
    {
        lock(sync)
        {
            using var command=connection.CreateCommand();
            command.CommandText="SELECT json FROM activity_journal WHERE time >= $start AND time <= $end AND ($next IS NULL OR time < $next) ORDER BY time,id";
            command.Parameters.AddWithValue("$start",session.Start.ToString("O"));command.Parameters.AddWithValue("$end",session.LastObserved.ToString("O"));command.Parameters.AddWithValue("$next",session.NextLogin?.ToString("O")??(object)DBNull.Value);
            using var reader=command.ExecuteReader();var rows=new List<JournalActivity>();
            while(reader.Read()){token.ThrowIfCancellationRequested();if(JsonSerializer.Deserialize<JournalActivity>(reader.GetString(0),Json.Options) is {} row)rows.Add(row);}
            return rows;
        }
    }
    private void SaveActivity(JournalActivity activity, SqliteTransaction? tx) => Execute("INSERT OR IGNORE INTO activity_journal VALUES($id,$time,$json)", tx, ("$id", activity.Id), ("$time", activity.At.ToString("O")), ("$json", JsonSerializer.Serialize(activity, Json.Options)));
    public void SaveActivities(IEnumerable<JournalActivity> activities)
    { lock (sync) { using var tx = connection.BeginTransaction(); foreach (var item in activities) SaveActivity(item, tx); tx.Commit(); } }
    public List<JournalActivity> LoadActivities(DateTime? before = null, string beforeId = "", int limit = 2000)
    {
        lock (sync)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM activity_journal WHERE $before IS NULL OR time < $before OR (time=$before AND id<$id) ORDER BY time DESC,id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$before", before?.ToString("O") ?? (object)DBNull.Value); command.Parameters.AddWithValue("$id", beforeId); command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 10000));
            using var reader = command.ExecuteReader(); var result = new List<JournalActivity>();
            while (reader.Read()) if (JsonSerializer.Deserialize<JournalActivity>(reader.GetString(0), Json.Options) is { } activity) result.Add(activity);
            return result;
        }
    }
    public void MigrateJournal(Catalog catalog)
    {
        lock (sync)
        {
            if (Get<bool>("activity-journal-migrated-v1")) return;
            var legacy = ObservationRecordsAfter(DateTime.MinValue).Select(row => ActivityJournal.Receipt(row.Id, row.Item, "Unknown", catalog, null)).ToArray();
            using var tx = connection.BeginTransaction(); foreach (var receipt in legacy) SaveActivity(receipt, tx);
            State("activity-journal-migrated-v1", true, tx); tx.Commit();
        }
    }
}
