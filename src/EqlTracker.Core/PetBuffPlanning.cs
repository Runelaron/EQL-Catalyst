using System.Globalization;

namespace EqlTracker.Core;

public sealed class PetBuffChoice
{
    public string Id { get; set; }="";
    public bool Enabled { get; set; }
    public int? Rank { get; set; }
    public double? Uptime { get; set; }
    public bool OwnershipConfirmed { get; set; }
    public bool External { get; set; }
}
public sealed record PetSpellEffect(int Slot,int Spa,double Base,double Limit,int Formula,double Max)
{ public double? StaticValue=>Formula is 0 or 100?Base:null; }
public sealed class PetBuffDefinition
{
    public string Id { get; set; }="";
    public string Name { get; set; }="";
    public Dictionary<string,int> Classes { get; set; }=[];
    public string Source { get; set; }="https://eqltools.com/sources";
    public string SourceItem { get; set; }="";
    public bool Aa { get; set; }
    public string RelatedAa { get; set; }="";
    public int? ConfirmedRank { get; set; }
    public bool Persistent { get; set; }=true;
    public bool ParametersComplete { get; set; }
    public int Target { get; set; }=14;
    public int CastRestriction { get; set; }
    public int CastMs { get; set; }
    public int RecoveryMs { get; set; }
    public int RecastMs { get; set; }
    public int Mana { get; set; }
    public List<PetSpellEffect> Effects { get; set; }=[];
    public string Description=>Name+(ParametersComplete?" · client parameters; learnability unverified":" · reference only; effects unresolved");
    public override string ToString()=>Description;
}
public sealed class PetBuffCatalog
{
    public string Version { get; set; }="reference-v1";
    public string Status { get; set; }="Client parameters unavailable; reference choices remain unverified.";
    public List<PetBuffDefinition> Buffs { get; set; }=[];
    public static PetBuffCatalog References()=>new(){Buffs=[
        new(){Id="reference:burnout3",Name="Burnout III",Classes=new(){["MAG"]=0},Source="https://eqlwiki.com/Burnout_III"},
        new(){Id="reference:augmentdeath",Name="Augmentation of Death",Classes=new(){["NEC"]=0},Source="https://eqlwiki.com/Augmentation_of_Death"},
        new(){Id="reference:intensifydeath",Name="Intensify Death",Classes=new(){["NEC"]=0,["SHD"]=0},Source="https://eqlwiki.com/Intensify_Death"},
        new(){Id="reference:strengthendeath",Name="Strengthen Death",Classes=new(){["NEC"]=0,["SHD"]=0},Source="https://eqlwiki.com/Strengthen_Death"},
        new(){Id="reference:quickening",Name="Spirit Quickening",Classes=new(){["BST"]=0},Source="https://eqlwiki.com/Spirit_Quickening"},
        new(){Id="reference:frenziedburnout",Name="Frenzied Burnout",Classes=new(){["MAG"]=0},Aa=true,RelatedAa="Companion's Fury",Source="https://eqlwiki.com/Companion%27s_Fury"} ]};
    public PetBuffDefinition? Find(string id)=>Buffs.FirstOrDefault(b=>b.Id==id);
    public IEnumerable<PetBuffDefinition> Choices(LoadoutProfile owner,bool external)=>Buffs.Where(b=>external||b.Classes.Any(c=>owner.Classes.Select(CastingPriority.Code).Contains(c.Key)&&c.Value<=owner.Level)||b.SourceItem.Length>0||b.Aa&&b.ConfirmedRank>0).OrderBy(b=>b.Name,StringComparer.Ordinal);
}
public sealed record PetBuffAssessment(string Name,string State,string Detail,string Source);
public sealed record PetBuffPair(string State,string Detail,string? Excluded=null);
public static class PetBuffResolver
{
    public static PetBuffPair Pair(PetBuffDefinition a,PetBuffDefinition b)
    {
        if(a.Id==b.Id)return new("Duplicate","Same spell identity; never counted twice.",b.Id);
        if(!a.ParametersComplete||!b.ParametersComplete)return new("Unresolved","Complete effect parameters unavailable; no assumed stacking.");
        if(!a.Persistent||!b.Persistent)return new("No persistent collision","An instant effect does not occupy a persistent buff slot.");
        if(a.CastRestriction!=0||b.CastRestriction!=0||a.Target!=14||b.Target!=14)return new("Unresolved","Target/casting restrictions need confirmation.");
        foreach(var (holder,other) in new[]{(a,b),(b,a)})foreach(var command in holder.Effects.Where(e=>e.Spa is 148 or 149))
        {
            var target=other.Effects.FirstOrDefault(e=>e.Slot==command.Limit&&e.Spa==command.Base);if(target==null)continue;
            if(target.StaticValue==null)return new("Unresolved","Stacking threshold depends on a level-scaled effect.");
            if(target.StaticValue<command.Max)return new("Computed conflict","SPA "+command.Spa+" threshold excludes the complete "+other.Name+" buff; EQL server overrides remain unverified.",other.Id);
        }
        bool aWins=false,bWins=false;
        foreach(var x in a.Effects.Where(e=>e.Spa is not (148 or 149)))
        {
            var y=b.Effects.FirstOrDefault(e=>e.Slot==x.Slot&&e.Spa==x.Spa);if(y==null||x.Spa==10&&x.Base==0&&y.Base==0)continue;
            if(x.Spa is not (1 or 2 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 15 or 59 or 69 or 119) || x.StaticValue==null || y.StaticValue==null)return new("Unresolved","Collision has an unsupported or level-scaled effect. No proc-ID or formula-value winner is invented.");
            if(x.Base>y.Base)aWins=true;else if(y.Base>x.Base)bWins=true;else {aWins=true;bWins=true;}
        }
        if(aWins&&bWins)return new("Computed conflict","Equal or competing effects collide; choose an entire buff, not its strongest individual stats.");
        if(aWins||bWins)return new("Computed conflict","Same-slot effect collision; complete weaker buff excluded. Server behavior still requires verification.",aWins?b.Id:a.Id);
        return new("Computed coexistence","No encoded slot collision found. Worn haste, spell haste and overhaste remain distinct; server exceptions/caps are unverified.");
    }
    public static List<PetBuffAssessment> Resolve(PetBuffCatalog catalog,IEnumerable<PetBuffChoice> choices,LoadoutProfile owner,string pet,bool external)
    {
        var selected=choices.Where(c=>c.Enabled).GroupBy(c=>c.Id).Select(g=>g.First()).ToList();var result=new List<PetBuffAssessment>();
        foreach(var choice in selected)
        {
            var definition=catalog.Find(choice.Id);if(definition==null){result.Add(new(choice.Id,"Unresolved","Saved spell identity is not in the current catalog.",""));continue;}
            bool own=definition.SourceItem.Length>0||definition.Aa&&definition.ConfirmedRank>0||definition.Classes.Any(c=>owner.Classes.Select(CastingPriority.Code).Contains(c.Key)&&c.Value<=owner.Level);
            if((!own||choice.External)&&!external){result.Add(new(definition.Name,"Excluded","External buffs are disabled.",definition.Source));continue;}
            if(definition.Aa&&(!choice.OwnershipConfirmed||choice.Rank==null)){result.Add(new(definition.Name,"Unverified AA","Confirm AA ownership and rank; no assumed bonus or uptime.",definition.Source));continue;}
            if((definition.Aa?choice.Rank is <1 or >100:choice.Rank is <0 or >10) || choice.Uptime is <0 or >1 || choice.Uptime is {} u&&!double.IsFinite(u)){result.Add(new(definition.Name,"Unresolved","Spell rank must be 0–10; AA rank 1–100 with confirmed evidence; uptime 0–100% or Unknown.",definition.Source));continue;}
            string status=definition.ParametersComplete?"Computed · target/effects only":"Unresolved effects";
            string detail=(choice.OwnershipConfirmed?"Ownership confirmed":"Learning/source possession unverified")+" · rank "+(choice.Rank?.ToString()??"Unknown")+" · uptime "+(choice.Uptime is {} up?(up*100).ToString("0.#")+"%":"Unknown")+". No numerical DPS bonus inferred.";
            if(definition.CastRestriction!=0||definition.Target!=14){status="Unresolved restrictions";detail+=" Confirm applicability to "+pet+".";}
            foreach(var other in selected.Where(o=>o.Id!=choice.Id))
            {
                var d=catalog.Find(other.Id);if(d==null||(!external&&d.SourceItem.Length==0&&!(d.Aa&&d.ConfirmedRank>0)&&!d.Classes.Any(c=>owner.Classes.Select(CastingPriority.Code).Contains(c.Key)&&c.Value<=owner.Level)))continue;
                if(d.Aa&&(!other.OwnershipConfirmed||other.Rank==null))continue;
                var pair=choice.Rank!=0||other.Rank!=0?new PetBuffPair("Unresolved","Rank scaling unverified; base spell parameters do not establish an upgraded-buff winner."):Pair(definition,d);detail+="\n"+d.Name+": "+pair.State+" · "+pair.Detail;if(pair.Excluded==definition.Id)status="Excluded by computed conflict";else if(pair.State=="Unresolved"&&!status.StartsWith("Excluded"))status="Unresolved stacking";
            }
            if(definition.SourceItem.Length>0)detail+="\nSource item: "+definition.SourceItem+" · recorded possession; no physical quantity inferred.";
            if(definition.Effects.Count>0)detail+="\n"+string.Join("; ",definition.Effects.Select(e=>$"slot {e.Slot}: SPA {e.Spa}, base {e.Base}, formula {e.Formula}, max {e.Max}"));
            result.Add(new(definition.Name,status,detail,definition.Source));
        }
        if(result.Count==0)result.Add(new("Buff setup","Unverified · none selected","No active buff roster is inferred from a landing message. Select and confirm the intended buffs. Worn haste is evaluated separately from spell haste and overhaste.","https://eqltools.com/sources"));return result;
    }
}

/// <summary>Independent read-only adapter for documented 171/172-column EQL formats.
/// Format reference: Amerzel/eql-info SPELL_FORMAT.md (MIT); no game files are distributed.</summary>
public static class PetClientSpellAdapter
{
    public const string Version="eql-spells-171-172-v1";
    private static readonly string[] Classes=["WAR","CLR","PAL","RNG","SHD","DRU","MNK","BRD","ROG","SHM","NEC","WIZ","MAG","ENC","BST","BER"];
    public static PetBuffCatalog Parse(IEnumerable<string> lines,CancellationToken token)
    {
        var catalog=PetBuffCatalog.References();int count=0;
        try
        {
            foreach(string line in lines)
            {
                token.ThrowIfCancellationRequested();if(line.Length==0)continue;var f=line.Split('^');if(f.Length is not (171 or 172) || f[165]!="0"||f[167]!="-1"||f[168]!="-1"||f[169]!="-1")throw new FormatException("Unsupported client spell format; numerical parsing disabled.");
                int I(int index)=>int.Parse(f[index],CultureInfo.InvariantCulture);int id=I(0);count++;
                if(I(28)==0||I(30)!=14||I(12)<=0)continue;
                var levels=new Dictionary<string,int>();for(int n=0;n<16;n++){int level=I(36+n);if(level is >0 and <=50)levels[Classes[n]]=level;}if(levels.Count==0)continue;
                var effects=new List<PetSpellEffect>();string blob=f[^1];if(blob.Length>0)foreach(string chunk in blob.Split('$'))
                {
                    var cells=chunk.Split('|');if(cells.Length!=6)throw new FormatException("Unsupported effect tuple.");var nums=cells.Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();if(nums.Any(v=>!double.IsFinite(v)))throw new FormatException("Nonfinite effect parameter.");effects.Add(new((int)nums[0],(int)nums[1],nums[2],nums[3],(int)nums[4],nums[5]));
                }
                var prior=catalog.Buffs.FirstOrDefault(b=>b.Name.Equals(f[1],StringComparison.OrdinalIgnoreCase));string stable=prior?.Id??"spell:"+id;
                if(prior!=null)catalog.Buffs.Remove(prior);
                catalog.Buffs.Add(new(){Id=stable,Name=f[1],Classes=levels,Target=I(30),Persistent=true,ParametersComplete=effects.Count>0,Effects=effects,CastRestriction=I(136),CastMs=I(8),RecoveryMs=I(9),RecastMs=I(10),Mana=I(14),Source="Local client spell "+id+" · "+Version});
            }
            catalog.Version=Version+":"+count+":"+PetComparisonEngine.Key(catalog.Buffs);catalog.Status=$"Read-only client catalog · {count:N0} records validated; pet buff learnability/server exceptions remain unverified.";return catalog;
        }
        catch(Exception e) when(e is FormatException or OverflowException or IndexOutOfRangeException)
        {var fallback=PetBuffCatalog.References();fallback.Status="Client format not recognized; parameters rejected. Reference buffs remain available, with unresolved stacking.";return fallback;}
    }
}
