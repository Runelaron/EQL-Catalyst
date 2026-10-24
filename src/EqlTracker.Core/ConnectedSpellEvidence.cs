using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal sealed record ConnectedSpell(string Name,int? Id,int? UpgradeRank,string Source,string Effects,
    bool? Beneficial=null,bool? Instant=null,double? DirectHeal=null,IReadOnlyDictionary<string,int>? Levels=null,int? Target=null,bool? CombatSkill=null);
internal sealed record ConnectedSpellIdentity(string Canonical,int? Rank,ConnectedSpell? Definition);

internal sealed class ConnectedSpellIndex
{
    internal const string Version="connected-spells-v2-canonical-landings";
    internal string EvidenceVersion { get; private set; }=Version;
    private readonly Dictionary<string,ConnectedSpell> spells=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> aliases=new(StringComparer.OrdinalIgnoreCase);
    internal ConnectedSpellIndex(IEnumerable<SpellReference> references,IReadOnlyDictionary<string,string>? redirects=null)
    {
        foreach(var spell in references)
        {
            string skill=spell.Fields.GetValueOrDefault("skill","");bool? combat=skill.Contains("Combat",StringComparison.OrdinalIgnoreCase)?true:new[]{"Alteration","Abjuration","Conjuration","Divination","Evocation"}.Contains(skill,StringComparer.OrdinalIgnoreCase)?false:null;
            spells[spell.Name]=new(spell.Name,null,0,spell.Url+" · revision "+spell.Revision,spell.Effects,CombatSkill:combat);
        }
        foreach(var spell in PetContextCatalog.Bundled.Spells)
        {
            spells.TryAdd(spell.Name,new(spell.Name,null,0,spell.Url+" · revision "+spell.Revision,""));
            foreach(string alias in spell.Aliases)aliases[alias]=spell.Name;
        }
        foreach(string name in new[]{"Burnout III","Verses of Victory"})spells.TryAdd(name,new(name,null,0,"Inspected spell identity; parameters require evidence",""));
        spells.TryAdd("Improved Healing III",new("Improved Healing III",null,0,"https://eqlwiki.com/Improved_Healing_III · revision 149780","1–20% eligible direct healing; max spell level 60; loses 5% effectiveness per level above 60; excludes Complete Heal, Promised Renewal, percentage heals and combat skills."));
        if(redirects!=null)foreach(var alias in redirects)
        {var match=spells.Keys.FirstOrDefault(n=>EffectReferences.Key(n)==alias.Value);if(match!=null)aliases[alias.Key]=match;}
        EvidenceVersion+="/"+PetComparisonEngine.Key(spells.Values.OrderBy(s=>s.Name).ToArray());
    }
    internal ConnectedSpellIdentity Resolve(string raw)
    {
        raw=Regex.Replace(raw.Trim(),@"\s+"," ");
        if(aliases.TryGetValue(raw,out string? alias)||aliases.TryGetValue(EffectReferences.Key(raw),out alias))raw=alias;
        if(spells.TryGetValue(raw,out var exact))return new(exact.Name,exact.UpgradeRank,exact);
        int split=raw.LastIndexOf(' ');
        if(split>0&&Upgrade(raw[(split+1)..]) is {} rank)
        {
            string name=raw[..split];
            if(aliases.TryGetValue(name,out alias)||aliases.TryGetValue(EffectReferences.Key(name),out alias))name=alias;
            if(spells.TryGetValue(name,out var baseline))return new(baseline.Name,rank,null); // Base parameters do not establish this rank's power.
        }
        return new(raw,null,null);
    }
    private static int? Upgrade(string suffix)
    {
        if(suffix.StartsWith('+')&&int.TryParse(suffix[1..],out int n)&&n is >=0 and <=1000)return n;
        if(!Regex.IsMatch(suffix,@"^(?=.)M?(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$",RegexOptions.IgnoreCase))return null;
        int value=0,last=0;foreach(char c in suffix.ToUpperInvariant().Reverse()){int v=c switch{'M'=>1000,'D'=>500,'C'=>100,'L'=>50,'X'=>10,'V'=>5,_=>1};value+=v<last?-v:v;last=v;}return value;
    }
    internal ConnectedSpellIndex WithClientFile(string path,CancellationToken token)
    {
        byte[] bytes=File.ReadAllBytes(path);token.ThrowIfCancellationRequested();
        string hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        return WithClientLines(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split('\n'),hash,token);
    }
    internal ConnectedSpellIndex WithClientLines(IEnumerable<string> lines,string fingerprint,CancellationToken token=default)
    {
        var pending=new Dictionary<string,ConnectedSpell>(StringComparer.OrdinalIgnoreCase);
        string[] classes=["WAR","CLR","PAL","RNG","SHD","DRU","MNK","BRD","ROG","SHM","NEC","WIZ","MAG","ENC","BST","BER"];
        try
        {
            foreach(string line in lines)
            {
                token.ThrowIfCancellationRequested();if(string.IsNullOrWhiteSpace(line))continue;var fields=line.TrimEnd('\r').Split('^');
                if(fields.Length is not (171 or 172)||fields[165]!="0"||fields[167]!="-1"||fields[168]!="-1"||fields[169]!="-1")return this;
                int I(int i)=>int.Parse(fields[i],CultureInfo.InvariantCulture);
                int id=I(0);var identity=Resolve(fields[1]);var effects=new List<PetSpellEffect>();
                if(fields[^1].Length>0)foreach(string tuple in fields[^1].Split('$'))
                {var cells=tuple.Split('|');if(cells.Length!=6)return this;var n=cells.Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(n.Any(v=>!double.IsFinite(v)))return this;effects.Add(new((int)n[0],(int)n[1],n[2],n[3],(int)n[4],n[5]));}
                var levels=new Dictionary<string,int>();for(int i=0;i<16;i++)if(I(36+i) is >0 and <=100)levels[classes[i]]=I(36+i);
                bool beneficial=I(28)>0,instant=I(12)==0;
                var healing=effects.Where(e=>e.Spa==0&&e.StaticValue>0).ToArray();
                double? direct=beneficial&&instant&&healing.Length==1?healing[0].StaticValue:null;
                pending[fields[1]]=new(identity.Canonical,id,identity.Rank,"Read-only client spell · SHA-256 "+fingerprint,
                    string.Join("; ",effects.Select(e=>$"slot {e.Slot}: SPA {e.Spa}, base {e.Base}, formula {e.Formula}, max {e.Max}")),beneficial,instant,direct,levels,I(30),identity.Definition?.CombatSkill??spells.GetValueOrDefault(identity.Canonical)?.CombatSkill);
            }
        }
        catch(Exception error) when(error is FormatException or OverflowException or IndexOutOfRangeException){return this;}
        foreach(var pair in pending)spells[pair.Key]=pair.Value;
        EvidenceVersion+="/client:"+fingerprint;return this;
    }
}

internal sealed class SpellActivityEvidence
{
    public string Id { get; set; }="";
    public DateTime At { get; set; }
    public string Canonical { get; set; }="";
    public string Raw { get; set; }="";
    public int? Rank { get; set; }
    public string Caster { get; set; }="";
    public string Recipient { get; set; }="";
    public string Transition { get; set; }="";
    public string Source { get; set; }="";
    public string[] Candidates { get; set; }=[];
}
internal sealed class ConnectedSpellState
{
    public string Version { get; set; }=ConnectedSpellIndex.Version;
    public long Revision { get; set; }
    public List<SpellActivityEvidence> History { get; set; }=[];
    internal HashSet<string>? KnownIds;
}
internal sealed record PetAbilityFact(string Name,string Kind,int Count,DateTime? Last,string Evidence);

internal static class ConnectedSpellEvidence
{
    internal static FileCheckpoint? InitialCheckpoint(FileCheckpoint? existing)
    {if(existing==null)return null;var copy=EquipmentPositions.Copy(existing);copy.Offset=0;return copy;}
    internal static void ReadMetadata(ConnectedSpellState state,IEnumerable<TailLine> lines,string character,IReadOnlyList<PetIdentity> pets,ConnectedSpellIndex index,SpellMessageIndex? messages,CancellationToken token=default)
    {
        var parser=new LogParser(character);var scratch=new StatEvidenceState();
        foreach(var line in lines)
        {
            token.ThrowIfCancellationRequested();
            if(parser.Parse(line.Text,line.Id) is {} parsed)Attempt(state,parsed,line.Id,character,pets,index);
            StatEvidenceReader.Observe(scratch,line.Text,line.Id,character,pets,messages,effect=>Effect(state,effect,index));
        }
    }
    internal static bool Add(ConnectedSpellState state,SpellActivityEvidence value)
    {
        state.KnownIds??=state.History.Select(e=>e.Id).ToHashSet();
        if(!state.KnownIds.Add(value.Id))
        {
            int existing=state.History.FindIndex(e=>e.Id==value.Id);
            if(existing<0||PetResearch.Hash(state.History[existing])==PetResearch.Hash(value)||state.History[existing].Transition=="Landing"&&value.Transition.StartsWith("Ambiguous"))return false;
            state.History[existing]=value;state.Revision++;return true;
        }
        if(state.History.Count>=4000&&value.At<state.History[^1].At){state.KnownIds.Remove(value.Id);return false;}
        int position=state.History.BinarySearch(value,Comparer<SpellActivityEvidence>.Create((a,b)=>b.At.CompareTo(a.At)));
        state.History.Insert(position<0?~position:position,value);
        if(state.History.Count>4000){state.KnownIds.Remove(state.History[^1].Id);state.History.RemoveAt(state.History.Count-1);}
        state.Revision++;return true;
    }
    internal static void Attempt(ConnectedSpellState state,ParsedLine line,string id,string character,IReadOnlyList<PetIdentity> pets,ConnectedSpellIndex index)
    {
        string raw=line.PlayerCast??line.ActivitySpell??"",caster=line.PlayerCast!=null?character:line.PetActivity??"";
        if(line.CastFailed){var prior=state.History.Where(e=>e.Caster==character&&e.Transition=="Cast attempt"&&e.At<=line.Time&&e.At>=line.Time.AddSeconds(-30)).MaxBy(e=>e.At);raw=line.FailedSpell??prior?.Raw??"";caster=character;}
        if(raw.Length==0||caster.Length==0||caster!=character&&!pets.Any(p=>p.Name.Equals(caster,StringComparison.OrdinalIgnoreCase)&&p.From<=line.Time&&(!p.Until.HasValue||line.Time<p.Until)))return;
        var resolved=index.Resolve(raw);Add(state,new(){Id=id+":spell-attempt",At=line.Time,Raw=raw,Canonical=resolved.Canonical,Rank=resolved.Rank,Caster=caster,Transition=line.CastFailed?"Interrupted / failed":"Cast attempt",Source="Logged cast evidence; recipient and success are not established"});
    }
    internal static void Effect(ConnectedSpellState state,EffectEvidence effect,ConnectedSpellIndex index)
    {
        if(effect.Spells.Count==0)return;
        var resolved=effect.Spells.Count==1?index.Resolve(effect.Spells[0]):null;
        var recent=state.History.Where(e=>e.At<=effect.At&&e.At>=effect.At.AddSeconds(-30)).ToArray();
        var candidates=effect.Spells.Select(s=>index.Resolve(s).Canonical).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failures=recent.Where(e=>e.Transition=="Interrupted / failed").ToArray();
        var casts=recent.Where(e=>e.Transition=="Cast attempt"&&candidates.Contains(e.Canonical)&&!failures.Any(f=>f.Caster==e.Caster&&f.Canonical==e.Canonical&&f.At>=e.At))
            .GroupBy(e=>(e.Caster,e.Canonical)).Select(g=>g.MaxBy(e=>e.At)!).ToArray();
        // An ambiguous client message can be resolved by one uninterrupted compatible cast.
        var unique=casts.Select(e=>(e.Caster,e.Canonical,e.Rank)).Distinct().Count()==1?casts[0]:null;
        if(effect.State=="Landing"&&unique!=null)resolved=index.Resolve(unique.Raw);
        string caster=casts.Select(e=>e.Caster).Distinct().Count()==1?casts[0].Caster:"";
        var ranks=casts.Select(e=>e.Rank).Distinct().ToArray();int? rank=ranks.Length==1?ranks[0]:resolved?.Rank>0?resolved.Rank:null;
        string recipient=effect.State=="Fade"&&effect.Owner=="Self"&&resolved?.Definition?.Target==14?"":effect.Actor;
        Add(state,new(){Id=effect.LogId+":spell-effect",At=effect.At,Raw=string.Join(" / ",effect.Spells),Canonical=resolved?.Canonical??"Ambiguous effect",Rank=rank,Caster=caster,Recipient=recipient,Transition=resolved==null?"Ambiguous "+effect.State:effect.State,Source=(caster.Length>0?"Caster/rank correlated from a unique uninterrupted cast in the preceding 30 seconds. ":"Caster unresolved. ")+effect.Evidence,Candidates=effect.Spells.ToArray()});
    }
    internal static string BuffHistory(ConnectedSpellState state,string recipient)
    {
        if(string.IsNullOrWhiteSpace(recipient))return "No confirmed recipient. Buff coverage is unresolved.";
        return FormatBuffHistory(state.History.Where(e=>e.Recipient.Equals(recipient,StringComparison.OrdinalIgnoreCase)));
    }
    internal static string PetBuffHistory(ConnectedSpellState state,PetBuild build,CharacterProfile profile)=>FormatBuffHistory(state.History.Where(e=>Owned(build,profile,e.Recipient,e.At)));
    internal static string PetBuffHistory(ConnectedSpellState state,CharacterProfile profile,IEnumerable<Encounter> encounters)
    {
        var ranges=encounters.Select(e=>(e.Start,e.End)).ToArray();
        return FormatBuffHistory(state.History.Where(e=>ranges.Any(r=>e.At>=r.Start&&e.At<=r.End)&&profile.PetIdentities.Any(p=>p.Name.Equals(e.Recipient,StringComparison.OrdinalIgnoreCase)&&p.From<=e.At&&(!p.Until.HasValue||e.At<p.Until))));
    }
    private static string FormatBuffHistory(IEnumerable<SpellActivityEvidence> history)
    {
        var effects=history.Where(e=>e.Transition.Contains("Landing")||e.Transition.Contains("Fade")).OrderByDescending(e=>e.At).Take(30).ToArray();
        return effects.Length==0?"No retained landing/fade evidence. Current buffs are unresolved.":string.Join("\n",effects.Select(e=>$"{e.At:MMM dd HH:mm:ss} · {(e.Canonical=="Ambiguous effect"?e.Raw:e.Canonical)} · rank {e.Rank?.ToString()??"Unknown"} · {e.Transition} · caster {(e.Caster.Length>0?e.Caster:"unknown")}\n{e.Source}"))+"\nHistorical received effects may be beneficial or detrimental. Duration, missing fades and complete current coverage remain unresolved.";
    }
    private static bool Owned(PetBuild build,CharacterProfile profile,string name,DateTime at)=>profile.PetIdentities.Any(p=>p.Element==build.PetType&&(p.CanonicalSummonSpell.Length==0||p.CanonicalSummonSpell==build.SummonSpell)&&(!build.SummonRankKnown||p.SummonRank==null||p.SummonRank==build.SummonRank)&&p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)&&p.From<=at&&(!p.Until.HasValue||at<p.Until));
    internal static IReadOnlyList<PetAbilityFact> PetAbilities(PetBuild build,CharacterProfile profile,IEnumerable<Encounter> fights,ConnectedSpellState state,ConnectedSpellIndex? index=null)
    {
        bool IsOwned(string name,DateTime at)=>Owned(build,profile,name,at);
        string SpellName(string raw){var spell=index?.Resolve(raw);return spell==null?raw:spell.Canonical+(spell.Rank.HasValue?" · upgrade rank "+spell.Rank:"");}
        var facts=new List<PetAbilityFact>();
        string[] documented=build.PetType switch{"Water"=>["Backstab"],"Air"=>["Monk special attacks"],"Earth"=>["Ranger spells and melee"],"Fire"=>["Wizard spells and melee"],"Monster"=>["Berserker Frenzy"],_=>PetDefinition.For(build.PetType).Classes.Length>0?[PetDefinition.For(build.PetType).Abilities]:[]};
        facts.AddRange(documented.Select(n=>new PetAbilityFact(n,"Documented capability",0,null,PetDefinition.For(build.PetType).Source+" · applicability and numerical power depend on summon context")));
        var specials=new HashSet<string>(["bash","bashes","kick","kicks","backstab","backstabs","flying kick","flying kicks","round kick","round kicks","tiger claw","tiger claws","eagle strike","eagle strikes","dragon punch","dragon punches","tail rake","tail rakes","frenzy","frenzies"],StringComparer.OrdinalIgnoreCase);
        var events=fights.SelectMany(f=>f.Events).Where(e=>e.Owner=="Pet"&&IsOwned(e.Actor,e.Time)&&!e.ActivityOnly&&(e.Kind is "Spell" or "DoT"||specials.Contains(e.Ability))).DistinctBy(e=>e.Id);
        foreach(var group in events.GroupBy(e=>(e.Ability,e.Kind)))facts.Add(new(group.Key.Kind=="Melee"?group.Key.Ability:SpellName(group.Key.Ability),group.Key.Kind=="Melee"?"Observed special attack":"Observed spell damage; proc origin may be unresolved",group.Count(),group.Max(e=>e.Time),"Logged ability: "+group.Key.Ability+" · owned-pet identity interval · summon/rank matched where known; equipment and buffs belong to each encounter"));
        foreach(var group in fights.SelectMany(f=>f.Heals).Where(e=>e.HealerOwner=="Pet"&&IsOwned(e.Healer,e.Time)).DistinctBy(e=>e.Id).GroupBy(e=>e.Ability))facts.Add(new(SpellName(group.Key),"Observed healing",group.Count(),group.Max(e=>e.Time),"Logged ability: "+group.Key+" · owned-pet healing records; effective restored HP is distinct from base spell power."));
        foreach(var group in state.History.Where(e=>IsOwned(e.Caster,e.At)&&e.Transition=="Cast attempt").GroupBy(e=>(e.Canonical,e.Rank)))facts.Add(new(group.Key.Canonical+" · upgrade rank "+(group.Key.Rank?.ToString()??"Unknown"),"Observed cast attempts",group.Count(),group.Max(e=>e.At),"Cast attempts alone do not prove a successful effect or innate spell rather than a configured effect."));
        return facts;
    }
}

internal static class HealingFocusEvidence
{
    internal static string Explain(ConnectedSpellIndex index,string spell,IEnumerable<EquipmentPosition> positions,Catalog catalog,bool pet,int casterLevel,bool overTime,IEnumerable<string>? casterClasses=null)
    {
        var identity=index.Resolve(spell);
        var active=new List<string>();bool rankedFocus=false;
        foreach(var position in positions.Where(p=>p.Item!=null&&p.Knowledge==EquipmentKnowledge.Occupied))
        {
            var entry=position.Item!;var item=catalog.FindKey(entry.ConfirmedItemKey)??catalog.Find(entry.Name);if(item==null)continue;
            var selected=PetEffectPolicy.SelectedTexts(entry).ToList();
            if(!pet&&!entry.Augments.Any(a=>ExaltationPlanner.EffectType(a.EffectText)=="Focus"))selected.AddRange(ItemEffects.Active(item,entry.TransferredOutEffects).Where(e=>ExaltationPlanner.EffectType(e)=="Focus"));
            foreach(var effect in selected.Where(e=>ExaltationPlanner.EffectType(e)=="Focus"))
            {var focus=index.Resolve(ItemEffects.Canonical(effect).Split('(')[0].Trim());if(focus.Canonical=="Improved Healing III"){active.Add(item.Name);rankedFocus|=focus.Rank!=0;}}
        }
        if(active.Count==0)return "No active Improved Healing III in this caster's effective loadout. Recipient equipment does not modify the caster's spells.";
        const string source="https://eqlwiki.com/Improved_Healing_III · revision 149780";
        string prefix="Current "+(pet?"pet":"player")+" loadout · "+string.Join(", ",active.Distinct())+". Historical caster gear is not established. ";
        if(rankedFocus||active.Count>1)return prefix+"Focus rank scaling / multiple-source stacking unresolved; no expected range calculated. "+source;
        if(overTime||identity.Canonical is "Complete Heal" or "Promised Renewal"||identity.Definition?.CombatSkill==true)return prefix+"Excluded: this focus does not apply to HoT / excluded healing spells / combat skills. "+source;
        var definition=identity.Definition;
        if(definition?.Beneficial!=true||definition.Instant!=true||definition.DirectHeal==null||definition.Levels==null||definition.Levels.Count==0||identity.Rank==null||definition.CombatSkill!=false)return prefix+"Possible focus modifier: documented 1–20% on qualifying direct heals. This spell/rank's static healing or restrictions are unresolved; no expected range calculated. "+source;
        var applicable=definition.Levels.Where(p=>casterClasses==null||casterClasses.Contains(p.Key)).Select(p=>p.Value).Distinct().ToArray();
        if(applicable.Length!=1)return prefix+"Caster class / spell-level applicability unresolved; no expected range calculated. "+source;
        int required=applicable[0];
        if(casterLevel<required)return prefix+"Caster eligibility unresolved; no expected range calculated. "+source;
        double decay=Math.Max(0,1-Math.Max(0,required-60)*.05);
        return prefix+$"Supported static-spell reference range: {definition.DirectHeal*(1+.01*decay):0.###}–{definition.DirectHeal*(1+.20*decay):0.###}. Focus 1–20%, with documented level decay; excludes crits, other modifiers and unverified stacking. This is not a prediction for the selected historical encounter. "+source;
    }
}
