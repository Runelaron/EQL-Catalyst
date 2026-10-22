using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EqlTracker.Core;

public enum PetEvidenceKind { Unresolved, Documented, Computed, Observed, Assumed }
public enum PetEquipmentMode { IntendedSlots, Reassign, Inventory, BestPossible }
public enum PetClassAccess { Unverified, PetClasses, OwnerClasses, Both, Incompatible }
public enum PetCastingPolicy { CurrentGameBehavior }
public sealed record PetMechanicValue(double? Value,PetEvidenceKind Evidence,string Source,string Explanation);
public sealed record PetDefinition(string Type,string SecondClass,string Abilities,bool Backstab=false)
{
    public const string Version="EQL-2026-10-13/pet-comparison-v3/"+RankedItemStats.Version;
    public string Source { get; init; }="https://eqlwiki.com/Pet_Guide";
    public string[] Classes=>SecondClass.Length==0?[]:["WAR",SecondClass];
    public IReadOnlyDictionary<string,PetMechanicValue> BaseStats { get; init; }=new Dictionary<string,PetMechanicValue>{["HP"]=new(null,PetEvidenceKind.Unresolved,"","No verified pet/level/rank baseline"),["AC"]=new(null,PetEvidenceKind.Unresolved,"","No verified pet/level/rank baseline"),["Damage"]=new(null,PetEvidenceKind.Unresolved,"","Weapon ratio emphasis does not publish innate damage"),["Delay"]=new(null,PetEvidenceKind.Unresolved,"","Innate cadence and casting interference unverified")};
    public static readonly PetDefinition[] All=[new("Air","MNK","Monk special attacks; cadence and damage response unresolved"),new("Earth","RNG","Ranger spells and melee; casting frequency and melee interference unresolved"),new("Fire","WIZ","Wizard spells and melee; spell selection and cadence unresolved"),new("Water","ROG","Rogue attacks; backstab requires position",true),new("Undead","SHD","Shadow Knight lifetaps and abilities; activation/cadence unresolved"),new("Warder","BST","Beastlord abilities; spell cadence unresolved"),new("Spirit","BST","Beastlord abilities; spell cadence unresolved"),new("Animation","PAL","Paladin utility/healing; cadence unresolved")];
    public static PetDefinition For(string type)=>All.FirstOrDefault(p=>p.Type==type)??new(type,"","Pet kit unverified");
}
public sealed class PetSummonSetup
{
    public string SpellId { get; set; }="";
    public int Rank { get; set; }
    public bool LearningConfirmed { get; set; }
    public bool RankConfirmed { get; set; }
    public string Focus { get; set; }="";
}
public sealed class PetComparisonPreferences
{
    public List<string> Expansions { get; set; } = ["Classic"];
    public List<PetBenchmark> Benchmarks { get; set; } = [];
    public string Goal { get; set; }="DPS";
    public Dictionary<string,string> SelectedSummons { get; set; }=[];
    public Dictionary<string,PetSummonSetup> Summons { get; set; }=[];
    public List<PetBuffChoice> Buffs { get; set; }=[];
    public PetEncounterAssumptions Encounter { get; set; }=new();
    public bool ExternalBuffs { get; set; }
}
public sealed class PetEncounterAssumptions
{
    public double? Seconds { get; set; }
    public double? TargetDefense { get; set; }
    public double? TargetResist { get; set; }
    public double? IncomingDps { get; set; }
    public string Target { get; set; }="";
    public string Explanation=>"Duration "+(Seconds?.ToString("0.##")??"Unknown")+"s · target defense "+(TargetDefense?.ToString("0.##")??"Unknown")+" · resist "+(TargetResist?.ToString("0.##")??"Unknown")+" · incoming DPS "+(IncomingDps?.ToString("0.##")??"Unknown");
    public void Validate(){foreach(double? v in new[]{Seconds,TargetDefense,TargetResist,IncomingDps})if(v.HasValue&&(!double.IsFinite(v.Value)||v.Value<0))throw new ArgumentException("Encounter inputs must be finite and nonnegative, or blank for Unknown.");if(Seconds==0)throw new ArgumentException("Duration must be positive, or blank for Unknown.");}
}
public sealed class PetComparisonScenario
{
    public PetCastingPolicy CastingPolicy { get; set; }=PetCastingPolicy.CurrentGameBehavior;
    public string MechanicsVersion { get; set; }=PetDefinition.Version;
    public string Goal { get; set; }="DPS";
    public PetEquipmentMode EquipmentMode { get; set; }
    public PetSummonSetup Summon { get; set; }=new();
    public PetEncounterAssumptions Encounter { get; set; }=new();
    public List<PetBuffChoice> Buffs { get; set; }=[];
    public bool ExternalBuffs { get; set; }
    public string InputKey { get; set; }="";
    public string OwnerMechanicsContext { get; set; }="";
}
public sealed record PetItemFit(string Id,string Item,string Slot,PetClassAccess Access,string Reason,bool Equipped);
public sealed record PetMeasuredSample(string Pet,string Targets,string Zone,int Fights,double Seconds,long Damage,double? Melee,double? Specials,double? Spells,string Conditions)
{ public double Dps=>Damage/Math.Max(1,Seconds); }
/// <summary>Only a complete, versioned model for the exact context may provide a prediction.</summary>
public sealed record PetSupportedOutcome(string ContextKey,string MechanicsVersion,double MeleeDps,double SpecialDps,double SpellDps,double ProcDps,double? SurvivalSeconds,double? BehindExtraDps,PetEvidenceKind Evidence,string Source,bool Complete)
{ public double Dps=>MeleeDps+SpecialDps+SpellDps+ProcDps; }
public sealed class PetComparisonResult
{
    public required PetDefinition Definition { get; init; }
    public required PetSummonOption Spell { get; init; }
    public required PetBuild Plan { get; init; }
    public required PetComparisonScenario Scenario { get; init; }
    public required EffectivePetEquipment Equipment { get; init; }
    public List<PetItemFit> Fits { get; init; }=[];
    public List<string> Missing { get; init; }=[];
    public List<PetBuffAssessment> Buffs { get; init; }=[];
    public List<PetMeasuredSample> Observed { get; init; }=[];
    public PetSupportedOutcome? Outcome { get; init; }
    public double DamagePreference { get; init; }
    public double DefensePreference { get; init; }
    public List<string> EquipmentRequirements { get; set; } = [];
    public bool AssignmentSearchComplete { get; set; } = true;
    public List<PetBuild> EquipmentTradeoffs { get; set; } = [];
    public PetBenchmark? Benchmark { get; set; }
    public double CurrentDamagePreference { get; set; }
    public double CurrentDefensePreference { get; set; }
    public double CurrentGearHp { get; set; }
    public double GearHp => Equipment.Totals.Stats.GetValueOrDefault("HP");
    public double DamagePreferenceDelta => DamagePreference-CurrentDamagePreference;
    public double DefensePreferenceDelta => DefensePreference-CurrentDefensePreference;
    public double GearHpDelta => GearHp-CurrentGearHp;
    public bool PartialEquipment => Fits.Any(f=>f.Reason!="Eligible") || Equipment.Totals.Notes.Count>0;
    public double? CurrentDps { get; set; }
    public double? CurrentHp { get; set; }
    public double? DpsDelta => DisplayDps.HasValue && CurrentDps.HasValue ? DisplayDps - CurrentDps : null;
    public double? HpDelta => Hp.HasValue && CurrentHp.HasValue ? Hp - CurrentHp : null;
    public double? Hp => Benchmark?.Hp ?? Definition.BaseStats.GetValueOrDefault("HP")?.Value;
    public double? DisplayDps => Outcome?.Dps ?? Benchmark?.Dps;
    public bool StrongTradeoff { get; set; }
    public string Pet=>Definition.Type;
    public string Summon=>Spell.Name+" +"+Plan.SummonRank;
    public string Level=>Plan.PetLevel?.ToString()??"Unknown";
    public string PredictedDps=>Outcome?.Dps.ToString("N1")??"Unavailable";
    public string Survival=>Outcome?.SurvivalSeconds?.ToString("0.0")+ (Outcome?.SurvivalSeconds.HasValue==true?"s":"Unavailable");
    public string Guidance=>$"Damage {DamagePreference:0.0} · Defense {DefensePreference:0.0} pts";
    public string Coverage=>Outcome!=null?"Supported model · "+Outcome.Evidence:"Incomplete · no predicted winner";
    public string FromBehind=>!Definition.Backstab?"Not applicable":Outcome?.BehindExtraDps is {} v?$"+{v:N1} DPS from behind":"Additional backstab damage unavailable; positioning not credited";
    public override string ToString()=>Definition.Type+"|"+Spell.Id+"|"+EquipmentPositions.Fingerprint(Equipment.Positions);
}
public static class PetComparisonEngine
{
    public static string Key(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static IReadOnlyList<PetSummonOption> Eligible(PetContextCatalog catalog,LoadoutProfile owner)=>owner.Level<1?[]:catalog.Spells.Where(s=>s.RequiredLevel<=owner.Level&&owner.Classes.Select(CastingPriority.Code).Contains(s.CasterClass,StringComparer.OrdinalIgnoreCase)).OrderByDescending(s=>s.RequiredLevel).ThenBy(s=>s.Id,StringComparer.Ordinal).ToList();
    public static PetClassAccess Access(ItemDefinition? item,LoadoutProfile owner,string type)
    {
        if(item?.MetadataComplete!=true || item.Classes.Count==0 || PetDefinition.For(type).Classes.Length==0)return PetClassAccess.Unverified;
        bool all=item.Classes.Contains("ALL"),pet=all||item.Classes.Intersect(PetDefinition.For(type).Classes,StringComparer.OrdinalIgnoreCase).Any(),player=all||item.Classes.Intersect(owner.Classes.Select(CastingPriority.Code),StringComparer.OrdinalIgnoreCase).Any();
        return pet&&player?PetClassAccess.Both:pet?PetClassAccess.PetClasses:player?PetClassAccess.OwnerClasses:PetClassAccess.Incompatible;
    }
    public static string AccessText(PetClassAccess access)=>access switch{PetClassAccess.PetClasses=>"Pet classes",PetClassAccess.OwnerClasses=>"Owner classes",PetClassAccess.Both=>"Both",PetClassAccess.Incompatible=>"Incompatible",_=>"Unverified"};
    public static List<PetComparisonResult> Evaluate(CharacterProfile profile,PetBuild source,Catalog catalog,PetContextCatalog summons,PetBuffCatalog buffs,IReadOnlyList<PetMeasuredSample> observed,PetEquipmentMode mode,CancellationToken token, IReadOnlyList<PetSupportedOutcome>? supported=null, InventorySnapshot? inventory=null)
    {
        var preferences=profile.PetComparison;preferences.Encounter.Validate();var owner=profile.ActiveLoadout; source=PetEquipmentProjection.Prepare(source,owner,catalog); var eligible=Eligible(summons,owner);var results=new List<PetComparisonResult>();
        var baseline=PetEquipmentInference.Build(source,owner,catalog,false);
        var baselineStats=PetReassignment.Stats(baseline,catalog);
        double currentDamage=GearScorer.Score(baselineStats,PetRoleRatings.Weights(profile,"DPS")),currentDefense=GearScorer.Score(baselineStats,PetRoleRatings.Weights(profile,"Tank"));
        foreach(var family in eligible.GroupBy(s=>s.PetType))
        {
            token.ThrowIfCancellationRequested();var recorded=summons.Find(family.Key,source.SummonSpell,source.SummonClass);var spell=family.FirstOrDefault(s=>s.Id==preferences.SelectedSummons.GetValueOrDefault(family.Key))??family.FirstOrDefault(s=>s.Id==recorded?.Id)??family.First();
            var setup=preferences.Summons.TryGetValue(spell.Id,out var saved)?EquipmentPositions.Copy(saved):new PetSummonSetup{SpellId=spell.Id,Rank=recorded?.Id==spell.Id&&source.PetType==spell.PetType?source.SummonRank:0,Focus=source.PetType==spell.PetType?source.Focus:""};
            if(setup.Rank is <0 or >10) { if(setup.Rank!=source.SummonRank)throw new ArgumentException("New summon previews use ranks 0–10."); }
            var plan=EquipmentPositions.Copy(source);plan.ComparisonScenario=null;plan.Undo=[];plan.EditHistory=[];plan.PetType=spell.PetType;plan.SummonSpell=spell.Name;plan.SummonClass=spell.CasterClass;plan.SummonRank=setup.Rank;plan.Focus=setup.Focus=="None"?"":setup.Focus;plan.Preset=preferences.Goal;
            var level=summons.Estimate(plan,owner.Level);plan.PetLevel=level.Level;plan.LevelMode=PetLevelMode.Automatic;plan.LevelEvidence=level.Explanation;
            if(source.PetLevel is >0 && plan.PetLevel.HasValue) { plan.PetLevel=Math.Min(source.PetLevel.Value,plan.PetLevel.Value); if(plan.PetLevel!=source.PetLevel)plan.LevelEvidence+=" Below shared level "+source.PetLevel+"; showing attainable summon level."; }
            if(mode==PetEquipmentMode.Reassign)
            {
                // Balanced returns actual equipment tradeoffs rather than an invented scalar.
                foreach(var assignment in PetReassignment.Solve(plan,owner,catalog,PetRoleRatings.Weights(profile,"DPS"),PetRoleRatings.Weights(profile,"Tank"),preferences.Goal,token)) Add(assignment);
            }
            else if (mode is PetEquipmentMode.Inventory or PetEquipmentMode.BestPossible)
            {
                var pool=PetEquipmentOptions.Pool(plan,owner,catalog,inventory??new(),mode,preferences.Expansions);
                var optimized=PetEquipmentOptions.Optimize(plan,owner,catalog,pool,profile,token);
                Add(optimized.Plan,optimized);
            }
            else Add(plan);
            void Add(PetBuild assigned, PetOptimization? optimization=null)
            {
                token.ThrowIfCancellationRequested();var equipment=PetEquipmentInference.Build(assigned,owner,catalog,false);var context=GuidedPetBuilds.Context(owner,assigned);var fits=new List<PetItemFit>();
                foreach(var entry in assigned.Items)
                {
                    var item=catalog.FindKey(entry.ItemKey)??catalog.Find(entry.Name);var access=Access(item,owner,spell.PetType);bool equipped=equipment.Selections.Any(s=>s.EntryId==entry.Id);string why=entry.Position>PetEquipmentPlanner.Capacity(owner.Classes)?"Outside storage capacity":item==null?"Metadata missing":GearScorer.Eligibility(item,context,true,entry.Entry().Augments,catalog);
                    if(why=="Eligible"&&!equipped)why=EquipmentPositions.IsSlot(entry.Slot)?"Intended slot blocked or incompatible; review slot/effect constraints":"Stored · no intended slot; not silently equipped";
                    fits.Add(new(entry.Id,entry.Name+" +"+entry.Rank,entry.Slot,access,why,equipped));
                }
                var scenario=new PetComparisonScenario{Goal=preferences.Goal,EquipmentMode=mode,Summon=setup,Encounter=EquipmentPositions.Copy(preferences.Encounter),Buffs=EquipmentPositions.Copy(preferences.Buffs),ExternalBuffs=preferences.ExternalBuffs};
                scenario.OwnerMechanicsContext=Key(new{owner.Classes,owner.Level,OwnerEquipment=EquipmentPositions.Fingerprint(EquipmentPositions.For(owner,false)),PetAAs=owner.PetStatProfiles.Select(p=>p.AAs)});
                scenario.InputKey=Key(new{Mechanics=PetDefinition.Version,Summons=summons.Version,Buffs=buffs.Version,owner.Classes,owner.Level,OwnerEquipment=EquipmentPositions.Fingerprint(EquipmentPositions.For(owner,false)),PetAAs=owner.PetStatProfiles.Select(p=>p.AAs),assigned,scenario,profile.PetGoalWeights,DpsWeights=PetRoleRatings.Weights(profile,"DPS")});
                var outcome=supported?.FirstOrDefault(s=>s.ContextKey==scenario.InputKey&&s.MechanicsVersion==PetDefinition.Version&&s.Complete&&s.Evidence is PetEvidenceKind.Documented or PetEvidenceKind.Computed&&s.Source.Length>0&&new[]{s.MeleeDps,s.SpecialDps,s.SpellDps,s.ProcDps}.All(v=>double.IsFinite(v)&&v>=0)&&(!s.SurvivalSeconds.HasValue||double.IsFinite(s.SurvivalSeconds.Value)&&s.SurvivalSeconds>0)&&(!s.BehindExtraDps.HasValue||double.IsFinite(s.BehindExtraDps.Value)&&s.BehindExtraDps>=0));
                var assessments=PetBuffResolver.Resolve(buffs,scenario.Buffs,owner,spell.PetType,scenario.ExternalBuffs);
                var missing=new List<string>();missing.AddRange(equipment.Totals.Notes.Where(n=>n.StartsWith("Rank estimate")));if(!setup.LearningConfirmed)missing.Add("Summon learning unverified");if(!setup.RankConfirmed)missing.Add("Summon rank unverified; scenario +"+setup.Rank);if(level.Level==null)missing.Add(level.Explanation);
                if(outcome==null)missing.Add("Innate pet damage/delay, skill/spell cadence, proc rates and target response are insufficient for predicted DPS or time-to-live.");
                if(assigned.Focus.Length>0)missing.Add(summons.FocusEvidence(assigned));
                if(assessments.Any(b=>b.State.Contains("Unresolved")||b.State.Contains("Unverified")))missing.Add("Some buff effects or uptime remain unresolved; no numerical bonus inferred.");
                if(fits.Any(f=>f.Reason!="Eligible"))missing.Add("Some inventory items are not part of this pet's effective equipment.");
                var stats=PetReassignment.Stats(equipment,catalog);assigned.ComparisonScenario=scenario;
                var row=new PetComparisonResult{Definition=PetDefinition.For(spell.PetType),Spell=spell,Plan=assigned,Scenario=scenario,Equipment=equipment,Fits=fits,Missing=missing,Buffs=assessments,Observed=observed.Where(s=>s.Pet==spell.PetType).ToList(),Outcome=outcome,CurrentDamagePreference=currentDamage,CurrentDefensePreference=currentDefense,CurrentGearHp=baseline.Totals.Stats.GetValueOrDefault("HP"),DamagePreference=GearScorer.Score(stats,PetRoleRatings.Weights(profile,"DPS")),DefensePreference=GearScorer.Score(stats,PetRoleRatings.Weights(profile,"Tank")),EquipmentRequirements=optimization?.Requirements.ToList()??[],AssignmentSearchComplete=optimization?.SearchComplete??true,EquipmentTradeoffs=optimization?.Alternatives??[]}; row.Benchmark=PetBenchmarks.Matching(row,preferences.Benchmarks);results.Add(row);
            }
        }
        foreach(var row in results) row.StrongTradeoff=row.Outcome?.SurvivalSeconds is {} defense&&!results.Any(other=>other!=row&&other.Outcome?.SurvivalSeconds is {} d&&PetRoleRatings.Dominates(other.Outcome.Dps,d,row.Outcome.Dps,defense));
        return results.OrderBy(r=>r.Outcome==null?1:0).ThenByDescending(r=>preferences.Goal=="Tank"?r.Outcome?.SurvivalSeconds:preferences.Goal=="Balanced"?r.StrongTradeoff?1:0:r.Outcome?.Dps).ThenBy(r=>r.Pet,StringComparer.Ordinal).ToList();
    }
    public static PetBuild SavePlan(PetComparisonResult result,string name)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Enter a Pet plan name.");var plan=EquipmentPositions.Copy(result.Plan);plan.Id=Guid.NewGuid().ToString("N");plan.Name=name.Trim();plan.EditHistory=[];plan.Undo=[];plan.StatProfiles=[];
        plan.IsTheorycraft|=result.Scenario.EquipmentMode==PetEquipmentMode.BestPossible||!result.Scenario.Summon.LearningConfirmed||!result.Scenario.Summon.RankConfirmed||result.Scenario.Summon.Rank>10||plan.Focus.Length>0||result.Fits.Any(f=>f.Slot!="Storage"&&(f.Access is PetClassAccess.Incompatible or PetClassAccess.Unverified||f.Reason!="Eligible"))||result.Scenario.Buffs.Any(b=>b.Enabled&&(!b.OwnershipConfirmed||b.Rank==null||b.External))||result.Scenario.Buffs.Any(b=>b.Enabled)&&result.Buffs.Any(b=>b.State.Contains("Unresolved")||b.State.Contains("Unverified")||b.State.StartsWith("Excluded"));
        plan.TheorycraftRulesVersion=PetDefinition.Version;plan.TheorycraftAssumptions=result.Missing.ToList();foreach(var item in plan.Items)item.EquipmentVerified=false;return plan;
    }
    public static List<PetMeasuredSample> History(IEnumerable<Encounter> fights)
    {
        return fights.Where(f=>f.Finished&&f.PetType.Length>0&&f.PetDamage>0).GroupBy(f=>new{f.PetType,f.Zone,f.Weapons,f.BuffNotes}).Select(g=>
        {
            var events=g.Where(f=>f.DetailAvailable).SelectMany(f=>f.Events).Where(e=>!e.ActivityOnly&&e.Owner=="Pet").ToList();bool complete=g.All(f=>f.DetailAvailable)&&events.Sum(e=>e.Damage)==g.Sum(f=>f.PetDamage);
            return new PetMeasuredSample(g.Key.PetType,string.Join(", ",events.Select(e=>e.Target).Distinct().Take(8)),g.Key.Zone,g.Count(),g.Sum(f=>f.Duration),g.Sum(f=>f.PetDamage),complete?events.Where(e=>e.Kind=="Melee").Sum(e=>(double)e.Damage):null,null,complete?events.Where(e=>e.Kind!="Melee").Sum(e=>(double)e.Damage):null,"Recorded weapons: "+g.Key.Weapons+" · buffs: "+g.Key.BuffNotes+" · exact gear/ranks/active buff coverage unverified; historical observation, not current-build prediction");
        }).ToList();
    }
}

public static class PetReassignment
{
    public static Dictionary<string,double> Stats(EffectivePetEquipment equipment,Catalog catalog)
    {
        var stats=new Dictionary<string,double>(equipment.Totals.Stats);double ratio=0;foreach(var p in equipment.Positions.Where(p=>p.Item!=null&&p.Slot is "Primary" or "Secondary")){var d=catalog.FindKey(p.Item!.ConfirmedItemKey);if(d!=null)ratio+=GearScorer.EffectiveStats(d,p.Item.Rank).GetValueOrDefault("Ratio");}stats["Ratio"]=ratio;return stats;
    }
    /// <summary>Exact finite search over the supplied physical copies. Never returns an interrupted optimum.</summary>
    public static List<PetBuild> Solve(PetBuild source,LoadoutProfile owner,Catalog catalog,Dictionary<string,double> offense,Dictionary<string,double> defense,string goal,CancellationToken token)
    {
        var items=source.Items.Where(i=>i.Position>0&&i.Position<=PetEquipmentPlanner.Capacity(owner.Classes)).OrderBy(i=>i.Id,StringComparer.Ordinal).ToList();var context=GuidedPetBuilds.Context(owner,source);
        var slots=new List<EquipmentPosition>();EquipmentPositions.Complete(slots);slots.RemoveAll(p=>p.Slot=="Any Slot");var options=items.Select(i=>catalog.FindKey(i.ItemKey)??catalog.Find(i.Name)).Select(d=>d==null||GearScorer.Eligibility(d,context,true,null,catalog)!="Eligible"?[]:slots.Where(p=>EquipmentPositions.Fits(d,p,true)).Select(p=>p.Id).ToArray()).ToArray();
        var frontier=new List<(PetBuild Plan,double Offense,double Defense)>();var chosen=new string?[items.Count];var occupied=new HashSet<string>();long visited=0;
        // Sum-of-items is a safe optimistic bound for nonnegative priorities (haste
        // actually takes the strongest item). Disable pruning for custom negatives.
        bool bound=offense.Values.Concat(defense.Values).All(w=>w>=0);
        double[] aMax=new double[items.Count+1],bMax=new double[items.Count+1];
        for(int n=items.Count-1;n>=0;n--){var d=catalog.FindKey(items[n].ItemKey)??catalog.Find(items[n].Name);var stats=d==null?new Dictionary<string,double>():GearScorer.EffectiveStats(d,items[n].Rank);aMax[n]=aMax[n+1]+Math.Max(0,GearScorer.Score(stats,offense));bMax[n]=bMax[n+1]+Math.Max(0,GearScorer.Score(stats,defense));}
        void Search(int index,double aBound,double bBound)
        {
            if((++visited&255)==0)token.ThrowIfCancellationRequested();
            if(bound&&frontier.Any(f=>goal=="Balanced"?f.Offense>=aBound+aMax[index]&&f.Defense>=bBound+bMax[index]:goal=="Tank"?f.Defense>bBound+bMax[index]:f.Offense>aBound+aMax[index]))return;
            if(index==items.Count)
            {
                var plan=EquipmentPositions.Copy(source);foreach(var i in plan.Items)i.Slot="Storage";for(int n=0;n<items.Count;n++)if(chosen[n]!=null)plan.Items.Single(i=>i.Id==items[n].Id).Slot=slots.Single(p=>p.Id==chosen[n]).Slot;
                var equipment=PetEquipmentInference.Build(plan,owner,catalog,false);if(equipment.Selections.Count!=chosen.Count(s=>s!=null))return;
                var stats=Stats(equipment,catalog);double a=GearScorer.Score(stats,offense),b=GearScorer.Score(stats,defense);
                bool Beats(double x,double y,double u,double v)=>goal=="Balanced"?PetRoleRatings.Dominates(x,y,u,v):goal=="Tank"?y>v||y==v&&x>u:x>u||x==u&&y>v;
                if(frontier.Any(f=>goal=="Balanced"?f.Offense>=a&&f.Defense>=b:goal=="Tank"?f.Defense>b||f.Defense==b&&f.Offense>=a:f.Offense>a||f.Offense==a&&f.Defense>=b))return;
                frontier.RemoveAll(f=>Beats(a,b,f.Offense,f.Defense));frontier.Add((plan,a,b));return;
            }
            foreach(string slot in options[index])
            {
                if(occupied.Contains(slot))continue;var d=catalog.FindKey(items[index].ItemKey)??catalog.Find(items[index].Name);bool two=d?.TwoHanded==true;
                if(two&&slot.StartsWith("PRIMARY")&&occupied.Contains("SECONDARY:1"))continue;
                if(slot.StartsWith("SECONDARY")&&Enumerable.Range(0,index).Any(n=>chosen[n]=="PRIMARY:1"&&(catalog.FindKey(items[n].ItemKey)??catalog.Find(items[n].Name))?.TwoHanded==true))continue;
                // Equivalent paired slots need only one canonical permutation.
                var p=slots.Single(p=>p.Id==slot);if(p.Index==2&&!occupied.Contains(EquipmentPositions.Key(p.Slot,1)))continue;
                var stats=d==null?new Dictionary<string,double>():GearScorer.EffectiveStats(d,items[index].Rank);
                occupied.Add(slot);chosen[index]=slot;Search(index+1,aBound+Math.Max(0,GearScorer.Score(stats,offense)),bBound+Math.Max(0,GearScorer.Score(stats,defense)));occupied.Remove(slot);chosen[index]=null;
            }
            chosen[index]=null;Search(index+1,aBound,bBound);
        }
        token.ThrowIfCancellationRequested();Search(0,0,0);token.ThrowIfCancellationRequested();return frontier.OrderByDescending(f=>goal=="Tank"?f.Defense:f.Offense).ThenByDescending(f=>f.Defense).Select(f=>f.Plan).ToList();
    }
}
