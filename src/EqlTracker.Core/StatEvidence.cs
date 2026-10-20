using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed class AaRankEvidence
{
    public string Name { get; set; } = "";
    public int? Rank { get; set; }
    public int MinimumRank { get; set; } = 1;
    public DateTime At { get; set; }
    public string Evidence { get; set; } = "";
    public string LogId { get; set; } = "";
}
public sealed class EffectEvidence
{
    public string Owner { get; set; } = "Self";
    public string Actor { get; set; } = "";
    public List<string> Spells { get; set; } = [];
    public string State { get; set; } = "Observed landing · current status unknown";
    public DateTime At { get; set; }
    public string Evidence { get; set; } = "";
    public string LogId { get; set; } = "";
    public string Key => Owner+":"+Actor+":"+string.Join('|',Spells.Order());
    public string Name => Spells.Count == 1 ? Spells[0] : "Ambiguous: "+string.Join(" / ",Spells);
}
public sealed class StatEvidenceState
{
    public long Revision { get; set; }
    public int? Level { get; set; }
    public DateTime? LevelAt { get; set; }
    public List<string> Classes { get; set; } = [];
    public DateTime? ClassesAt { get; set; }
    public Dictionary<string, AaRankEvidence> AAs { get; set; } = [];
    public List<EffectEvidence> Effects { get; set; } = [];
    public string SpellSource { get; set; } = "Spell messages unavailable";
    public bool CaughtUp { get; set; }
    public string Status { get; set; } = "Waiting for character log";
}

// Only names and client-authored landing/fade messages are read. No spell-effect
// numeric columns, duration formulas, stacking rules, or active buff roster is inferred.
public sealed class SpellMessageIndex
{
    private readonly Dictionary<string,List<string>> self = new(StringComparer.Ordinal), other = new(StringComparer.Ordinal), fades = new(StringComparer.Ordinal);
    public static SpellMessageIndex Parse(IEnumerable<string> spellLines, IEnumerable<string> messageLines)
    {
        var names = new Dictionary<int,string>();
        foreach (string line in spellLines) { int a=line.IndexOf('^'),b=a<0?-1:line.IndexOf('^',a+1);if(a>0 && b>a && int.TryParse(line[..a],out int id))names[id]=line[(a+1)..b]; }
        var result=new SpellMessageIndex();bool header=false;
        foreach(string line in messageLines)
        {
            if(line.StartsWith("#SPELLINDEX^CASTERMETXT^CASTEROTHERTXT^CASTEDMETXT^CASTEDOTHERTXT^SPELLGONE^",StringComparison.Ordinal)){header=true;continue;}
            if(!header)continue;var f=line.Split('^');if(f.Length<6 || !int.TryParse(f[0],out int id) || !names.TryGetValue(id,out string? name))continue;
            Add(result.self,f[3],name);Add(result.other,f[4],name);Add(result.fades,f[5],name);
        }
        return result;
        static void Add(Dictionary<string,List<string>> map,string message,string name)
        { if(message.Length==0)return;if(!map.TryGetValue(message,out var list))map[message]=list=[];if(!list.Contains(name))list.Add(name); }
    }
    internal List<string>? Landing(string text,bool pet) => (pet?other:self).GetValueOrDefault(text);
    internal List<string>? Fade(string text) => fades.GetValueOrDefault(text);
}

public static partial class StatEvidenceReader
{
    [GeneratedRegex("^You have improved (?<name>.+) (?<rank>[0-9]+) at a cost of [0-9]+ ability points?\\.$")] private static partial Regex Improved();
    [GeneratedRegex("^You have gained the ability \"(?<name>[^\"]+)\" at a cost of [0-9]+ ability points?\\.$")] private static partial Regex Learned();
    [GeneratedRegex("^\\[(?<level>[0-9]+) (?<classes>[A-Z]+(?:/[A-Z]+){0,2})\\] (?<name>[^ ]+)(?: |$)")] private static partial Regex Who();
    public static bool Observe(StatEvidenceState state,string line,string id,string character,IReadOnlyList<PetIdentity> pets,SpellMessageIndex? spells)
    {
        int end=line.IndexOf("] ",StringComparison.Ordinal);if(end<0)return false;string message=line[(end+2)..];
        var aa=message.StartsWith("You have improved ",StringComparison.Ordinal)?Improved().Match(message):Learned().Match(message);
        var who=message.StartsWith('[')?Who().Match(message):null;
        const string welcome="You have gained a level! Welcome to level ";int? gainedLevel=message.StartsWith(welcome,StringComparison.Ordinal) && int.TryParse(message[welcome.Length..].TrimEnd('!'),out int level) && level is >=1 and <=100?level:null;
        string owner="Self",actor=character;List<string>? effects=null;bool faded=false;
        string petAt(DateTime at)=>pets.Where(p=>p.From<=at && (p.Until==null || at<p.Until)).MaxBy(p=>p.From)?.Name ?? "Pet (identity unknown)";
        bool namedFade=message.StartsWith("Your ",StringComparison.Ordinal) && message.EndsWith(" spell has worn off.",StringComparison.Ordinal);
        if(namedFade){faded=true;bool pet=message.StartsWith("Your pet's ",StringComparison.Ordinal);owner=pet?"Pet":"Self";effects=[message[(pet?11:5)..^20]];}
        if(effects==null && spells!=null)
        {
            effects=spells.Landing(message,false);if(effects==null){effects=spells.Fade(message);faded=effects!=null;}
            if(effects==null)
            {
                int space=message.IndexOf(' ');int possessive=message.IndexOf('\'');int cut=possessive>0 && (space<0 || possessive<space)?possessive:space;
                if(cut>0){string name=message[..cut];if(pets.Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))){effects=spells.Landing(message[cut..],true);if(effects!=null){owner="Pet";actor=name;}}}
            }
        }
        if(!aa.Success && gainedLevel==null && (who==null || !who.Success || !who.Groups["name"].Value.Equals(character,StringComparison.OrdinalIgnoreCase)) && effects==null)return false;
        if(!DateTime.TryParseExact(line[1..end],["ddd MMM dd HH:mm:ss yyyy","ddd MMM d HH:mm:ss yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var at))return false;
        at=DateTime.SpecifyKind(at,DateTimeKind.Local);bool changed=false;
        if(aa.Success)
        {
            string name=aa.Groups["name"].Value,key=Identity.Key(name);int? rank=int.TryParse(aa.Groups["rank"].Value,out int r)?r:null;
            if(rank is <1 or >100)return false;
            if(!state.AAs.TryGetValue(key,out var prior) || at>prior.At || at==prior.At && rank>prior.Rank)
            {state.AAs[key]=new(){Name=name,Rank=rank,At=at,Evidence=message,LogId=id};changed=true;}
        }
        if(who?.Success==true && who.Groups["name"].Value.Equals(character,StringComparison.OrdinalIgnoreCase) && int.TryParse(who.Groups["level"].Value,out int whoLevel) && whoLevel is >=1 and <=100 && (state.ClassesAt==null || at>state.ClassesAt))
        {if(state.LevelAt==null || at>state.LevelAt){state.Level=whoLevel;state.LevelAt=at;}state.Classes=who.Groups["classes"].Value.Split('/').ToList();state.ClassesAt=at;changed=true;}
        if(gainedLevel!=null && (state.LevelAt==null || at>state.LevelAt)){state.Level=gainedLevel;state.LevelAt=at;changed=true;}
        if(effects!=null && effects.Count>0)
        {
            if(owner=="Pet")
            {
                if(actor==character)actor=petAt(at);
                else if(!pets.Any(p=>p.Name.Equals(actor,StringComparison.OrdinalIgnoreCase) && at>=p.From && (p.Until==null || at<p.Until)))return changed;
            }
            string identity=string.Join('|',effects.Order());var prior=state.Effects.FirstOrDefault(e=>e.Owner==owner && e.Actor.Equals(actor,StringComparison.OrdinalIgnoreCase) && string.Join('|',e.Spells.Order())==identity);
            if(prior==null || at>prior.At)
            {
                if(prior!=null)state.Effects.Remove(prior);
                state.Effects.Add(new(){Owner=owner,Actor=actor,Spells=effects.ToList(),At=at,LogId=id,Evidence=message,State=faded?"Wore off · historical evidence":"Observed landing · current status unknown"});
                state.Effects=state.Effects.OrderByDescending(e=>e.At).Take(200).ToList();changed=true;
            }
        }
        if(changed)state.Revision++;return changed;
    }
}
