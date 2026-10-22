using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private FileCheckpoint? rankCheckpoint;
    private bool ranksInitialized;
    private void BackfillItemRanks(LogParser parser)
    {
        if (!ranksInitialized) { rankCheckpoint = store.Get<FileCheckpoint>("itemRankCheckpoint-v1"); ranksInitialized = true; }
        if (!File.Exists(GameLogPath)) return;
        if (rankCheckpoint == null && checkpoint != null) { rankCheckpoint = EquipmentPositions.Copy(checkpoint); rankCheckpoint.Offset = 0; }
        var batch = LogTailer.Read(GameLogPath, rankCheckpoint, 256 * 1024);
        if (rankCheckpoint != null && batch.Checkpoint.Offset == rankCheckpoint.Offset && !batch.Reset) return;
        var found = new List<(string Id, ItemObservation Item)>();
        foreach (var line in batch.Lines)
        {
            cancel.Token.ThrowIfCancellationRequested();
            if (!line.Text.Contains("You have successfully merged two items together to create a new item:", StringComparison.Ordinal)) continue;
            try { if (parser.Parse(line.Text, line.Id)?.Item is { Action: "Upgraded" } item) found.Add((line.Id, item)); }
            catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { }
        }
        lock (Sync)
        {
            foreach (var row in found)
            {
                InventoryParser.Apply(Inventory, row.Item, row.Id);
                observations.Add(row);
            }
            if (found.Count > 0) { planningRevision++; saveRequested = true; }
            rankCheckpoint = batch.Checkpoint;
            var frozen = EquipmentPositions.Copy(rankCheckpoint);
            // The ordered writer commits observations/inventory before advancing this cursor.
            Flush(); QueuePersistence(() => store.Set("itemRankCheckpoint-v1", frozen));
        }
    }
}
