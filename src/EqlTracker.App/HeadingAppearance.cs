using System.Text.Json;
using EqlTracker.Core;
namespace EqlTracker.App;

internal static class HeadingAppearance
{
    internal const string StateKey="heading-appearance-v1";
    internal static string Resolve(string? value)=>value=="quest"?"quest":"classic";
    internal static string Read(TrackerStore store)
    {
        try{var value=store.Get<JsonElement>(StateKey);return Resolve(value.ValueKind==JsonValueKind.String?value.GetString():null);}
        catch(JsonException){return "classic";}
    }
}
public sealed partial class TrackerController
{
    private string? headingStyle;
    internal string HeadingStyle {get{lock(Sync)return headingStyle??=HeadingAppearance.Read(store);}}
    internal void SetHeadingStyle(string? value)
    {
        string next=HeadingAppearance.Resolve(value);
        lock(Sync){if(HeadingStyle==next)return;headingStyle=next;if(!Demo)QueuePersistence(()=>store.Set(HeadingAppearance.StateKey,next));}
    }
}
