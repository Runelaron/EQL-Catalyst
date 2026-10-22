using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal sealed record QuestInstructionBlock(QuestStep Step, string Heading, string Instruction, bool Prerequisite);
internal sealed record QuestSourceBlock(ItemDefinition Item, IReadOnlyList<ItemSource> Evidence, IReadOnlyList<string> Additional)
{
    internal ItemSource Source => Evidence[0];
    internal string Method => AcquisitionPresentation.Method(Source);
    public string Actor => Source.Mob.Length>0 && !Regex.IsMatch(Source.Mob,@"^(Give|Make|Combine|Kill|Loot)\b",RegexOptions.IgnoreCase) ? Source.Mob : "Source not documented";
    internal string Zone => Source.Zone.Length>0 && !Source.Zone.Equals("See walkthrough",StringComparison.OrdinalIgnoreCase) ? Source.Zone : "Zone not documented";
}
internal sealed record ProgressPresentation(IReadOnlyList<QuestInstructionBlock> Instructions, IReadOnlyList<QuestSourceBlock> Sources)
{
    internal static ProgressPresentation Build(QuestNode node, QuestDefinition? quest, CharacterProfile profile, Catalog catalog, bool allRoutes=false)
    {
        var detail=allRoutes&&node.Kind==QuestNodeKind.Quest&&quest!=null ? new QuestAcquisitionDetail(node.Kind,QuestPresentation.RewardKeys(quest).ToArray(),quest.Steps,Array.Empty<QuestStep>()) : QuestAcquisition.Describe(node,quest,profile);
        var sources=SourcesFor(detail.ItemKeys,catalog).ToList();
        var instructions=new List<QuestInstructionBlock>();
        void Add(QuestStep step,bool prerequisite)
        {
            var parts=QuestPresentation.Instruction(quest,step);
            string heading=QuestPresentation.Normalize(parts.Heading)==QuestPresentation.Normalize(node.Name)?"":parts.Heading;
            string body=parts.Instruction;
            if(node.Kind!=QuestNodeKind.Quest && !prerequisite)
                for(int i=0;i<sources.Count;i++) if(CoversDrop(body,sources[i])||AcquisitionPresentation.CoversPickup(body,sources[i]))
                {
                    sources[i]=sources[i] with{Additional=sources[i].Additional.Concat(Qualifiers(body)).Distinct().ToArray()};body="";heading="";break;
                }
            instructions.Add(new(step,heading,body,prerequisite));
        }
        foreach(var step in detail.Steps) Add(step,false);
        foreach(var step in detail.Prerequisites) Add(step,true);
        sources=sources.Select(s=>s with{Additional=s.Additional.Where(n=>!instructions.Any(i=>QuestPresentation.Normalize(i.Instruction)==QuestPresentation.Normalize(n))).ToArray()}).ToList();
        return new(instructions,sources);
    }
    internal static IReadOnlyList<QuestSourceBlock> SourcesFor(IEnumerable<string> keys,Catalog catalog)
    {
        var result=new List<QuestSourceBlock>();
        foreach(string key in keys.Distinct())
        {
            var item=catalog.FindKey(key);if(item==null)continue;
            foreach(var group in AcquisitionPresentation.Routes(item.Sources))
            {
                var evidence=group.OrderByDescending(s=>AcquisitionPresentation.Method(s)=="Ground spawn"&&s.Method.Equals("Ground spawn",StringComparison.OrdinalIgnoreCase)).ToArray();var block=new QuestSourceBlock(item,evidence,[]);
                result.Add(block with{Additional=AcquisitionPresentation.Details(block)});
            }
        }
        return result;
    }
    internal static bool CoversDrop(string instruction,QuestSourceBlock route)
    {
        if(instruction.Length==0 || !route.Method.Equals("Drop",StringComparison.OrdinalIgnoreCase) || route.Source.Mob.Length==0 || route.Source.Zone.Length==0)return false;
        // These complete, anchored forms encode only the source facts. Extra actions,
        // quantities, alternatives or requirements fail closed and remain visible.
        var m=Regex.Match(instruction.Trim(),@"^(?:Kill|Defeat)\s+(?<mob>.+?)\s+in\s+(?<zone>.+?)[,.;]\s*(?:and\s+)?(?:loot|receive)\s+(?<item>.+?)[.!]?$",RegexOptions.IgnoreCase);
        if(!m.Success)m=Regex.Match(instruction.Trim(),@"^(?<zone>.+?)\s+-\s+(?:Kill|Defeat)\s+(?<mob>.+?)\s+for\s+(?<item>.+?)[.!]?$",RegexOptions.IgnoreCase);
        if(!m.Success)return false;
        string CleanMob(string text)=>QuestPresentation.Normalize(Regex.Replace(Regex.Replace(text,@"^\[\d+\]\s*",""),@"\s*\(raid\)\s*"," ",RegexOptions.IgnoreCase));
        return CleanMob(m.Groups["mob"].Value)==CleanMob(route.Source.Mob)
            && Identity.ZoneKey(m.Groups["zone"].Value)==Identity.ZoneKey(route.Source.Zone)
            && route.Item.Aliases.Prepend(route.Item.Name).Any(n=>QuestPresentation.Normalize(n)==QuestPresentation.Normalize(m.Groups["item"].Value));
    }
    private static IEnumerable<string> Qualifiers(string text)
    {
        if(Regex.IsMatch(text,@"\braid\b",RegexOptions.IgnoreCase)) yield return "Raid target";
        var level=Regex.Match(text,@"\[(\d+)\]");if(level.Success)yield return "Mob level: "+level.Groups[1].Value;
    }
}
