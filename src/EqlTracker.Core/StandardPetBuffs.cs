using System.Globalization;

namespace EqlTracker.Core;

// Character-private presentation evidence. This is neither a live roster nor an equipment rating model.
internal sealed class StandardPetBuffState
{
    public string Version { get; set; }=StandardPetBuffs.Version;
    public long Revision { get; set; }
    public List<LearnedPetBuff> Buffs { get; set; }=[];
    public List<PairedPetBuffCapture> Pairs { get; set; }=[];
}
internal sealed class LearnedPetBuff
{
    public string Spell { get; set; }="";
    public int? Rank { get; set; }
    public DateTime At { get; set; }
    public string Caster { get; set; }="";
    public string Recipient { get; set; }="";
    public string Pet { get; set; }="";
    public string Evidence { get; set; }="";
    public bool Excluded { get; set; }
    public bool OverrideRank { get; set; }
    public int? SelectedRank { get; set; }
    internal int? EffectiveRank=>OverrideRank?SelectedRank:Rank;
}
internal sealed class PairedPetBuffCapture
{
    public string Baseline { get; set; }="";
    public string Buffed { get; set; }="";
    public string Spell { get; set; }="";
    public int? Rank { get; set; }
    public double? SpeedCeiling { get; set; }
    public string Evidence { get; set; }="";
}
internal sealed record PetBuffMagnitude(string Spell,int? Rank,IReadOnlyDictionary<string,double> Values,bool Estimated,string Evidence);
internal sealed record StandardPetBuffView(IReadOnlyDictionary<string,double?> Values,string Title,string Conditions,IReadOnlyList<string> Details,IReadOnlyDictionary<string,string> Hints);

internal static class StandardPetBuffs
{
    internal const string Version="standard-pet-buffs-v1-scoped-ranks";
    internal static bool Burst(string name)=>name.StartsWith("Frenzied Burnout",StringComparison.OrdinalIgnoreCase)||name.Contains("Frenzy",StringComparison.OrdinalIgnoreCase)||name.Contains("Fury",StringComparison.OrdinalIgnoreCase);
    internal static bool Learn(StandardPetBuffState state,ConnectedSpellState history,ConnectedSpellIndex index,CharacterProfile profile)
    {
        bool changed=false;
        foreach(var effect in history.History.Where(e=>e.Transition=="Landing"&&e.Caster.Equals(profile.Character,StringComparison.OrdinalIgnoreCase)&&!Burst(e.Canonical)).OrderBy(e=>e.At))
        {
            var pet=PetIdentityResolver.At(profile,effect.At,effect.Recipient);
            if(pet==null||!pet.Name.Equals(effect.Recipient,StringComparison.OrdinalIgnoreCase)||effect.Canonical=="Ambiguous effect")continue;
            var definition=index.Resolve(effect.Canonical).Definition;
            // Client target 14 is Pet. Group/song effects need the same explicit owned recipient.
            // Never include an instant heal, detrimental effect, unknown duration, or an AA burst.
            bool knownSustained=effect.Canonical is "Burnout III" or "Verses of Victory";
            if(!knownSustained&&(definition?.Beneficial!=true||definition.Instant!=false||definition.CombatSkill==true))continue;
            var prior=state.Buffs.FirstOrDefault(b=>b.Spell.Equals(effect.Canonical,StringComparison.OrdinalIgnoreCase));
            if(prior!=null&&effect.At<=prior.At)continue;
            if(prior==null){prior=new(){Spell=effect.Canonical};state.Buffs.Add(prior);}
            prior.Rank=effect.Rank;prior.At=effect.At;prior.Caster=effect.Caster;prior.Recipient=effect.Recipient;prior.Pet=pet.Element;
            prior.Evidence=effect.Id+" · "+effect.Source;changed=true;
        }
        if(changed)state.Revision++;return changed;
    }
    internal static PetBuffMagnitude Magnitude(string spell,int? rank,PetResearchState? research)
    {
        var values=new Dictionary<string,double>();bool modeled=rank is >=0 and <=10&&spell is "Burnout III" or "Verses of Victory";
        if(modeled)
        {
            var bases=spell=="Burnout III"?new Dictionary<string,int>{{"STR",50},{"AC",11},{"HastePercent",60}}:new Dictionary<string,int>{{"STR",30},{"AGI",30},{"AC",12},{"HastePercent",30}};
            foreach(var (stat,n) in bases)values[stat]=stat=="HastePercent"?n+rank!.Value:(double)decimal.Ceiling(n*(100m+2m*rank!.Value)/100m);
        }
        var exact=research?.Buffs.Where(b=>b.Transition=="Inspect"&&b.UpgradeRank==rank&&(b.SpellLine==spell||b.Spell==spell)).LastOrDefault();
        if(exact!=null)
        {
            bool incomplete=values.Keys.Any(stat=>!exact.Displayed.ContainsKey("Adjusted"+stat));
            foreach(string stat in new[]{"STR","AGI","AC","HastePercent"})if(exact.Displayed.TryGetValue("Adjusted"+stat,out double n))values[stat]=n;
            return new(spell,rank,values,incomplete,"Inspected effect · "+exact.Evidence+(incomplete?" · remaining fields use the local fitted rule":""));
        }
        if(!modeled)return new(spell,rank,values,false,"Effect magnitude unavailable for this spell/rank; no general scaling rule assumed.");
        return new(spell,rank,values,rank!=0,"Local fitted effect estimate: ceil(base × (1 + 0.02 × rank)); haste base + rank points. Burnout III / inspected Verses of Victory only. Caster modifiers and pet display conversions are separate. Conflicting reference: https://eqltools.com/learn/spell-upgrades");
    }
    internal static StandardPetBuffView Project(PetDetailsStatistics stats,PetBuild build,StandardPetBuffState setup,PetResearchState? research,bool enabled)
    {
        var values=stats.Observed.ToDictionary(p=>p.Key,p=>p.Value);var details=new List<string>();var hints=new Dictionary<string,string>();
        var selected=setup.Buffs.Where(b=>!b.Excluded&&!Burst(b.Spell)).ToArray();
        var pair=setup.Pairs.FirstOrDefault(p=>p.Baseline==stats.CaptureId);
        if(pair?.SpeedCeiling is {} ceiling&&values.GetValueOrDefault("AttackSpeedPercent") is {} speed)hints["AttackSpeedPercent"]=speed>=ceiling?CapHint(speed,ceiling,null,"observed ordinary buffs"):$"Observed cap {ceiling:0.###} · ordinary buffs";
        foreach(var (stat,limit) in stats.Limits)if(limit.Confirmed&&values.GetValueOrDefault(stat) is {} current)hints[stat]=current>=limit.Value?CapHint(current,limit.Value,stats.Uncapped.GetValueOrDefault(stat),limit.Source):$"Cap {limit.Value:0.###} · {limit.Source}";
        if(!enabled)return new(values,"Observed snapshot",stats.Conditions,details,hints);
        string condition=selected.Length==0?"No confirmed sustained buffs learned yet":string.Join(" · ",selected.Select(b=>b.Spell+" +"+(b.EffectiveRank?.ToString()??"?")));
        var baseline=research?.Sheets.FirstOrDefault(s=>s.Id==stats.CaptureId);
        bool unbuffed=stats.Unbuffed;
        if(!unbuffed){details.Add("A matching unbuffed equipped baseline is unavailable. Captured values below retain their original conditions; standard-buff totals are not calculated.");return new(values,"Standard-buff scenario · baseline unavailable",condition,details,hints);}
        if(!stats.MatchingEquipment)details.Add("Related unbuffed capture: gear does not have an exact matching fingerprint. Totals apply to that capture's loadout, not recalculated gear.");
        if(selected.Length==0)return new(values,"Standard-buff scenario",condition,details,hints);
        var magnitudes=selected.Select(b=>Magnitude(b.Spell,b.EffectiveRank,research)).ToArray();
        foreach(var m in magnitudes)details.Add(m.Spell+" +"+(m.Rank?.ToString()??"?")+" · "+(m.Estimated?"Estimated":"Recorded / reference")+" · "+string.Join(" · ",m.Values.Select(p=>p.Key+" +"+p.Value.ToString("0.###",CultureInfo.InvariantCulture)))+"\n"+m.Evidence);
        // Until whole-buff coexistence is established, do not cherry-pick a union of spell stats.
        if(selected.Length>1){details.Add("Multiple standard buffs selected; simultaneous stacking is unresolved. Captured baseline retained; combined totals unavailable.");foreach(string key in values.Keys.Where(k=>values[k].HasValue))hints[key]="Baseline · stacking unresolved";return new(values,"Standard-buff scenario · stacking unresolved",condition,details,hints);}
        var buff=selected[0];var magnitude=magnitudes[0];
        if(magnitude.Values.Count==0){details.Add("The learned spell/rank lacks supported numerical effects. Captured baseline retained.");foreach(string key in values.Keys.Where(k=>values[k].HasValue))hints[key]="Baseline · buff change unknown";return new(values,"Standard-buff scenario · effects unresolved",condition,details,hints);}
        bool reused=false;
        if(pair!=null&&baseline!=null&&pair.Spell==buff.Spell&&pair.Rank==buff.EffectiveRank&&research?.Sheets.FirstOrDefault(s=>s.Id==pair.Buffed) is {} observed&&observed.Reviewed&&observed.Context.Pet==baseline.Context.Pet&&observed.Context.Summon==baseline.Context.Summon&&observed.Context.Rank==baseline.Context.Rank&&observed.Context.Level==baseline.Context.Level&&observed.Context.Patch==baseline.Context.Patch&&observed.Context.Equipment==baseline.Context.Equipment&&observed.Context.FocusAndAA==baseline.Context.FocusAndAA)
        {
            foreach(var field in observed.Values.Where(p=>p.Value.HasValue))values[field.Key]=field.Value;
            details.Add("Measured paired buffed capture · "+observed.Label+" · "+pair.Evidence+". Same captured equipment/context; not a universal AC or STR→Attack rule.");reused=true;
            if(pair.SpeedCeiling is {} cap&&values.GetValueOrDefault("AttackSpeedPercent")>=cap)hints["AttackSpeedPercent"]=$"Capped · observed {cap:0.###} · excess unknown";
        }
        if(!reused)
        {
            foreach(string stat in new[]{"STR","AGI"})if(magnitude.Values.TryGetValue(stat,out double bonus))values[stat]=values.GetValueOrDefault(stat) is {} before?before+bonus:null;
            foreach(string field in new[]{"ACLeft","ACRight","AttackLeft","AttackRight","AC","ATK","AttackSpeedPercent"})
                if(values.GetValueOrDefault(field).HasValue){hints[field]="Baseline · buff change unknown";}
            details.Add("Displayed AC / Attack conversion and worn/spell/overhaste stacking are unresolved at this context/rank. Their displayed values remain labeled baseline; raw spell AC/haste are listed above.");
        }
        details.Add("Scenario assumes selected sustained buffs active, not measured uptime or a complete current roster. Gear is already included; Added remains gear-only. Equipment preference scores are unchanged.");
        return new(values,"Standard-buff scenario"+(magnitude.Estimated&&!reused?" · estimated":""),condition,details,hints);
    }
    internal static string CapHint(double value,double cap,double? uncapped,string source)
        =>value<cap?"":uncapped is {} raw&&raw>=cap?$"{raw-cap:0.###} over · {source}":$"Capped · {source} · excess unknown";
}
