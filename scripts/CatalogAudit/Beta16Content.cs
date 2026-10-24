using System.Text.Json;
using System.Text.RegularExpressions;
using EqlTracker.Core;

internal static class Beta16Content
{
    internal static void Run(string source,string cache,string output)
    {
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        using var doc=JsonDocument.Parse(File.ReadAllText(cache));var root=doc.RootElement;
        var pages=root.GetProperty("pages").EnumerateObject().GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.MaxBy(p=>p.Value.GetProperty("revision").GetInt64())).ToDictionary(p=>p.Name,p=>new PublicWikiPage(p.Name,p.Value.GetProperty("text").GetString()!,p.Value.GetProperty("revision").GetInt64()),StringComparer.OrdinalIgnoreCase);
        var redirects=root.GetProperty("redirects").EnumerateObject().GroupBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Last().Value.GetString()!,StringComparer.OrdinalIgnoreCase);
        string Canonical(string title){var seen=new HashSet<string>();while(redirects.TryGetValue(title,out var target)&&seen.Add(title))title=target;return title;}
        string Plain(string t)=>ItemAcquisitionEvidence.Plain(t);
        IEnumerable<string> Links(string t)=>Regex.Matches(t,@"\[\[(?:[^]|]*:)?([^]|]+)(?:\|[^]]+)?\]\]|\{\{:\s*([^}|]+)").Select(m=>(m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value).Split('#')[0].Replace('_',' ').Trim()).Where(t=>t.Length>0&&!WikiContentQuality.Placeholder(t)).Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase);
        string Field(PublicWikiPage p,string field)=>WikiTemplateFields.Value(p.Text,field);
        string Stamp(string title)=>root.GetProperty("pages").GetProperty(title).GetProperty("timestamp").GetString()!;
        string Url(PublicWikiPage p)=>"https://eqlwiki.com/"+Uri.EscapeDataString(p.Title.Replace(' ','_'))+"?oldid="+p.Revision;
        var catalog=Json.Read<Catalog>(Path.Combine(source,"data/catalog.json"));var snapshot=Json.Read<EquipmentSnapshot>(Path.Combine(source,"data/equipment.json"));
        var baseKeys=catalog.Items.Select(i=>i.Key).ToHashSet();var equipmentKeys=snapshot.Items.Select(i=>i.Key).ToHashSet();var all=new Catalog{Items=catalog.Items.ToList()};CatalogItemMerge.Apply(all,snapshot.Items);
        int before=all.Items.Count,missingBefore=all.Items.Count(i=>i.Sources.Count==0);var originalSources=all.Items.ToDictionary(i=>i.Key,i=>i.Sources.Count);
        var parsed=pages.Values.Where(p=>p.Text.Contains("{{Itempage",StringComparison.OrdinalIgnoreCase)&&!WikiContentQuality.Placeholder(p.Title)).Select(p=>WikiClient.ParseItem(p.Title,p.Text,p.Revision)).ToArray();
        foreach(var item in parsed)
        {
            var page=pages[Uri.UnescapeDataString(new Uri(item.WikiUrl).AbsolutePath.Trim('/')).Replace('_',' ')];
            foreach(var link in ItemAcquisitionEvidence.Related(page.Text))if(pages.TryGetValue(Canonical(link.Target),out var quest)&&ItemAcquisitionEvidence.Resolve(item,link,quest) is {} evidence)item.Sources.Add(evidence);
            foreach(var alias in redirects.Where(r=>Canonical(r.Key).Equals(page.Title,StringComparison.OrdinalIgnoreCase)).Select(r=>r.Key))if(!WikiContentQuality.Placeholder(alias)&&!item.Aliases.Contains(alias))item.Aliases.Add(alias);
        }
        CatalogItemMerge.Apply(all,parsed);
        var named=new NamedNpcDocument{Version="named-npcs-v2"};int npcSources=0;int unclassifiedNpcs=0;
        var notable=new Dictionary<string,PublicWikiPage>(StringComparer.OrdinalIgnoreCase);
        foreach(var zonePage in pages.Values.Where(p=>p.Text.Contains("zoneTopTable",StringComparison.OrdinalIgnoreCase)))
        {
            var row=Regex.Match(zonePage.Text,@"(?im)^![^\r\n]*(?:Notable NPCs|Named NPCs|Named Mobs)[^\r\n]*\r?\n\|\s*(?<body>[^\r\n]+)");
            foreach(string actor in Links(row.Groups["body"].Value))notable[Identity.ZoneKey(zonePage.Title)+"|"+CombatIdentity.Key(actor)]=zonePage;
        }
        foreach(var p in pages.Values.Where(p=>p.Text.Contains("{{Namedmobpage",StringComparison.OrdinalIgnoreCase)))
        {
            string name=Plain(Field(p,"name"));if(name.Length==0)name=p.Title;if(WikiContentQuality.Placeholder(name))continue;
            var zones=Links(Field(p,"zone")).ToArray();if(zones.Length==0){string zone=Plain(Field(p,"zone"));if(zone.Length>0&&!WikiContentQuality.Placeholder(zone))zones=[zone];}
            foreach(string zone in zones)
            {
                string proof=NamedNpcClassification.Evidence(p.Text);
                if(proof.Length==0&&notable.TryGetValue(Identity.ZoneKey(zone)+"|"+CombatIdentity.Key(name),out var listing))proof="Explicit zone notable/named NPC list · "+Url(listing);
                if(proof.Length>0)named.Records.Add(new(){Name=name,Zone=zone,Url=Url(p),Revision=p.Revision,Timestamp=Stamp(p.Title),ClassificationEvidence=proof,Aliases=redirects.Where(r=>Canonical(r.Key).Equals(p.Title,StringComparison.OrdinalIgnoreCase)).Select(r=>r.Key).Append(p.Title).Distinct().ToList()});else unclassifiedNpcs++;
                foreach(string loot in Links(Field(p,"known_loot")).Concat(Links(Field(p,"common_loot"))).Distinct())
                {
                    var item=all.Find(loot);if(item==null)continue;
                    string id=ItemAcquisitionEvidence.Version+":npc-drop:"+Identity.Key(zone+" "+name);
                    if(item.Sources.Any(s=>s.EvidenceId==id&&s.WikiUrl==Url(p)))continue;
                    item.Sources.Add(new(){Method="Drop",Zone=zone,Mob=name,Revision=p.Revision,WikiUrl=Url(p),EvidenceId=id,Availability="Wiki documented",Era=item.IntroducedExpansion,Notes="Explicit NPC known/common loot list. Same wiki as item evidence; not independent confirmation."});npcSources++;
                }
            }
        }
        var zonePages=root.GetProperty("zoneConnections").EnumerateArray().Select(v=>v.GetString()!).Concat(new[]{"Hate, Betrayal, and Trust","The Tragic Tale of House Everling"}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int addedQuests=0;var starts=new List<QuestStartEvidence>();
        foreach(var page in pages.Values.Where(p=>zonePages.Contains(p.Title)&&p.Text.Contains("questTopTable",StringComparison.OrdinalIgnoreCase)))
        {
            var start=QuestStartEvidence.Parse(page.Title,page.Text,page.Revision);starts.Add(start);
            var old=catalog.Quests.FirstOrDefault(q=>q.Name.Equals(page.Title,StringComparison.OrdinalIgnoreCase));
            var q=old??new QuestDefinition{Id="nektropos-"+Regex.Replace(Identity.Key(page.Title),@"[^a-z0-9]+","-").Trim('-'),Name=page.Title,Class="ALL",EligibleClasses=["ALL"],Kind="Quest",Expansion="Classic"};
            q.Revision=page.Revision;q.StartRevision=page.Revision;q.StartZone=start.StartZone;q.StartNpc=start.StartNpc;q.StartEvidence=start.Source+" · Classic classification inferred from current content";q.WikiUrl=Url(page);q.Coverage="Partial documented checklist";q.ChecklistCoverage="Partial; source contains unfinished content";
            var summary=Regex.Match(page.Text,@"(?ims)^={1,4}\s*Summary\s*={1,4}\s*$(?<body>.*?)(?=^={1,4}[^=]|\z)");
            var steps=new List<QuestStep>();
            foreach(string line in summary.Groups["body"].Value.Split('\n').Where(l=>l.TrimStart().StartsWith('*')))
            {
                string instruction=Plain(line.TrimStart('*',' '));if(instruction.Length==0)continue;
                if(instruction.Contains("???")){if(!steps.Any(s=>s.Name=="Further steps undocumented"))steps.Add(new(){Id=q.Id+":unknown",Name="Further steps undocumented",Instructions="Remaining steps are not documented in this source revision.",WikiUrl=q.WikiUrl,Revision=page.Revision});continue;}
                var step=new QuestStep{Id=q.Id+":"+PetComparisonEngine.Key(instruction)[..12],Name=instruction,Instructions=instruction,WikiUrl=q.WikiUrl,Revision=page.Revision,Kind=RecipeKind.Quest};
                if(Regex.IsMatch(instruction,@"\b(?:give|hand|deliver|turn in)\b",RegexOptions.IgnoreCase))
                    foreach(string title in Links(line))if(all.Find(title) is {} item){step.Inputs.Add(new(){ItemKey=item.Key});baseKeys.Add(item.Key);}
                steps.Add(step);
            }
            if(steps.Count==0)steps.Add(new(){Id=q.Id+":overview",Name="Walkthrough incomplete",Instructions="Quest identity and starting information are documented; complete steps remain unavailable.",WikiUrl=q.WikiUrl,Revision=page.Revision});
            q.Steps=steps;q.Requirements=steps.SelectMany(s=>s.Inputs).GroupBy(i=>i.ItemKey).Select(g=>new ItemQuantity{ItemKey=g.Key,Count=g.Max(i=>i.Count)}).ToList();
            if(old==null){catalog.Quests.Add(q);addedQuests++;}
        }
        foreach(var p in pages.Values)
            if(!catalog.Documents.Any(d=>d.Title==p.Title&&d.Revision==p.Revision))catalog.Documents.Add(new(){Title=p.Title,Url=Url(p),Revision=p.Revision,Timestamp=Stamp(p.Title)});
        catalog.Items=all.Items.Where(i=>baseKeys.Contains(i.Key)).ToList();catalog.SchemaVersion=Math.Max(16,catalog.SchemaVersion);catalog.Zones=catalog.Zones.Concat(new[]{"Lake Nerius","Nektropos Castle","Nektropos Undercroft"}).Distinct().ToList();
        snapshot.Items=all.Items.Where(i=>equipmentKeys.Contains(i.Key)||parsed.Any(p=>p.Key==i.Key)).ToList();snapshot.Titles=root.GetProperty("indexes").GetProperty("Itempage").EnumerateArray().Select(v=>v.GetString()!).ToList();snapshot.GeneratedAt=DateTime.UtcNow;
        var existingStarts=Json.Read<List<QuestStartEvidence>>(Path.Combine(source,"data/quest-starts.json"));existingStarts=existingStarts.Where(s=>!starts.Any(n=>n.Name==s.Name)).Concat(starts).ToList();
        var camel=new JsonSerializerOptions(Json.Options){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        File.WriteAllText(Path.Combine(source,"data/catalog.json"),JsonSerializer.Serialize(catalog,camel));
        File.WriteAllText(Path.Combine(source,"data/equipment.json"),JsonSerializer.Serialize(new Dictionary<string,object>{{"Version",snapshot.Version},{"GeneratedAt",snapshot.GeneratedAt},{"Titles",snapshot.Titles},{"items",snapshot.Items}},camel));
        Json.Write(Path.Combine(source,"data/named-npcs.json"),named);Json.Write(Path.Combine(source,"data/quest-starts.json"),existingStarts);
        var dispositions=all.Items.OrderBy(i=>i.Key).Select(i=>new{i.Key,i.Name,i.MetadataComplete,Template=WikiContentQuality.TemplateRecord(i),BeforeSources=originalSources.GetValueOrDefault(i.Key),AfterSources=i.Sources.Count,Status=i.Sources.Count>0?WikiContentQuality.TemplateRecord(i)?"Documented acquisition; item fields are an unfilled template":"Documented acquisition available":pages.Values.Any(p=>p.Title==i.Name)?"Reviewed source has no proven acquisition":"No bundled acquisition; unchanged or on-demand page not re-reviewed",Sources=i.Sources.Select(s=>new{s.Method,s.Mob,s.Zone,s.WikiUrl,s.Revision})}).ToArray();
        Json.Write(Path.Combine(output,"coverage.json"),new{BundledBefore=before,BundledAfter=all.Items.Count,MissingAcquisitionBefore=missingBefore,MissingAcquisitionAfter=all.Items.Count(i=>i.Sources.Count==0),ParsedPages=parsed.Length,AddedQuests=addedQuests,NamedRecords=named.Records.Count,NpcLootRelationships=npcSources,Items=dispositions,MissingPublicPages=root.GetProperty("missing").EnumerateArray().Select(v=>v.GetString()).ToArray(),Attribution="EQLWiki CC BY-SA 4.0; per-source revision URLs retained. Unfilled templates and unknown values excluded. Same-source lists are not independent corroboration."});
        Console.WriteLine(JsonSerializer.Serialize(new{ParsedItems=parsed.Length,AddedQuests=addedQuests,NamedRecords=named.Records.Count,UnclassifiedNpcs=unclassifiedNpcs,NpcLootRelationships=npcSources,BundledItems=all.Items.Count,IncompleteTemplates=parsed.Count(WikiContentQuality.TemplateRecord),MissingBefore=missingBefore,MissingAfter=all.Items.Count(i=>i.Sources.Count==0)}));
    }
}
