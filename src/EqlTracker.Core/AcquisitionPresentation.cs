using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal static class AcquisitionPresentation
{
    internal static string ActorKey(string value) => Regex.Replace(Regex.Replace(value.Trim(),@"^(?:an?|the)\s+","",RegexOptions.IgnoreCase),@"[^\p{L}\p{N}]","").ToUpperInvariant();
    internal static string Method(ItemSource source)
    {
        string method=QuestPresentation.Normalize(source.Method);
        if(method=="GROUND SPAWN"||QuestPresentation.Normalize(source.Mob)=="GROUND SPAWN")return "Ground spawn";
        if(method is "VENDOR" or "MERCHANT" or "SOLD BY" or "PURCHASE" or "PURCHASED" or "BUY")return "Purchase";
        return method=="DROP"?"Drop":source.Method.Length>0?source.Method:"Acquisition method unverified";
    }
    internal static string Coordinates(string value)
    {
        var match=Regex.Match(value,@"(?<n>[+-]?\d+(?:\.\d+)?)\s*,\s*(?<n>[+-]?\d+(?:\.\d+)?)(?:\s*,\s*(?<n>[+-]?\d+(?:\.\d+)?))?");
        return match.Success?string.Join(", ",match.Groups["n"].Captures.Select(c=>double.Parse(c.Value,CultureInfo.InvariantCulture).ToString("+0.###;-0.###;0",CultureInfo.InvariantCulture))):"";
    }
    internal static string CoordinateKey(ItemSource source)=>Coordinates(source.Coordinates.Length>0?source.Coordinates:source.Mob+" "+source.Notes);
    internal static IEnumerable<IGrouping<string,ItemSource>> Routes(IEnumerable<ItemSource> input)
    {
        var rows=input.ToArray();
        string Key(ItemSource source)
        {
            string zone=Identity.ZoneKey(source.Zone),method=Method(source),actor=ActorKey(source.Mob);
            if(method=="Ground spawn")
            {
                string coords=CoordinateKey(source);
                if(coords.Length==0&&QuestPresentation.Normalize(source.Mob) is "GROUND SPAWN" or "")
                {
                    var known=rows.Where(s=>Identity.ZoneKey(s.Zone)==zone&&Method(s)==method).Select(CoordinateKey).Where(c=>c.Length>0).Distinct().ToArray();
                    if(known.Length==1)coords=known[0]; // Keep this unqualified record as evidence, not a second asserted location.
                }
                actor=coords.Length>0?coords:actor+"|"+QuestPresentation.Normalize(source.Notes);
            }
            return zone+"|"+method+"|"+actor;
        }
        return rows.GroupBy(Key);
    }
    internal static bool CoversPickup(string body,QuestSourceBlock route)
    {
        if(route.Method!="Ground spawn")return false;
        string text=body.Trim();
        if(text.StartsWith(route.Source.Zone+" - ",StringComparison.OrdinalIgnoreCase))text=text[(route.Source.Zone.Length+3)..];
        var match=Regex.Match(text,@"^(?:Pick\s*up|Collect)\s+(?<item>.+?)(?:\s*\((?<coords>[+\d.,\s-]+)\))?[.!]?$",RegexOptions.IgnoreCase);
        if(!match.Success||!route.Item.Aliases.Prepend(route.Item.Name).Any(n=>QuestPresentation.Normalize(n)==QuestPresentation.Normalize(match.Groups["item"].Value)))return false;
        return !match.Groups["coords"].Success||Coordinates(match.Groups["coords"].Value)==route.Evidence.Select(CoordinateKey).FirstOrDefault(c=>c.Length>0);
    }
    internal static IReadOnlyList<string> Details(QuestSourceBlock block)
    {
        var rows=new List<string>();
        foreach(string coordinates in block.Evidence.Select(CoordinateKey).Where(c=>c.Length>0).Distinct())rows.Add("Coordinates: "+coordinates);
        foreach(var source in block.Evidence)
        {
            if(block.Method=="Ground spawn"&&source.Mob.Length>0&&!Regex.IsMatch(source.Mob,@"^(?:Pick\s*up|Collect|Ground spawn)\b",RegexOptions.IgnoreCase))rows.Add(source.Mob);
            string note=source.Notes;
            if(Regex.IsMatch(note,@"\braid\b",RegexOptions.IgnoreCase))rows.Add("Raid target");
            var level=Regex.Match(note,@"\[(\d+)\]");if(level.Success)rows.Add("Mob level: "+level.Groups[1].Value);
            if(note.Length==0||ProgressPresentation.CoversDrop(note,block)||CoversPickup(note,block)||Regex.IsMatch(note,@"^(?:.+\s+)?(?:epic checklist|preparation guide|wiki documented)$",RegexOptions.IgnoreCase))continue;
            rows.Add(note);
        }
        return rows.DistinctBy(QuestPresentation.Normalize).ToArray();
    }
}
