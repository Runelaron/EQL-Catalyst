namespace EqlTracker.Core;

public static class SourcePresentation
{
    public static IEnumerable<IGrouping<string,ItemSource>> Groups(IEnumerable<ItemSource> sources) => sources.GroupBy(s=>Identity.ZoneKey(s.Zone)+"|"+QuestPresentation.Normalize(s.Mob)+"|"+QuestPresentation.Normalize(s.Method));
    public static string Evidence(IEnumerable<ItemSource> sources) => string.Join("\n",sources.Select(s=>s.WikiUrl+" · revision "+s.Revision+" · "+s.Era+" · "+s.Availability+"\n"+s.Notes).Distinct());
}
