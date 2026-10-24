namespace EqlTracker.Core;

internal sealed class NamedNpcRecord
{
    public string Name {get;set;}="";
    public string Zone {get;set;}="";
    public List<string> Aliases {get;set;}=[];
    public string Url {get;set;}="";
    public long Revision {get;set;}
    public string Timestamp {get;set;}="";
    public string ClassificationEvidence {get;set;}="";
}
internal sealed class NamedNpcDocument
{
    public string Version {get;set;}="named-npcs-v2";
    public List<NamedNpcRecord> Records {get;set;}=[];
}
internal sealed class NamedNpcIndex
{
    private readonly HashSet<string> names=[];
    internal string Version {get;}
    internal NamedNpcIndex(NamedNpcDocument document)
    {
        Version=document.Version+":"+PetComparisonEngine.Key(document.Records);
        foreach(var r in document.Records.Where(r=>r.Name.Length>0&&r.Zone.Length>0&&r.Revision>0&&r.Url.Length>0&&r.ClassificationEvidence.Length>0))
            foreach(string name in r.Aliases.Prepend(r.Name))names.Add(Key(r.Zone,name));
    }
    private static string Key(string zone,string name)=>Identity.ZoneKey(zone)+"|"+CombatIdentity.Key(name);
    internal bool Matches(string zone,string name)=>names.Contains(Key(zone,name));
    internal bool Matches(Encounter fight)=>fight.Events.Where(e=>!e.ActivityOnly&&e.Owner is "Player" or "Pet").Any(e=>Matches(fight.Zone,e.Target));
}

internal static class NamedNpcClassification
{
    internal static string Evidence(string text)
    {
        foreach(string field in new[]{"named","is_named","rare_spawn","is_rare"})
        {
            string value=ItemAcquisitionEvidence.Plain(WikiTemplateFields.Value(text,field)).ToLowerInvariant();
            if(value is "yes" or "true" or "1" or "named" or "rare")return "Explicit "+field+" field";
            if(value is "no" or "false" or "0")return "";
        }
        if(System.Text.RegularExpressions.Regex.IsMatch(text,@"\[\[Category:\s*(?:Named (?:NPCs|Mobs|Creatures)|Raid (?:Targets|Bosses)|Bosses)\s*\]\]",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return "Explicit named/boss category";
        string description=ItemAcquisitionEvidence.Plain(WikiTemplateFields.Value(text,"description"));
        if(System.Text.RegularExpressions.Regex.IsMatch(description,@"\b(?:not|isn['’]t)\s+(?:a\s+)?(?:named|rare)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return "";
        return System.Text.RegularExpressions.Regex.IsMatch(description,@"\b(?:rare(?:\s+named)?|named)\s+(?:spawn|mob|npc|creature|monster|enemy)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase)?"Description explicitly identifies a named or rare spawn":"";
    }
}
