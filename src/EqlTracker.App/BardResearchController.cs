using System.Globalization;
using System.Security.Cryptography;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed record BardSetChoice(string Id,string Label,string Tip=""){public override string ToString()=>Label;}
internal sealed record BardPresentationRequest(string Key,BardComparisonInput Input,IReadOnlyList<BardSetChoice> Choices,string Selected,string SetupSummary,string Evidence);
public sealed partial class TrackerController
{
    private BardResearchState bardResearch=new();
    private FileCheckpoint? bardCheckpoint;
    private bool bardInitialized;
    private DateTime nextBardRead,nextBardFiles;
    private string bardFileHash="",bardClientVersion="";
    private void InitializeBardResearch()
    {
        lock(Sync)
        {
            if(bardInitialized)return;var saved=store.Get<BardResearchState>(BardRotations.Version);
            if(saved==null&&store.Get<BardResearchState>("bard-rotations-v1") is {} previous)saved=new(){Preferences=EquipmentPositions.Copy(previous.Preferences),Modifiers=EquipmentPositions.Copy(previous.Modifiers),Plans=EquipmentPositions.Copy(previous.Plans),Saved=EquipmentPositions.Copy(previous.Saved),FileEvidence=previous.FileEvidence};
            bardResearch=saved??new();bardCheckpoint=store.Get<FileCheckpoint>(BardRotations.Version+":checkpoint");bardInitialized=true;
        }
    }
    internal BardResearchState BardResearchSnapshot(){lock(Sync){if(!Demo)InitializeBardResearch();return EquipmentPositions.Copy(bardResearch);}}
    private void PersistBardResearch()
    {
        var frozen=EquipmentPositions.Copy(bardResearch);var checkpoint=EquipmentPositions.Copy(bardCheckpoint);QueuePersistence(()=>store.SaveBardResearch(frozen,checkpoint));
    }
    internal void SelectBardSet(string id){lock(Sync){bardResearch.Preferences.SelectedSet=id;PersistBardResearch();}}
    internal void SetBardStrength(bool value){lock(Sync){bardResearch.Preferences.EffectiveStrength=value;PersistBardResearch();}}
    internal void SaveBardModifier(BardModifierCapture capture)
    {
        if(capture.Source.Trim().Length==0||capture.FinalMultipliers.Values.Any(v=>!double.IsFinite(v)||v is <0 or >100)||capture.WithAmplification is {} n&&(!double.IsFinite(n)||n is <0 or >100))throw new InvalidDataException("Use reviewed multipliers between 0 and 100 and an evidence source.");
        lock(Sync){capture.At=DateTime.Now;capture.Caster=Profile.Character;capture.Context=BardEquipmentContext();bardResearch.Modifiers.Add(EquipmentPositions.Copy(capture));PersistBardResearch();}
    }
    internal void SaveBardSwap(string setId,BardSong before,BardSong after)
    {
        lock(Sync)
        {
            if(!bardResearch.Known.ContainsKey(BardRotations.VersionKey(after.Identity))&&!integrated.Bard.Available.Any(s=>s.Available&&s.Identity==after.Identity))throw new InvalidDataException("Replacement availability changed; review it again.");
            bardResearch.Plans.RemoveAll(p=>p.Set==setId&&p.Before==BardRotations.Key(before.Identity));bardResearch.Plans.Add(new(setId,BardRotations.Key(before.Identity),after.Identity.Name,after.Identity.UpgradeRank,DateTime.Now,before.Gem>0?before.Gem:null));PersistBardResearch();
        }
    }
    private string BardEquipmentContext()=>PetComparisonEngine.Key(new{Profile.ActiveLoadout.Classes,Profile.ActiveLoadout.Level,Equipment=EquipmentPositions.Fingerprint(EquipmentPositions.For(Profile.ActiveLoadout,false)),AAs=statEvidence.AAs.Values.OrderBy(a=>a.Name).Select(a=>new{a.Name,a.Rank})});
    private void ReadBardResearch()
    {
        if(Demo||cancel.IsCancellationRequested||DateTime.UtcNow<nextBardRead||!File.Exists(GameLogPath))return;
        InitializeBardResearch();var index=SpellIndexSnapshot();bool filesChanged=false;
        if(!BardRotations.MetadataReady(index)){nextBardRead=DateTime.UtcNow.AddMilliseconds(500);return;}
        if(DateTime.UtcNow>=nextBardFiles||bardClientVersion!=index.EvidenceVersion)
        {
            nextBardFiles=DateTime.UtcNow.AddSeconds(5);bardClientVersion=index.EvidenceVersion;string pattern=Profile.Character+"_"+Profile.Server+"*.ini";
            var files=Directory.Exists(Profile.GameFolder)?Directory.GetFiles(Profile.GameFolder,pattern,SearchOption.TopDirectoryOnly):[];
            if(files.Length==1)
            {
                var info=new FileInfo(files[0]);if(info.Length<=2*1024*1024)
                {
                    byte[] raw=File.ReadAllBytes(files[0]);string hash=Convert.ToHexStringLower(SHA256.HashData(raw))+index.EvidenceVersion;
                    if(hash!=bardFileHash){var saved=BardRotations.SavedLoadouts(System.Text.Encoding.UTF8.GetString(raw),index,"Read-only character spell sets · SHA-256 "+Convert.ToHexStringLower(SHA256.HashData(raw)));lock(Sync){bardResearch.Saved=saved;bardResearch.FileEvidence="Saved EQ sets read; none is assumed active";bardFileHash=hash;filesChanged=true;}}
                }
            }
            else lock(Sync)bardResearch.FileEvidence=files.Length==0?"Character spell-set file unavailable":"Multiple character spell-set files; active source needs review";
        }
        var batch=LogTailer.Read(GameLogPath,bardCheckpoint,1024*1024);if(batch.Lines.Count==0&&!batch.Reset&&!filesChanged&&bardCheckpoint?.Offset==batch.Checkpoint.Offset){nextBardRead=DateTime.UtcNow.AddMilliseconds(1500);return;}var parser=new LogParser(Profile.Character);var scratch=new StatEvidenceState();
        BardResearchState working;string character=Profile.Character,server=Profile.Server;lock(Sync)working=EquipmentPositions.Copy(bardResearch);
        if(batch.Reset){working.Pending.Clear();working.Correlation=new();}
        {
            foreach(var line in batch.Lines)
            {
                cancel.Token.ThrowIfCancellationRequested();var parsed=parser.Parse(line.Text,line.Id);if(parsed==null)continue;
                string message=line.Text[(line.Text.IndexOf("] ",StringComparison.Ordinal)+2)..];string id=BardRotations.EvidenceId(parsed.Time,line.Id,message);
                string session=Sessions?.Sessions.FirstOrDefault(s=>parsed.Time>=s.Start&&(!s.NextLogin.HasValue||parsed.Time<s.NextLogin))?.Id??parsed.Time.Date.ToString("O");
                if(parsed.Boundary){BardRotations.Boundary(working,session+":"+id);working.Correlation.History.Clear();working.Correlation.KnownIds=null;}
                else if(working.Pending.LastOrDefault() is {} previous&&previous.Session!=session)BardRotations.Boundary(working,session);
                BardRotations.Memorized(working,message,parsed.Time,index);
                if(parsed.CasterSpell!=null||parsed.PlayerCast!=null||parsed.ActivitySpell!=null||parsed.CastFailed||parsed.FailedCaster!=null)
                {
                    ConnectedSpellEvidence.Attempt(working.Correlation,parsed,id,Profile.Character,Profile.PetIdentities,index);
                    var attempt=working.Correlation.History.FirstOrDefault(e=>e.Id==id+":spell-attempt");
                    if(attempt!=null&&BardRotations.Song(index,index.Resolve(attempt.Canonical))!=null)attempt.Rank=BardRotations.ObservedIdentity(index,attempt.Raw).Rank;
                }
                StatEvidenceReader.Observe(scratch,line.Text,id,Profile.Character,Profile.PetIdentities,statMessages,effect=>
                {
                    ConnectedSpellEvidence.Effect(working.Correlation,effect,index);
                    var observed=working.Correlation.History.FirstOrDefault(e=>e.Id==id+":spell-effect");
                    if(observed?.Transition!="Landing"||!observed.Caster.Equals(Profile.Character,StringComparison.OrdinalIgnoreCase)||!observed.Recipient.Equals(Profile.Character,StringComparison.OrdinalIgnoreCase))return;
                    var song=BardRotations.Song(index,index.Resolve(observed.Canonical) with{Rank=observed.Rank});if(song==null)return;
                    double? seconds=SongDuration(song,index);
                    BardRotations.Observe(working,new(id,observed.At,song,session,seconds));
                });
            }
            lock(Sync){if(Profile.Character!=character||Profile.Server!=server)return;working.Preferences=bardResearch.Preferences;working.Modifiers=bardResearch.Modifiers;working.Plans=bardResearch.Plans;bardResearch=working;bardCheckpoint=EquipmentPositions.Copy(batch.Checkpoint);PersistBardResearch();}
        }
        nextBardRead=DateTime.UtcNow.AddMilliseconds(batch.CaughtUp?1500:0);
    }
    private static double? SongDuration(BardSong song,ConnectedSpellIndex index)
    {
        // Only a literal reference duration is used. Unknown formulas/ticks do not become uptime.
        string? text=index.Resolve(song.Identity.Name).Definition?.Duration;
        if(text!=null&&System.Text.RegularExpressions.Regex.Match(text,@"^(?<n>\d+(?:\.\d+)?)\s*(?:sec|seconds?)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase) is {Success:true} match&&double.TryParse(match.Groups["n"].Value,CultureInfo.InvariantCulture,out double n))return n;
        return null;
    }
    internal BardPresentationRequest BardPresentation()
    {
        lock(Sync)
        {
            if(!Demo)InitializeBardResearch();var state=new BardResearchState{Known=EquipmentPositions.Copy(bardResearch.Known),Common=EquipmentPositions.Copy(bardResearch.Common),Current=EquipmentPositions.Copy(bardResearch.Current),Saved=EquipmentPositions.Copy(bardResearch.Saved),Preferences=EquipmentPositions.Copy(bardResearch.Preferences),Modifiers=EquipmentPositions.Copy(bardResearch.Modifiers),FileEvidence=bardResearch.FileEvidence};var setup=EquipmentPositions.Copy(integrated.Bard);var index=SpellIndexSnapshot();
            bool live=LabBrowsing.FollowCurrent;var session=SelectedSession;DateTime at=live?DateTime.Now:session?.LastObserved??DateTime.MinValue;
            var period=GroupPeriods.FirstOrDefault(p=>p.Id==Profile.Meter.GroupPeriodId);if(period!=null){at=period.End??period.LastObserved;live=period.Live;}
            var conditions=new List<string>();
            var common=state.Common.Where(s=>s.First<=at).OrderByDescending(s=>s.ObservedSeconds).ThenByDescending(s=>s.Cycles).ToArray();
            var choices=new List<BardSetChoice>{new("detected","Detected")};choices.AddRange(common.Select(s=>new BardSetChoice(s.Id,"Common · "+string.Join(" / ",s.Songs.Take(2).Select(x=>x.Identity.Name+" +"+(x.Identity.UpgradeRank?.ToString()??"?")))+(s.Songs.Count>2?" +"+(s.Songs.Count-2):""),s.Cycles+" cycles · "+s.Episodes+" episodes · "+s.Sessions.Count+" sessions · "+(s.ObservedSeconds>0?s.ObservedSeconds.ToString("0.#")+" supported seconds":"duration unresolved")+" · "+s.Last.ToString("g"))));if(live)choices.AddRange(state.Saved.Select(s=>new BardSetChoice(s.Id,"EQ · "+s.Name,s.Evidence)));
            string selected=state.Preferences.SelectedSet;
            var set=selected=="detected"?live?state.Current:common.Where(s=>s.Last<=at).MaxBy(s=>s.Last):common.Concat(live?state.Saved:[]).FirstOrDefault(s=>s.Id==selected);
            if(set==null&&selected=="detected"&&setup.Songs.Count>0)set=new(){Id="reviewed",Name="Reviewed song set",Origin="Player setup · use unconfirmed",Songs=EquipmentPositions.Copy(setup.Songs),GemCount=setup.GemCount,Evidence="Explicit setup, not a detected active rotation"};
            if(set==null)conditions.Add(state.FileEvidence);
            if(set==null&&selected!="detected")conditions.Add("Pinned set is unavailable in this session; choose Detected or another set.");
            if(set!=null&&selected=="detected"&&set.Origin=="Repeated observations")conditions.Add("Detected as of "+set.Last.ToString("g")+"; this is not a complete live buff roster.");
            var known=state.Known.Values.Where(k=>k.First<=at).Select(k=>k.Song).Concat(live?setup.Available.Where(s=>s.Available):[]).DistinctBy(s=>BardRotations.VersionKey(s.Identity)).ToArray();
            var songs=known.Concat(set?.Songs??[]).Concat(setup.Songs).DistinctBy(s=>BardRotations.VersionKey(s.Identity)).ToArray();
            var facts=songs.ToDictionary(BardSongComparison.FactKey,s=>BardSongComparison.Fact(s,index,live?setup.Songs.Concat(setup.Available):[]));
            string equipmentContext=BardEquipmentContext();var modifier=state.Modifiers.Where(m=>m.Caster==Profile.Character&&m.At<=at&&(live?m.Context==equipmentContext:true)).MaxBy(m=>m.At);
            if(!live)conditions.Add("Historical caster modifiers require a capture from that context; Current equipment is not substituted.");
            var aas=statEvidence.AAs.Values.Where(a=>a.Name is "Singing Mastery" or "Instrument Mastery" or "Jam Fest" or "Symphonic Aura").ToArray();
            if(live&&setup.ConfirmedAuraRank==null&&aas.FirstOrDefault(a=>a.Name=="Symphonic Aura")?.Rank is >=1 and <=5)setup.ConfirmedAuraRank=aas.First(a=>a.Name=="Symphonic Aura").Rank;
            var group=Profile.GroupMembers.Where(m=>!m.IsPet&&m.Contains(m.Name,at)).Select(m=>m.Name).Append(Profile.Character).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var context=new EvaluationContext{Character=Profile.Character,Recipient=Profile.Character,Server=Profile.Server,Classes=live?[..Profile.CurrentClasses]:SessionEvidence.ClassesAt(session,at).Split('/').Where(s=>s.Length>0).ToList(),CasterLevel=live&&Profile.ActiveLoadout.Level>0?Profile.ActiveLoadout.Level:null,Live=live,Fingerprint=live?equipmentContext:session?.Id??""};
            // An old capture alone cannot establish the gear/AA state throughout a historical session.
            if(!live)modifier=null;
            var input=new BardComparisonInput(set,setup,known,facts,EquipmentPositions.Copy(integrated.Coverage),group,context,EquipmentPositions.Copy(integrated.Rules),modifier,state.Preferences.EffectiveStrength,at,conditions);
            if(live&&setup.EffectiveCasterLevel==null&&Profile.ActiveLoadout.Level>0&&aas.FirstOrDefault(a=>a.Name=="Jam Fest")?.Rank is >=0 and <=3){int rank=aas.First(a=>a.Name=="Jam Fest").Rank!.Value;setup.EffectiveCasterLevel=Profile.ActiveLoadout.Level+(rank switch{1=>1,2=>3,3=>5,_=>0});}
            string summary=string.Join(" · ",aas.Select(a=>a.Name+" "+(a.Rank?.ToString()??"Unknown")));
            var equipped=EquipmentPositions.For(Profile.ActiveLoadout,false).Where(p=>p.Item!=null).SelectMany(p=>{var item=(EquipmentView?.Catalog??Catalog).FindKey(p.Item!.ConfirmedItemKey);return (item?.Effects??[]).Concat(PetEffectPolicy.SelectedTexts(p.Item)).Where(e=>new[]{"singing","brass","string","percussion","wind","instrument"}.Any(t=>e.Contains(t,StringComparison.OrdinalIgnoreCase))).Select(e=>(item?.Name??p.Item!.Name)+": "+e);}).Distinct().ToArray();
            summary+=(summary.Length>0?"\n":"")+string.Join("\n",equipped);summary+=(summary.Length>0?"\n":"")+"Documented AA influence: Singing/Instrument Mastery 20/40/60%; Jam Fest +1/+3/+5 effective levels. Combined potency is not assumed from these descriptions.";
            string evidence=string.Join("\n",common.Select(s=>$"{s.Name}\n{s.Cycles} cycles · {s.Episodes} episodes · {s.Sessions.Count} sessions · {s.ObservedSeconds:0.#} supported seconds\n{s.First:g} → {s.Last:g}\n{s.Evidence}"));
            string key=BardSongComparison.Version+PetComparisonEngine.Key(new{selected,state.Preferences.EffectiveStrength,Set=set==null?null:new{set.Id,set.Songs,set.Origin},known,facts,setup,Modifier=modifier,Context=context,Coverage=input.Coverage.Where(c=>c.Fresh(at,Profile.Character)).ToArray(),Group=group.Order().ToArray(),input.Rules});
            return new(key,input,choices,selected,summary,evidence);
        }
    }
}
