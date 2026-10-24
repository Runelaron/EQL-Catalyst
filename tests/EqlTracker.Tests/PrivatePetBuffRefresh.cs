using EqlTracker.Core;

// Offline maintenance of a caller-supplied private COPY. Never opens the live combat database.
static class PrivatePetBuffRefresh
{
    internal sealed class Request
    {
        public string Log {get;set;}="";
        public string Names {get;set;}="";
        public string Messages {get;set;}="";
        public FileCheckpoint Checkpoint {get;set;}=new();
    }
    internal static void Run(string folder)
    {
        var request=Json.Read<Request>(Path.Combine(folder,"request.json"));var profile=Json.Read<CharacterProfile>(Path.Combine(folder,"profile.json"));var research=Json.Read<PetResearchState>(Path.Combine(folder,"research.json"));var setup=Json.Read<StandardPetBuffState>(Path.Combine(folder,"standard.json"));var fights=Json.Read<List<Encounter>>(Path.Combine(folder,"encounters.json"));
        string combatBefore=PetResearch.Hash(fights.Select(f=>new{f.Id,f.Start,f.End,f.PlayerDamage,f.PetDamage,f.UnknownDamage,f.Events,f.Heals}));
        foreach(var sheet in research.Sheets)sheet.Validate();foreach(var buff in research.Buffs)PetResearch.NormalizeBuff(buff);
        var messages=SpellMessageIndex.Parse(File.ReadLines(request.Names),File.ReadLines(request.Messages));var index=new ConnectedSpellIndex([]).WithClientFile(request.Names,CancellationToken.None);var history=new ConnectedSpellState();var checkpoint=ConnectedSpellEvidence.InitialCheckpoint(request.Checkpoint);int count=0;
        do
        {
            var batch=LogTailer.Read(request.Log,checkpoint,1024*1024);ConnectedSpellEvidence.ReadMetadata(history,batch.Lines,profile.Character,profile.PetIdentities,index,messages);checkpoint=batch.Checkpoint;count+=batch.Lines.Count;if(batch.CaughtUp)break;
        }while(true);
        PetResearch.ReprojectBuffMetadata(research,history,profile);StandardPetBuffs.Learn(setup,history,index,profile);PetResearch.Collect(research,fights,profile);
        foreach(var sample in research.Samples.Values)if(sample.Components.Values.Sum()!=sample.Damage||sample.BuffPeriodDamage.Values.Sum()!=sample.Damage||Math.Abs(sample.BuffPeriodSeconds.Values.Sum()-sample.Seconds)>.0001)throw new InvalidDataException("Research arithmetic reconciliation failed");
        if(combatBefore!=PetResearch.Hash(fights.Select(f=>new{f.Id,f.Start,f.End,f.PlayerDamage,f.PetDamage,f.UnknownDamage,f.Events,f.Heals})))throw new InvalidDataException("Combat mutated during metadata refresh");
        Json.Write(Path.Combine(folder,"research-updated.json"),research);Json.Write(Path.Combine(folder,"standard-updated.json"),setup);Json.Write(Path.Combine(folder,"connected-updated.json"),history);Json.Write(Path.Combine(folder,"connected-checkpoint.json"),checkpoint);
        Json.Write(Path.Combine(folder,"refresh-report.json"),new{Passed=true,CombatUnchanged=true,LinesRead=count,History=history.History.Count,Sheets=research.Sheets.Count,LearnedBuffs=setup.Buffs.Count,Samples=research.Samples.Count});
        Console.WriteLine("Private copy metadata refresh passed; combat totals unchanged.");
    }
}
