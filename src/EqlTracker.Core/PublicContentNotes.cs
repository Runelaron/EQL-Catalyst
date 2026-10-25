using System.Text.Json;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal static class PublicContentNotes
{
    internal const string Version="content-notes-v17";
    private static readonly Lazy<Dictionary<string,List<string>>> bundled=new(()=>Read(Path.Combine(AppContext.BaseDirectory,"data","public-content-notes.json")));
    internal static IReadOnlyList<string> Item(string key)=>bundled.Value.GetValueOrDefault("item:"+key)??[];
    internal static IReadOnlyList<string> Zone(string name)=>bundled.Value.GetValueOrDefault("zone:"+Identity.ZoneKey(name))??[];
    internal static string Coordinate(string text,string label)
    {var match=Regex.Match(ItemAcquisitionEvidence.Plain(text),Regex.Escape(label)+@"\s*:\s*\(?(?<c>-?\d+\s*,\s*-?\d+\s*,\s*-?\d+)",RegexOptions.IgnoreCase);return match.Success?match.Groups["c"].Value:"Undocumented";}
    internal static Dictionary<string,List<string>> Read(string path)
    {
        try{return File.Exists(path)?Json.Read<Dictionary<string,List<string>>>(path):[];}
        catch(Exception error) when(error is IOException or JsonException){return [];}
    }
}
