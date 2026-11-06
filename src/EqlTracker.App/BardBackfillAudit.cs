using System.Diagnostics;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    internal async Task AuditBardBackfill(string folder)
    {
        if(!Path.GetFileName(folder).StartsWith("bard-copy-",StringComparison.Ordinal))throw new InvalidOperationException("An isolated retained-data copy is required");
        await PrepareIntegratedValidation();string before=PetResearch.Hash(new{Profile,Inventory,Damage=Combat.Encounters.Select(f=>new{f.Id,f.Start,f.End,f.PlayerDamage,f.PetDamage,f.UnknownDamage})});var watch=Stopwatch.StartNew();long pulses=0;
        RefreshStatEvidence();ReadStatEvidence();
        while(BardRotations.MetadataReady(SpellIndexSnapshot())&&watch.Elapsed.TotalSeconds<150)
        {
            nextBardRead=DateTime.MinValue;ReadBardResearch();pulses++;
            if(bardCheckpoint!=null&&bardCheckpoint.Offset>=new FileInfo(GameLogPath).Length)break;
            await Task.Delay(20);
        }
        await DrainPersistenceAsync();string after=PetResearch.Hash(new{Profile,Inventory,Damage=Combat.Encounters.Select(f=>new{f.Id,f.Start,f.End,f.PlayerDamage,f.PetDamage,f.UnknownDamage})});
        bool ready=BardRotations.MetadataReady(SpellIndexSnapshot()),caught=bardCheckpoint!=null&&bardCheckpoint.Offset>=new FileInfo(GameLogPath).Length;
        var state=BardResearchSnapshot();Json.Write(Path.Combine(folder,"bard-backfill-audit.json"),new{Passed=ready&&caught&&before==after&&state.Known.Count>0,Ready=ready,CaughtUp=caught,ProfileInventoryAndCombatUnchanged=before==after,KnownVariants=state.Known.Count,SavedSets=state.Saved.Count,ConfirmedSets=state.Common.Count,Seconds=watch.Elapsed.TotalSeconds,Polls=pulses,IsolatedCopy=true});
        if(!ready||!caught||before!=after||state.Known.Count==0)throw new InvalidOperationException("Bard retained-data backfill audit did not pass");
    }
}
