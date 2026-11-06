namespace EqlTracker.Core;
public sealed partial class TrackerStore
{
    internal void SaveBardResearch(BardResearchState state,FileCheckpoint? checkpoint)
    {lock(sync){using var transaction=connection.BeginTransaction();State(BardRotations.Version,state,transaction);if(checkpoint!=null)State(BardRotations.Version+":checkpoint",checkpoint,transaction);transaction.Commit();}}
}
