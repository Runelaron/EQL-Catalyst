using System.Text.Json;
using System.Security.Cryptography;
using EqlTracker.Core;
using Microsoft.Data.Sqlite;

static class InventoryReplayAudit
{
    public static void Run(string before,string after)
    {
        using var old=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=before,Mode=SqliteOpenMode.ReadOnly}.ToString());old.Open();
        using var current=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=after,Mode=SqliteOpenMode.ReadOnly}.ToString());current.Open();using var snapshot=current.BeginTransaction();
        string State(SqliteConnection db,string key){using var command=db.CreateCommand();if(db==current)command.Transaction=snapshot;command.CommandText="select json from state where key=$key";command.Parameters.AddWithValue("$key",key);return (string)command.ExecuteScalar()!;}
        var inventory=JsonSerializer.Deserialize<InventorySnapshot>(State(old,"inventory"),Json.Options)!;
        var expected=JsonSerializer.Deserialize<InventorySnapshot>(State(current,"inventory"),Json.Options)!;
        var oldIds=new HashSet<string>();using(var command=old.CreateCommand()){command.CommandText="select id from observations";using var rows=command.ExecuteReader();while(rows.Read())oldIds.Add(rows.GetString(0));}
        var events=new List<(string Id,ItemObservation Item)>();using(var command=current.CreateCommand()){command.Transaction=snapshot;command.CommandText="select id,json from observations order by time,id";using var rows=command.ExecuteReader();while(rows.Read())if(!oldIds.Contains(rows.GetString(0)))events.Add((rows.GetString(0),JsonSerializer.Deserialize<ItemObservation>(rows.GetString(1),Json.Options)!));}
        foreach(var e in events)InventoryParser.Apply(inventory,e.Item,e.Id);
        string observed=JsonSerializer.Serialize(expected,Json.Options),replayed=JsonSerializer.Serialize(inventory,Json.Options);
        var observedNode=JsonSerializer.SerializeToNode(expected,Json.Options)!.AsObject();var replayedNode=JsonSerializer.SerializeToNode(inventory,Json.Options)!.AsObject();observedNode.Remove("Revision");replayedNode.Remove("Revision");
        if(!System.Text.Json.Nodes.JsonNode.DeepEquals(observedNode,replayedNode))
        {
            var a=JsonSerializer.SerializeToNode(inventory,Json.Options)!.AsObject();var b=JsonSerializer.SerializeToNode(expected,Json.Options)!.AsObject();
            Console.WriteLine(JsonSerializer.Serialize(new{ExactReplay=false,DifferingFields=a.Where(pair=>!System.Text.Json.Nodes.JsonNode.DeepEquals(pair.Value,b[pair.Key])).Select(pair=>new{Field=pair.Key,Expected=pair.Key=="Entries"?"Entry array":b[pair.Key]?.ToJsonString(),Replayed=pair.Key=="Entries"?"Entry array":pair.Value?.ToJsonString()}),Events=events.Select(e=>new{e.Id,e.Item.Name,e.Item.Action})},Json.Options));
            if(!System.Text.Json.Nodes.JsonNode.DeepEquals(a["Entries"],b["Entries"]))Console.WriteLine(JsonSerializer.Serialize(new{ReplayedEntries=inventory.Entries.Where(e=>!expected.Entries.Any(v=>JsonSerializer.Serialize(v,Json.Options)==JsonSerializer.Serialize(e,Json.Options))),ExpectedEntries=expected.Entries.Where(e=>!inventory.Entries.Any(v=>JsonSerializer.Serialize(v,Json.Options)==JsonSerializer.Serialize(e,Json.Options)))},Json.Options));
            Environment.ExitCode=1;return;
        }
        Console.WriteLine(JsonSerializer.Serialize(new{ExactReplay=true,NewObservations=events.Select(e=>new{e.Id,e.Item.Time,e.Item.Name,e.Item.Action,e.Item.Result}),InventorySha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(observed))),Statement="Applying only new observed game actions to the quiescent backup reproduces all current inventory evidence exactly; the transient cache revision is excluded."},Json.Options));
    }
}
