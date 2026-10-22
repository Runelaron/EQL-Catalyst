using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed record QuestStartEvidence(string Name, long Revision, string StartZone, string StartNpc, string Source)
{
    public static QuestStartEvidence Parse(string name, string text, long revision)
    {
        string Read(string label)
        {
            var row=Regex.Match(text,@"(?im)^![ \t']*(?:"+label+@")\s*:?\s*'*\s*\r?\n\|\s*(?<value>[^\r\n]+)");
            if(!row.Success)row=Regex.Match(text,@"(?im)^\|\s*(?:"+label.Replace(" ","[ _]?")+@")\s*=\s*(?<value>[^\r\n]+)");
            string value=row.Groups["value"].Value;
            value=Regex.Replace(value,@"\[\[([^]|]+)(?:\|([^]]+))?\]\]",m=>m.Groups[2].Success?m.Groups[2].Value:m.Groups[1].Value);
            return Regex.Replace(value,@"<[^>]*>|'{2,}","").Trim();
        }
        return new(name,revision,Read("Start Zone|Starting Zone"),Read("Quest Giver|Start NPC|Starting NPC"),"https://eqlwiki.com/"+Uri.EscapeDataString(name.Replace(' ','_'))+"?oldid="+revision+" · structured quest-start fields · parser v1");
    }
    public static void Apply(Catalog catalog,string path)
    {
        if(!File.Exists(path))return;
        foreach(var start in Json.Read<List<QuestStartEvidence>>(path))
        {
            var quest=catalog.Quests.FirstOrDefault(q=>q.Name.Equals(start.Name,StringComparison.OrdinalIgnoreCase));
            if(quest==null||start.Revision<quest.Revision)continue;
            quest.StartRevision=start.Revision;quest.StartZone=start.StartZone;quest.StartNpc=start.StartNpc;quest.StartEvidence=start.Source;
        }
    }
}
