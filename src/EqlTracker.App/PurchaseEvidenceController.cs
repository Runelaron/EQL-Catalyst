using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private FileCheckpoint? purchaseCheckpoint;
    private bool purchaseCheckpointRead;
    private DateTime nextPurchaseRead;
    internal string PurchaseCoverage {get;private set;}="Purchase evidence not yet read";
    private void ReadPurchaseEvidence(LogParser parser)
    {
        if(Demo||cancel.IsCancellationRequested||integratedWorker?.IsCompleted!=true||DateTime.UtcNow<nextPurchaseRead||!File.Exists(GameLogPath))return;
        if(!purchaseCheckpointRead){purchaseCheckpoint=store.Get<FileCheckpoint>(PurchaseEvidence.Version);purchaseCheckpointRead=true;}
        var batch=LogTailer.Read(GameLogPath,purchaseCheckpoint,1024*1024);var rows=new List<JournalActivity>();
        foreach(var line in batch.Lines)
        {
            cancel.Token.ThrowIfCancellationRequested();
            if(!line.Text.Contains("] You purchased ",StringComparison.Ordinal))continue;
            var parsed=parser.Parse(line.Text,line.Id);if(parsed==null)continue;
            string message=line.Text[(line.Text.IndexOf("] ",StringComparison.Ordinal)+2)..];
            if(PurchaseEvidence.Parse(parsed.Time,line.Id,message,"Unknown") is {} activity)rows.Add(activity);
        }
        var frozen=EquipmentPositions.Copy(batch.Checkpoint);purchaseCheckpoint=frozen;
        lock(Sync)
        {
            foreach(var row in rows)if(journal.TryAdd(row.Id,row)){journalRevision++;activityRevision++;}
            PurchaseCoverage=batch.CaughtUp?"Recorded purchase evidence is caught up; not a complete wallet statement":"Reading historical purchase evidence";
        }
        QueuePersistence(()=>store.SavePurchaseEvidence(rows,frozen));
        nextPurchaseRead=DateTime.UtcNow.AddMilliseconds(batch.CaughtUp?3000:0);
    }
}
