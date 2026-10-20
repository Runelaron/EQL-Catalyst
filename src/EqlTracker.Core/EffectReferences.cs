using System.Net;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;
public enum EffectReferenceStatus { Pending, Available, Conflicting, Unavailable }
public sealed record EffectReferenceFixture(string Name,long Revision,string Text) { public DateTime VerifiedAt { get; init; }=DateTime.UnixEpoch; }
public sealed record EffectReferenceSnapshot(string Key, EffectReferenceStatus Status, IReadOnlyList<InspectionLine> Lines, SpellReference? Reference, string Limitation);
public static class EffectReferences
{
    public const int ParserVersion=2;
    public static string Key(string effect)=>Identity.Key(Name(effect));
    public static string Name(string effect)=>Regex.Replace(ItemEffects.Canonical(effect),@"^(?:focus(?:[_ ]effect)?|proc(?:[_ ]effect)?|worn(?:[_ ]effect)?|click(?:[_ ]effect)?):\s*","",RegexOptions.IgnoreCase).Split('(')[0].Trim();
    public static Dictionary<string,string> Fields(string text)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int start=text.IndexOf("{{Spellpage",StringComparison.OrdinalIgnoreCase);if(start<0)return values;
        int depth=0,links=0,valueStart=0;string? field=null;
        for(int i=start;i<text.Length-1;i++)
        {
            if(text.AsSpan(i).StartsWith("{{")){depth++;i++;continue;}
            if(text.AsSpan(i).StartsWith("}}")){depth--;if(depth==0){if(field!=null)values[field]=text[valueStart..i].Trim();break;}i++;continue;}
            if(text.AsSpan(i).StartsWith("[[")){links++;i++;continue;}
            if(text.AsSpan(i).StartsWith("]]")){links--;i++;continue;}
            if(text[i]!='|'||depth!=1||links!=0)continue;
            var match=Regex.Match(text[i..],@"^\|\s*([a-zA-Z_][a-zA-Z_0-9 -]*)\s*=");if(!match.Success)continue;
            if(field!=null)values[field]=text[valueStart..i].Trim();field=match.Groups[1].Value.Trim();valueStart=i+match.Length;i=valueStart-1;
        }
        return values;
    }
    public static string Plain(string value)
    {
        value=Regex.Replace(value,@"\[\[(?:[^\]|]+\|)?([^\]]+)\]\]","$1");
        value=Regex.Replace(value,@"\{\{SpellSlotRow(?:Smart)?\s*\|\s*\d+\s*\|\s*(.*?)(?:\s*\|\s*simple\s*=.*?)?\}\}","$1",RegexOptions.Singleline|RegexOptions.IgnoreCase);
        value=Regex.Replace(value,@"<br\s*/?>|</p>","\n",RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(Regex.Replace(value,@"<[^>]*>|^\s*\*\s*","",RegexOptions.Multiline)).Trim();
    }
    public static EffectReferenceSnapshot Build(string effect,ItemDefinition source,SpellReference? reference,bool unavailable=false)
    {
        var lines=new List<InspectionLine>();string name=Name(effect);
        var activationText=Regex.Match(effect,@"\bat\s+Level\s+(\d+)",RegexOptions.IgnoreCase);
        int? level=source.EffectActivationLevel??(activationText.Success&&int.TryParse(activationText.Groups[1].Value,out int parsed)?parsed:null);
        if(level is { } activation)lines.Add(new("Item activation",$"Level {activation} · source item requirement; socket unlock is separate"));
        if(reference==null)
        {
            if(Regex.IsMatch(effect,@"\d"))lines.Add(new("Item reference",effect));
            return new(name,unavailable?EffectReferenceStatus.Unavailable:EffectReferenceStatus.Pending,lines,null,unavailable?"Numeric spell reference unavailable. Item restrictions remain valid; missing amounts are unknown.":"Loading cached spell reference; numeric amounts are unknown until verified.");
        }
        if(reference.Effects.Length>0)lines.Add(new("Base effects",reference.Effects));
        else if(reference.Description.Length>0)lines.Add(new("Base description",reference.Description));
        foreach(string key in new[]{"duration","target_type","resist","casting_time","recast_time"})if(reference.Fields.GetValueOrDefault(key) is {Length:>0} value)lines.Add(new(key.Replace('_',' '),value));
        if(reference.Fields.Count==0&&reference.Details.Length>0)lines.Add(new("Base details",reference.Details));
        var interval=Regex.Match(reference.Description,@"every\s+(\d+)\s+seconds",RegexOptions.IgnoreCase);
        if(interval.Success)lines.Add(new("Tick interval",interval.Groups[1].Value+" seconds · source reference"));
        if(reference.Name.Equals("Swarming Pain",StringComparison.OrdinalIgnoreCase)&&reference.Effects.Contains("50 per tick")&&reference.Fields.GetValueOrDefault("duration","").Contains("4 ticks"))lines.Add(new("Base reference maximum","50 × 4 = 200 HP if all four ticks apply; not a Legends rank-adjusted prediction"));
        bool conflict=reference.Limitation.StartsWith("Conflicting",StringComparison.OrdinalIgnoreCase)||reference.Effects.Contains("{{")||reference.Effects.Contains("#if",StringComparison.OrdinalIgnoreCase);
        lines.Add(new("Context scaling","No verified Legends rank/focus adjustment is applied to this base reference."));
        return new(name,conflict?EffectReferenceStatus.Conflicting:EffectReferenceStatus.Available,lines,reference,(conflict?"Reference values are unresolved. ":"")+reference.Limitation);
    }
}
