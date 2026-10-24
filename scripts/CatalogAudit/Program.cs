using System.Text.Json;
using EqlTracker.Core;

if(args.Length==4&&args[0]=="--refresh"){Beta16Content.Run(args[1],args[2],args[3]);return;}
if(args.Length!=3)throw new ArgumentException("Usage: CatalogAudit SOURCE_ROOT WIKI_CACHE OUTPUT_FOLDER");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
var catalog=Json.Read<Catalog>(Path.Combine(root,"data/catalog.json"));var snapshot=Json.Read<EquipmentSnapshot>(Path.Combine(root,"data/equipment.json"));
CatalogItemMerge.Apply(catalog,snapshot.Items);CatalogSlotEvidence.Apply(catalog,Path.Combine(root,"data/catalog-slot-repairs.json"));
using var cache=JsonDocument.Parse(File.ReadAllText(args[1]));var pages=cache.RootElement.GetProperty("pages");var redirects=cache.RootElement.GetProperty("redirects");
PublicWikiPage? Read(string title)
{
    var visited=new HashSet<string>();
    while(redirects.TryGetProperty(title,out var redirect)&&visited.Add(title))title=redirect.GetProperty("to").GetString()!;
    return pages.TryGetProperty(title,out var page)?new(title,page.GetProperty("text").GetString()!,page.GetProperty("revision").GetInt64()):null;
}
var evidence=new List<AcquisitionEvidenceRecord>();var coverage=new List<object>();int totalBefore=0,totalAfter=0;
foreach(var item in catalog.Items.OrderBy(i=>i.Key,StringComparer.Ordinal))
{
    bool before=ItemAcquisition.For(item,catalog,new()).Count>0;if(!before)totalBefore++;
    string title=item.WikiUrl.Length>0?Uri.UnescapeDataString(new Uri(item.WikiUrl).AbsolutePath.Trim('/')).Replace('_',' '):item.Name;
    var page=Read(title);var added=new List<ItemSource>();var related=new List<object>();
    if(page!=null)
    {
        var parsed=WikiClient.ParseItem(page.Title,page.Text,page.Revision);
        foreach(var source in parsed.Sources)
        {
            if(source.Mob.StartsWith("This item",StringComparison.OrdinalIgnoreCase))continue;
            source.Revision=page.Revision;source.WikiUrl=item.WikiUrl;source.EvidenceId=source.EvidenceId.Length>0?source.EvidenceId:ItemAcquisitionEvidence.Version+":drop:"+Identity.Key(source.Zone+" "+source.Mob);
            if(!item.Sources.Any(s=>Identity.ZoneKey(s.Zone)==Identity.ZoneKey(source.Zone)&&AcquisitionPresentation.ActorKey(s.Mob)==AcquisitionPresentation.ActorKey(source.Mob)&&AcquisitionPresentation.Method(s)==AcquisitionPresentation.Method(source)))added.Add(source);
        }
        foreach(var link in ItemAcquisitionEvidence.Related(page.Text))
        {
            var quest=Read(link.Target);var source=quest==null?null:ItemAcquisitionEvidence.Resolve(item,link,quest);
            related.Add(new{link.Name,link.Url,Status=source!=null?"Verified output relationship":quest==null?"Linked document unavailable":"Related quest only; reward not established"});
            if(source!=null)added.Add(source);
        }
    }
    added=added.DistinctBy(s=>(s.Method,s.Zone,s.Mob,s.EvidenceId)).ToList();
    if(added.Count>0){evidence.Add(new(){Key=item.Key,Sources=added});item.Sources.AddRange(added);}
    bool after=ItemAcquisition.For(item,catalog,new()).Count>0;if(!after)totalAfter++;
    coverage.Add(new{item.Key,item.Name,Before=before,After=after,Added=added.Count,Related=related,Status=after?"Documented acquisition available":page==null?"Public item page missing or identity unresolved":ItemAcquisitionEvidence.Related(page.Text).Count>0?"Quest connection documented; output/acquisition not established":"No acquisition documented on reviewed item page",Source=item.WikiUrl,Revision=page?.Revision});
}
Json.Write(Path.Combine(output,"acquisition-evidence.json"),evidence);
Json.Write(Path.Combine(output,"coverage-after.json"),new{BundledItems=catalog.Items.Count,WithoutAcquisitionBefore=totalBefore,WithoutAcquisitionAfter=totalAfter,RepairedItems=evidence.Count,AddedSources=evidence.Sum(r=>r.Sources.Count),Items=coverage,
    IndexedTitles=snapshot.Titles.Order().Select(title=>new{Title=title,Status=catalog.Find(title)!=null?"Bundled definition or alias":Read(title)!=null?"Reviewed public page; indexed-only metadata remains on demand":"Indexed only; not a confirmed wearable definition"}),
    EvidenceVersion=ItemAcquisitionEvidence.Version,Source="EQLWiki CC BY-SA 4.0; revision URLs retained. No private inventories or logs used."});
Console.WriteLine(JsonSerializer.Serialize(new{Items=catalog.Items.Count,Repaired=evidence.Count,Added=evidence.Sum(r=>r.Sources.Count),MissingBefore=totalBefore,MissingAfter=totalAfter}));
