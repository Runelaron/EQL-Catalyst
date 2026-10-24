using System.Net;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal enum AcquisitionRelationshipKind { RelatedQuest, Reward, TurnIn, Purchase }
internal sealed record QuestAcquisitionLink(string Target, string Name, string Url);
internal sealed record PublicWikiPage(string Title, string Text, long Revision);
internal sealed class AcquisitionEvidenceRecord
{
    public string Key { get; set; } = "";
    public List<ItemSource> Sources { get; set; } = [];
}
internal static class ItemAcquisitionEvidence
{
    internal const string Version = "acquisition-v3";
    internal static string Plain(string text) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(Regex.Replace(text,
        @"\[\[(?:[^]|]+\|)?([^]]+)\]\]", "$1"), @"<[^>]+>|'{2,}", " "), @"\s+", " ")).Trim();
    internal static IReadOnlyList<QuestAcquisitionLink> Related(string text) => Regex.Matches(WikiTemplateFields.Value(text,"relatedquests"),@"\[\[([^]|]+)(?:\|([^]]+))?\]\]")
        .Select(m => new QuestAcquisitionLink(m.Groups[1].Value.Split('#')[0].Replace('_',' ').Trim(),
            m.Groups[2].Success ? Plain(m.Groups[2].Value) : m.Groups[1].Value.Split('#')[0].Replace('_',' ').Trim(),
            "https://eqlwiki.com/"+Uri.EscapeDataString(m.Groups[1].Value.Split('#')[0].Replace(' ','_'))+(m.Groups[1].Value.Contains('#')?"#"+Uri.EscapeDataString(m.Groups[1].Value.Split('#')[1].Replace(' ','_')):"")))
        .DistinctBy(l => (l.Target,l.Name)).ToArray();

    internal static void ParsePurchases(ItemDefinition item, string text, long revision)
    {
        string field = WikiTemplateFields.Value(text,"soldby");
        if (field.Length == 0) field = WikiTemplateFields.Value(text,"sold_by");
        string zone = "Location undocumented";
        foreach(Match row in Regex.Matches(field,@"\{\{ItemWhereRow[^}]*\}\}",RegexOptions.IgnoreCase))
        {
            var links=Regex.Matches(row.Value,@"\[\[([^]|]+)(?:\|[^]]+)?\]\]").Select(m=>m.Groups[1].Value.Replace('_',' ')).Where(n=>!WikiContentQuality.Placeholder(n)).ToArray();
            if(links.Length<2)continue;
            item.Sources.Add(new(){Method="Purchase",Zone=links[0],Mob=links[1],Revision=revision,WikiUrl=item.WikiUrl,EvidenceId=Version+":purchase:"+Identity.Key(links[0]+" "+links[1]),Availability="Wiki documented",Era=item.IntroducedExpansion,Notes="Documented vendor table"});
        }
        foreach (string line in field.Split('\n'))
        {
            if(line.Contains("{{ItemWhereRow",StringComparison.OrdinalIgnoreCase))continue;
            var links = Regex.Matches(line,@"\[\[([^]|]+)(?:\|([^]]+))?\]\]").Select(m=>m.Groups[1].Value.Replace('_',' ')).Where(n=>!WikiContentQuality.Placeholder(n)).ToArray();
            if (links.Length == 0) continue;
            if (!line.TrimStart().StartsWith('*')) { zone = links[0]; continue; }
            foreach (string actor in links) item.Sources.Add(new() { Method="Purchase",Mob=actor,Zone=zone,Revision=revision,WikiUrl=item.WikiUrl,
                EvidenceId=Version+":purchase:"+Identity.Key(zone+" "+actor),Availability="Wiki documented",Era=item.IntroducedExpansion,
                Notes=Plain(line.TrimStart('*',' ')).Replace(actor,"",StringComparison.Ordinal).Trim(' ',':','-') });
        }
    }

    internal static ItemSource? Resolve(ItemDefinition item, QuestAcquisitionLink link, PublicWikiPage page)
    {
        string itemKey = Identity.Key(item.Name);
        bool ItemReference(string value) => Regex.Matches(value,@"(?:\[\[|\{\{:)\s*([^]|}]+)").Any(m=>Identity.Key(m.Groups[1].Value)==itemKey);
        bool RewardReference(string value) => ItemReference(value) || Regex.Matches(value,@"(?m)^\|\s*([^\r\n|{}]+)").Any(m=>Identity.Key(m.Groups[1].Value)==itemKey);
        bool reward = false; string conditions = "";
        // A Plane of Sky test row has an explicit reward in its first cell, then its test name.
        foreach (string row in Regex.Split(page.Text,@"(?m)^\|-.*$"))
        {
            var cells = Regex.Split(row,@"(?m)^\|(?![}|-])").Where(c=>c.Trim().Length>0).ToArray();
            if (cells.Length < 2 || !ItemReference(cells[0]) || !Plain(cells[1]).Equals(link.Name,StringComparison.OrdinalIgnoreCase)) continue;
            reward=true;conditions="Documented reward in the test's reward column.";break;
        }
        // Structured quest templates declare reward fields; requirements/related links do not.
        foreach (string field in new[]{"reward","rewards","reward_item","rewarditem"})
            if(RewardReference(WikiTemplateFields.Value(page.Text,field))) {reward=true;conditions="Documented quest reward field.";}
        // Some walkthroughs use an explicit Rewards section rather than a template.
        var section=Regex.Match(page.Text,@"(?ims)^={1,4}\s*(?:Quest\s+)?Rewards?\s*={1,4}\s*$(?<body>.*?)(?=^={1,4}[^=]|\z)");
        if(section.Success && RewardReference(section.Groups["body"].Value)) {reward=true;conditions="Documented Rewards section (including named Gear Set entries).";}
        foreach(Match statement in Regex.Matches(page.Text,@"(?im)\b(?:receive|rewarded with|rewards? you with|hands? you|gives? you)\b(?<result>[^.\r\n]{0,300})"))
            if(ItemReference(statement.Groups["result"].Value)) {reward=true;conditions="Explicit walkthrough output: "+Plain(statement.Value);break;}
        if(!reward)return null;
        var start=QuestStartEvidence.Parse(page.Title,page.Text,page.Revision);
        string zone=page.Title.Equals("Plane of Sky",StringComparison.OrdinalIgnoreCase) ? "Plane of Sky" : start.StartZone;
        return new() { Method="Quest",Mob=link.Name,Zone=zone.Length>0?zone:"Starting zone undocumented",Availability="Wiki documented",
            Revision=page.Revision,WikiUrl=link.Url,EvidenceId=Version+":reward:"+Identity.Key(link.Name),Era=item.IntroducedExpansion,
            Notes=conditions+" Reward relationship verified separately from the item's Related quests link. "+(start.StartNpc.Length>0?"Starts with "+start.StartNpc+". ":"")+"Source revision "+page.Revision+"; "+AcquisitionRelationshipKind.Reward };
    }
    internal static void Apply(Catalog catalog,string path)
    {
        if(!File.Exists(path))return;
        foreach(var record in Json.Read<List<AcquisitionEvidenceRecord>>(path))
        {
            var item=catalog.FindKey(record.Key);if(item==null)continue;
            item.Sources=item.Sources.Concat(record.Sources).DistinctBy(s=>(s.EvidenceId,s.Zone,s.Mob,s.Method,s.Notes)).ToList();
        }
    }
}
