namespace EqlTracker.Core;

internal sealed class BardModifierCapture
{
    public DateTime At {get;set;}
    public string Caster {get;set;}="";
    public string Context {get;set;}="";
    public Dictionary<string,double> FinalMultipliers {get;set;}=[];
    public double? WithAmplification {get;set;}
    public int? AmplificationRank {get;set;}
    public string Source {get;set;}="";
}
internal sealed record BardComponent(EffectInput Effect,bool? SkillEnhanced);
internal sealed record BardSongFact(BardSong Song,string Skill,IReadOnlyList<BardComponent> Components,bool Adjusted,string Evidence);
internal sealed record BardSwapOption(BardSong Song,string Reason,string Evidence,IReadOnlyList<CompatibilityDelta> Changes);
internal sealed record BardComparisonRow(BardSong Song,string Status,string Reason,string Evidence,IReadOnlyList<BardSwapOption> Options);
internal sealed record BardComparisonView(string SetId,string Title,string Context,IReadOnlyList<BardComparisonRow> Rows,IReadOnlyList<string> Conditions);
internal sealed record BardComparisonInput(BardRotationSet? Set,BardSetup Setup,IReadOnlyList<BardSong> Known,IReadOnlyDictionary<string,BardSongFact> Facts,
    IReadOnlyList<CoverageObservation> Coverage,IReadOnlySet<string> Group,EvaluationContext Context,IReadOnlyList<EffectRule> Rules,
    BardModifierCapture? Modifier,bool EffectiveStrength,DateTime At,IReadOnlyList<string> Conditions);

internal static class BardSongComparison
{
    internal const string Version="bard-comparison-v1-rank-precedence";
    internal const string EffectiveTooltip="Include supported singing, instrument focus, confirmed AA and Amplification gains when suggesting replacements. Higher spell ranks still override lower ranks within supported overlap groups. Unknown power or stacking is never assumed.";
    internal static string FactKey(BardSong song)=>BardRotations.VersionKey(song.Identity);
    internal static BardSongFact Fact(BardSong song,ConnectedSpellIndex index,IEnumerable<BardSong> reviewed)
    {
        var exact=reviewed.FirstOrDefault(s=>s.Identity==song.Identity&&s.Effects.Count>0);
        if(exact!=null)return new(song,index.Resolve(song.Identity.Name).Definition?.Skill??"Unknown",exact.Effects.Select(e=>new BardComponent(EquipmentPositions.Copy(e),e.Family=="HastePercent"?false:song.Identity.Name=="Verses of Victory"?true:null)).ToArray(),true,"Reviewed adjusted components · "+string.Join("; ",exact.Effects.Select(e=>e.Source).Distinct()));
        var definition=index.Resolve(song.Identity.Name).Definition;var effects=new List<BardComponent>();
        EffectInput Effect(string family,double? amount,string source,EvidenceQuality quality=EvidenceQuality.Estimated)=>new(){Identity=song.Identity,Family=family,Amount=amount,Channel=family=="HastePercent"?EffectChannel.OrdinaryHaste:family=="Damage"?EffectChannel.Damage:EffectChannel.Attribute,Quality=quality,Source=source};
        if(song.Identity.Name=="Amplification")return new(song,"Singing",[],false,"Amplification is modeled through a reviewed, rank-matched combined Singing multiplier. Wiki overview, effect text and client formula disagree: https://eqlwiki.com/Amplification");
        if(song.Identity.Name=="Verses of Victory")
        {
            var magnitude=StandardPetBuffs.Magnitude(song.Identity.Name,song.Identity.UpgradeRank,null);
            foreach(var (stat,n) in magnitude.Values)effects.Add(new(Effect(stat,n,magnitude.Evidence),stat!="HastePercent"));
        }
        else if(song.Identity.UpgradeRank==0&&definition?.Parameters!=null)
        {
            foreach(var parameter in definition.Parameters)
            {
                string family=parameter.Spa switch{0=>parameter.Base>=0?"HPRegen":"Damage",1=>"SpellAC",2=>"Attack",3=>"MovementSpeed",4=>"STR",5=>"DEX",6=>"AGI",7=>"STA",8=>"INT",9=>"WIS",10=>"CHA",11=>"HastePercent",15=>"ManaRegen",44=>"ResistFire",45=>"ResistDisease",46=>"ResistCold",49=>"ResistPoison",50=>"ResistMagic",59=>"DamageShield",_=>"Unreviewed component · SPA "+parameter.Spa};
                double? value=parameter.StaticValue;if(family=="HastePercent"&&value.HasValue)value-=100;
                if(family.StartsWith("Unreviewed",StringComparison.Ordinal))value=null;
                effects.Add(new(Effect(family,value,definition.Source+" · static base parameters only"),family=="HastePercent"?false:definition.InstrumentEnhanced));
            }
        }
        if(effects.Count==0)effects.Add(new(Effect("Unreviewed song effects",null,"Exact rank effects have not been captured",EvidenceQuality.Unknown),null));
        return new(song,definition?.Skill??"Unknown",effects,false,string.Join("\n",effects.Select(e=>e.Effect.Source).Distinct()));
    }
    internal static BardComparisonView Evaluate(BardComparisonInput input,CancellationToken token=default)
    {
        if(input.Set==null)return new("","Detected","",[],input.Conditions.Concat(["No repeating set is confirmed yet. Saved EQ sets can be pinned without claiming they are playing."]).ToArray());
        var selected=BardRotations.Select(input.Set,input.Setup.ConfirmedAuraRank).Select(EquipmentPositions.Copy).ToArray();
        foreach(var song in selected)song.Keep|=input.Setup.Songs.Any(s=>s.Keep&&BardRotations.Key(s.Identity)==BardRotations.Key(song.Identity));
        var conditions=input.Conditions.ToList();if(input.Setup.ConfirmedAuraRank==null)conditions.Add("Aura capacity unconfirmed; proposals preserve the observed number of songs.");
        if(input.Modifier==null&&input.Facts.Values.Any(f=>!f.Adjusted&&f.Components.Any(c=>c.SkillEnhanced!=false)))conditions.Add("Combined singing/instrument potency needs review; AA descriptions alone do not establish stacking.");
        if(selected.Length==0)conditions.Add("Selection is unresolved: an earlier eligible gem lacks complete eligibility metadata.");
        var active=input.Coverage.Where(c=>input.Group.Contains(c.Caster)&&c.Caster!=input.Context.Character&&c.Fresh(input.At,input.Context.Recipient)).GroupBy(c=>(c.Caster,c.Recipient,c.Song)).Select(g=>g.MaxBy(c=>c.At)!).ToArray();
        if(active.Length==0)conditions.Add("No supported current group coverage for this recipient; old landings are not a live roster.");
        var external=active.SelectMany(c=>c.Effects.Select(e=>{var copy=EquipmentPositions.Copy(e);copy.Identity=c.Song;copy.Caster=c.Caster;copy.Recipient=c.Recipient;return copy;})).ToArray();
        var rows=new List<BardComparisonRow>();var combinations=new List<BardSong[]>{selected};var reserved=new HashSet<string>();
        for(int row=0;row<selected.Length;row++)
        {
            token.ThrowIfCancellationRequested();var current=selected[row];var options=new List<BardSwapOption>();
            if(!current.Keep&&input.Context.Classes.Contains("BRD")&&input.Context.CasterLevel.HasValue)
            foreach(var candidate in input.Known.Where(s=>s.Available&&s.RequiredLevel.HasValue&&s.RequiredLevel<=input.Context.CasterLevel&&s.AuraEligible==true&&s.Identity.UpgradeRank.HasValue&&s.Identity!=current.Identity&&!reserved.Contains(FactKey(s))).OrderBy(s=>s.Identity.Name).ThenByDescending(s=>s.Identity.UpgradeRank))
            {
                if(selected.Where((_,i)=>i!=row).Any(s=>BardRotations.Key(s.Identity)==BardRotations.Key(candidate.Identity)))continue;
                CompatibilityComparison? result=null;bool valid=true;string proof="";
                foreach(var prior in combinations)
                {
                    token.ThrowIfCancellationRequested();var after=prior.ToArray();after[row]=candidate;
                    var beforeEffects=Effects(prior,input);var afterEffects=Effects(after,input);
                    var compared=BuildCompatibility.Compare(input.Context,Ranked(external.Concat(beforeEffects),input.Rules,input.Context),Ranked(external.Concat(afterEffects),input.Rules,input.Context),Rules(external.Concat(beforeEffects).Concat(afterEffects),input.Rules));
                    bool gain=compared.Changes.Any(c=>c.UsableChange>0&&(input.EffectiveStrength||c.Before==0||c.Before==null&&c.After.HasValue));
                    // Rank-only mode still permits an eligible same-song rank upgrade.
                    gain|=!input.EffectiveStrength&&BardRotations.Key(candidate.Identity)==BardRotations.Key(current.Identity)&&candidate.Identity.UpgradeRank>current.Identity.UpgradeRank&&compared.Changes.Any(c=>c.UsableChange>0);
                    if(!compared.CanClaimUpgrade||!gain||compared.Changes.Any(c=>c.UsableChange<0)){valid=false;break;}
                    result=compared;proof=string.Join("\n",compared.Changes.Where(c=>c.UsableChange!=0).Select(c=>$"{c.Family}: {c.Before:0.###} → {c.After:0.###}"));
                }
                if(valid&&result!=null)options.Add(new(candidate,candidate.Identity.Name=="Amplification"?"Strengthens the retained Singing songs":"Adds useful group coverage without a supported loss",proof+"\nWhole proposed set checked; higher ranks retain precedence.\n"+input.Facts.GetValueOrDefault(FactKey(candidate))?.Evidence,result.Changes));
            }
            // Do not call one benefit universally best across unrelated families.
            var frontier=options.Where(a=>!options.Any(b=>!ReferenceEquals(a,b)&&Dominates(b.Changes,a.Changes))).Take(3).ToArray();
            if(frontier.Length>0)
            {
                foreach(var option in frontier)reserved.Add(FactKey(option.Song));
                combinations=combinations.SelectMany(previous=>frontier.Select(o=>{var after=previous.ToArray();after[row]=o.Song;return after;})).ToList();
            }
            string status=frontier.Length==1?"Swap":frontier.Length>1?"Options":"Keep";
            string reason=frontier.Length>0?frontier[0].Reason:current.Keep?"Pinned to keep":!current.Identity.UpgradeRank.HasValue?"Rank needs confirmation":!input.Context.Classes.Contains("BRD")?"Active Bard class needs confirmation":"No supported replacement improves this set";
            var fact=input.Facts.GetValueOrDefault(FactKey(current));if(frontier.Length==0&&Effects([current],input).Any(c=>!c.Amount.HasValue)){status="Needs evidence";reason="Ranked effects or combined modifiers need review";}
            rows.Add(new(current,status,reason,(fact?.Evidence??"Song metadata unavailable")+"\n"+string.Join("\n",conditions)+"\nHigher-rank precedence is user-confirmed within supported overlap groups; not a cross-song value score.",frontier));
        }
        return new(input.Set.Id,input.Set.Name,input.Context.Live?"Current group":"Historical group context · "+input.At.ToString("g"),rows,conditions.Distinct().ToArray());
    }
    internal static IReadOnlyList<EffectInput> Effects(IEnumerable<BardSong> songs,BardComparisonInput input)
    {
        var set=songs.ToArray();var amp=set.FirstOrDefault(s=>s.Identity.Name=="Amplification");var result=new List<EffectInput>();
        foreach(var song in set)
        {
            if(song.Identity.Name=="Amplification")
            {if(input.Modifier?.Source.Length>0&&input.Modifier.WithAmplification.HasValue&&input.Modifier.AmplificationRank==song.Identity.UpgradeRank)continue;result.Add(new(){Identity=song.Identity,Family="Amplification power unresolved",Quality=EvidenceQuality.Unknown});continue;}
            var fact=input.Facts.GetValueOrDefault(FactKey(song));if(fact==null){result.Add(new(){Identity=song.Identity,Family="Missing song metadata",Quality=EvidenceQuality.Unknown});continue;}
            foreach(var component in fact.Components)
            {
                var effect=EquipmentPositions.Copy(component.Effect);effect.Identity=song.Identity;effect.Caster=input.Context.Character;effect.Recipient=input.Context.Recipient;
                if(fact.Adjusted&&amp!=null&&fact.Skill=="Singing"&&component.SkillEnhanced!=false)
                {
                    var modifier=input.Modifier;double? original=modifier?.FinalMultipliers.GetValueOrDefault("Singing");
                    if(component.SkillEnhanced==true&&modifier?.Source.Length>0&&original is >0&&modifier.AmplificationRank==amp.Identity.UpgradeRank&&modifier.WithAmplification.HasValue)effect.Amount*=modifier.WithAmplification/original;
                    else effect.Amount=null;
                }
                if(!fact.Adjusted&&component.SkillEnhanced!=false)
                {
                    double? multiplier=input.Modifier?.Source.Length>0&&input.Modifier.FinalMultipliers.TryGetValue(fact.Skill,out double value)?value:null;
                    if(amp!=null&&fact.Skill=="Singing")multiplier=input.Modifier?.AmplificationRank==amp.Identity.UpgradeRank?input.Modifier?.WithAmplification:null;
                    if(component.SkillEnhanced==null||!multiplier.HasValue)effect.Amount=null;else effect.Amount*=multiplier;
                }
                result.Add(effect);
            }
        }
        return result;
    }
    private static bool Dominates(IReadOnlyList<CompatibilityDelta> a,IReadOnlyList<CompatibilityDelta> b)
    {var keys=a.Select(x=>x.Family).Concat(b.Select(x=>x.Family)).Distinct();bool greater=false;foreach(string key in keys){double x=a.FirstOrDefault(d=>d.Family==key)?.UsableChange??0,y=b.FirstOrDefault(d=>d.Family==key)?.UsableChange??0;if(x<y)return false;greater|=x>y;}return greater;}
    private static EffectRule[] Rules(IEnumerable<EffectInput> source,IReadOnlyList<EffectRule> rules)
    {
        var same=source.GroupBy(e=>(Name:BardRotations.Key(e.Identity),e.Family,e.Channel)).Where(g=>g.Count()>1).Select(g=>new EffectRule{Id="same-song:"+g.Key.Name+g.Key.Family,Family=g.Key.Family,SpellIds=g.Select(e=>e.Identity.SpellId).Distinct().ToList(),Kind=EffectRuleKind.Strongest,Quality=EvidenceQuality.UserConfirmed,Source="User-confirmed same-song overlap; higher upgrade rank has precedence",Version=Version});
        return rules.Concat(same).ToArray();
    }
    internal static IReadOnlyList<EffectInput> Ranked(IEnumerable<EffectInput> source,IReadOnlyList<EffectRule> rules,EvaluationContext context)
    {
        var effects=source.ToArray();var result=new List<EffectInput>();
        foreach(var group in effects.GroupBy(e=>(e.Family,e.Channel)))
        {
            var members=group.ToArray();bool supported=members.Select(e=>BardRotations.Key(e.Identity)).Distinct().Count()==1||rules.Any(r=>r.Kind==EffectRuleKind.Strongest&&r.Family==group.Key.Family&&members.All(e=>r.SpellIds.Contains(e.Identity.SpellId))&&r.Applies(context,members));
            if(!supported||group.Key.Channel==EffectChannel.Damage){result.AddRange(members);continue;}
            if(members.Any(e=>!e.Identity.UpgradeRank.HasValue)){result.AddRange(members.Select(e=>{var copy=EquipmentPositions.Copy(e);copy.Amount=null;copy.Quality=EvidenceQuality.Unknown;copy.Source+=" · overlap rank needs confirmation";return copy;}));continue;}
            int rank=members.Max(e=>e.Identity.UpgradeRank!.Value);result.AddRange(members.Where(e=>e.Identity.UpgradeRank==rank));
        }
        return result;
    }
}
