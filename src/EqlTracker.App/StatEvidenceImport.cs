using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private StatEvidenceState statEvidence = new();
    private FileCheckpoint? statCheckpoint;
    private SpellMessageIndex? statMessages;
    private string statMessageFolder = "";
    private DateTime statMessageWrite;
    private DateTime statNamesWrite;
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
            DateTime messageWrite=File.GetLastWriteTimeUtc(messages),namesWrite=File.GetLastWriteTimeUtc(names);
            bool clientChanged=statMessageFolder!=folder||messageWrite!=statMessageWrite||namesWrite!=statNamesWrite;
            if(clientChanged||connectedReferenceRevision!=referenceRevision)
            {
                RefreshConnectedSpellClient(names);
                if(clientChanged)
                {
                    SpellMessageIndex? nextMessages=null;
                    string source="Spell messages unavailable; retained history preserved";
                    if(File.Exists(names)&&File.Exists(messages))
                    {
                        nextMessages=SpellMessageIndex.Parse(File.ReadLines(names),File.ReadLines(messages));
                        string Sha(string file){using var input=File.OpenRead(file);return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input));}
                        source=$"Read-only spell messages · {messageWrite:yyyy-MM-dd} UTC · spells_us.txt SHA-256: {Sha(names)} · spells_us_str.txt SHA-256: {Sha(messages)}";
                    }
                    statMessages=nextMessages;statMessageFolder=folder;statMessageWrite=messageWrite;statNamesWrite=namesWrite;
                    lock(Sync)statEvidence.SpellSource=source;
                }
            }
            var batch=LogTailer.Read(log,offset);
            bool sharedOffset=connectedCheckpoint!=null&&offset!=null&&connectedCheckpoint.Generation==offset.Generation&&connectedCheckpoint.Offset==offset.Offset;
            var metadata=sharedOffset?null:LogTailer.Read(log,connectedCheckpoint,128*1024);
            StatEvidenceState updated;ConnectedSpellState spells;
            lock(Sync){updated=EquipmentPositions.Copy(statEvidence);spells=EquipmentPositions.Copy(connectedSpells);}
            var index=SpellIndexSnapshot();var evidenceParser=new LogParser(character);
            foreach(var line in batch.Lines)
            {
                cancel.Token.ThrowIfCancellationRequested();
                if(evidenceParser.Parse(line.Text,line.Id) is {} parsed)ConnectedSpellEvidence.Attempt(spells,parsed,line.Id,character,pets,index);
                StatEvidenceReader.Observe(updated,line.Text,line.Id,character,pets,statMessages,effect=>{ObserveResearchEffect(effect);ConnectedSpellEvidence.Effect(spells,effect,index);});
            }
            if(metadata!=null)ConnectedSpellEvidence.ReadMetadata(spells,metadata.Lines,character,pets,index,statMessages,cancel.Token);
            lock(Sync)
            {
                bool wasCaughtUp=statEvidence.CaughtUp;
                statEvidence=updated;
                connectedSpells=spells;
                RefreshStandardPetBuffs();
                connectedCheckpoint=metadata?.Checkpoint??batch.Checkpoint;
                statCheckpoint=batch.Checkpoint;statEvidence.CaughtUp=batch.CaughtUp;
                if(wasCaughtUp!=batch.CaughtUp)statEvidence.Revision++;
                statEvidence.Status=batch.CaughtUp?"Following character log · historical evidence, not a complete active-stat snapshot":"Reading retained stat/AA/effect evidence…";
                if(batch.Lines.Count>0 || batch.Reset || metadata is {Lines.Count:>0} or {Reset:true})
                {var savedEvidence = EquipmentPositions.Copy(statEvidence); var savedCheckpoint = EquipmentPositions.Copy(statCheckpoint);var savedSpells=EquipmentPositions.Copy(connectedSpells);var savedConnectedCheckpoint=EquipmentPositions.Copy(connectedCheckpoint); QueuePersistence(() => { store.Set("statEvidence", savedEvidence); store.Set("statEvidenceCheckpoint", savedCheckpoint);store.Set("connectedSpellEvidence-v1",savedSpells);store.Set("connectedSpellCheckpoint-v1",savedConnectedCheckpoint); });}
                if(!batch.CaughtUp||metadata is {CaughtUp:false})nextStatRead=DateTime.MinValue;
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
        {lock(Sync){if(statEvidence.Status!="Game-data read deferred · cached evidence retained")statEvidence.Revision++;statEvidence.Status="Game-data read deferred · cached evidence retained";nextStatRead=DateTime.UtcNow.AddSeconds(10);}}
    }
}
