namespace EqlTracker.Core;

internal static class CompactObjective
{
    internal static string Text(ObjectiveRow item)
    {
        string cls=CastingPriority.Code(item.Class);
        if(cls.Length==0)cls=item.Class;
        var sources=item.Sources.Where(s=>Identity.ZoneKey(s.Zone)==Identity.ZoneKey(item.Zone)).ToArray();
        var source=sources.FirstOrDefault(s=>AcquisitionPresentation.ActorKey(s.Mob)==AcquisitionPresentation.ActorKey(item.Mob))??sources.FirstOrDefault();
        string actor=source?.Mob??item.Mob;
        string method=source==null?"":AcquisitionPresentation.Method(source);
        if(method=="Drop"&&actor.Length>0)return cls+" · "+actor+" — "+item.Item;
        if(method is not ("Ground spawn" or "Crafted" or "Craft" or "Crafting")&&actor.Length>0&&!new[]{"Unknown","Ground spawn","Crafted","Crafting"}.Contains(actor,StringComparer.OrdinalIgnoreCase))return cls+" · "+actor;
        return cls+" · "+item.Item;
    }
    internal static string Tooltip(ObjectiveRow item,bool fallback)=>item.Class+" · "+item.Quest+"\n"+item.Item+$" · need {item.Remaining} · owned {item.Owned} / {item.Needed}\nDestination: "+item.Zone+"\n"+item.Instructions+
        (item.Sources.Count>0?"\nSources:\n"+string.Join("\n",item.Sources.Select(s=>s.ToString()).Distinct()):"")+(fallback?"\nNext goal from Queue/suggestions; no travel or drop-rate estimate.":"");
}
