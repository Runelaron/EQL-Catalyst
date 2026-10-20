using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private StatEvidenceState statEvidence = new();
    private FileCheckpoint? statCheckpoint;
    private SpellMessageIndex? statMessages;
    private string statMessageFolder = "";
    private DateTime statMessageWrite;
    private DateTime nextStatRead;
    public long StatEvidenceRevision { get { lock(Sync)return statEvidence.Revision; } }
    public StatEvidenceState StatEvidenceSnapshot() { lock(Sync)return EquipmentPositions.Copy(statEvidence); }
    public void RefreshStatEvidence() { lock(Sync)nextStatRead=DateTime.MinValue; }
    internal void SeedStatEvidencePreview()
    {
        if(!Demo)throw new InvalidOperationException("Stat evidence fixtures require preview mode.");
        StatEvidenceReader.Observe(statEvidence,"[Sun Oct 04 20:09:29 2026] You have improved Combat Fury 4 at a cost of 4 ability points.","preview:1",Profile.Character,[],null);
        StatEvidenceReader.Observe(statEvidence,"[Thu Oct 08 22:46:21 2026] You have gained the ability \"Combat Stability\" at a cost of 2 ability points.","preview:2",Profile.Character,[],null);
        StatEvidenceReader.Observe(statEvidence,"[Thu Oct 15 21:34:38 2026] Your Spirit of Wolf spell has worn off.","preview:3",Profile.Character,[],null);
        statEvidence.Status="Preview fixture · real log formats, illustrative profile totals";
    }
    internal async Task ValidateStatEvidenceAsync(string folder)
    {
        string before=System.Text.Json.JsonSerializer.Serialize(new {Profile,Inventory,Damage=Combat.Encounters.Sum(f=>f.Damage),Times=Combat.Encounters.Select(f=>new {f.Id,f.Start,f.End})});
        var clock=System.Diagnostics.Stopwatch.StartNew();string log=GameLogPath;
        if(File.Exists(log))for(int i=0;i<10000;i++){RefreshStatEvidence();ReadStatEvidence();if(statEvidence.CaughtUp)break;await Task.Delay(1);}
        string after=System.Text.Json.JsonSerializer.Serialize(new {Profile,Inventory,Damage=Combat.Encounters.Sum(f=>f.Damage),Times=Combat.Encounters.Select(f=>new {f.Id,f.Start,f.End})});
        Json.Write(Path.Combine(folder,"stat-evidence-report.json"),new {Available=File.Exists(log),Passed=before==after && (!File.Exists(log) || statEvidence.CaughtUp),Seconds=clock.Elapsed.TotalSeconds,ProfileInventoryDamageAndTimingUnchanged=before==after,statEvidence.Level,statEvidence.Classes,ExactRanks=statEvidence.AAs.Values.Count(a=>a.Rank.HasValue),LearnedWithoutRank=statEvidence.AAs.Values.Count(a=>a.Rank==null),Ranks=statEvidence.AAs.Values.OrderBy(a=>a.Name),EffectEvidence=statEvidence.Effects.Count,statEvidence.SpellSource,CurrentBuffRoster="Unknown; effect history is not an active list",StatTotals="Unavailable in inspected export/log/INI; worn contributions remain estimates"});
    }
    private void ReadStatEvidence()
    {
        string folder,character,server;FileCheckpoint? offset;List<PetIdentity> pets;
        lock(Sync)
        {
            if(DateTime.UtcNow<nextStatRead)return;nextStatRead=DateTime.UtcNow.AddSeconds(3);
            folder=Profile.GameFolder;character=Profile.Character;server=Profile.Server;offset=statCheckpoint;pets=Profile.PetIdentities.ToList();
        }
        try
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            var log=Path.Combine(folder,"Logs",$"eqlog_{character}_{server}.txt");
            if(!File.Exists(log)){lock(Sync){if(statEvidence.Status!="Character log missing · cached evidence retained")statEvidence.Revision++;statEvidence.Status="Character log missing · cached evidence retained";}return;}
            string names=Path.Combine(folder,"spells_us.txt"),messages=Path.Combine(folder,"spells_us_str.txt");
            if(statMessageFolder!=folder || statMessages==null || File.GetLastWriteTimeUtc(messages)!=statMessageWrite)
            {
                statMessageFolder=folder;statMessages=null;
                if(File.Exists(names) && File.Exists(messages))
                {
                    statMessages=SpellMessageIndex.Parse(File.ReadLines(names),File.ReadLines(messages));
                    statMessageWrite=File.GetLastWriteTimeUtc(messages);
                    lock(Sync)statEvidence.SpellSource=$"Installed spell-message files · {File.GetLastWriteTime(messages):MMM/dd}";
                }
            }
            var batch=LogTailer.Read(log,offset);StatEvidenceState updated;
            lock(Sync)updated=EquipmentPositions.Copy(statEvidence);
            foreach(var line in batch.Lines)StatEvidenceReader.Observe(updated,line.Text,line.Id,character,pets,statMessages);
            lock(Sync)
            {
                bool wasCaughtUp=statEvidence.CaughtUp;
                statEvidence=updated;
                statCheckpoint=batch.Checkpoint;statEvidence.CaughtUp=batch.CaughtUp;
                if(wasCaughtUp!=batch.CaughtUp)statEvidence.Revision++;
                statEvidence.Status=batch.CaughtUp?"Following character log · historical evidence, not a complete active-stat snapshot":"Reading retained stat/AA/effect evidence…";
                if(batch.Lines.Count>0 || batch.Reset)
                {var savedEvidence = EquipmentPositions.Copy(statEvidence); var savedCheckpoint = EquipmentPositions.Copy(statCheckpoint); QueuePersistence(() => { store.Set("statEvidence", savedEvidence); store.Set("statEvidenceCheckpoint", savedCheckpoint); });}
                if(!batch.CaughtUp)nextStatRead=DateTime.MinValue;
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
        {lock(Sync){if(statEvidence.Status!="Game-data read deferred · cached evidence retained")statEvidence.Revision++;statEvidence.Status="Game-data read deferred · cached evidence retained";nextStatRead=DateTime.UtcNow.AddSeconds(10);}}
    }
}
