namespace EqlTracker.Core;

// Presentation only. It never changes a meter selection or replays log ingestion.
internal sealed record SessionOverview(SessionRecap Recap,double CombatSeconds,double ElapsedSeconds,
    double SelfDps,double PetDps,long? Healing,string HealingCoverage,bool Active,string Qualification)
{
    internal const string Version="overview-v1";
    internal static SessionOverview Build(PlaySession session,IEnumerable<JournalActivity> activity,IEnumerable<Encounter> encounters,bool active,bool historyIncomplete=false)
    {
        var fights=encounters.Where(f=>f.Start>=session.Start&&f.Start<=session.LastObserved&&(!session.NextLogin.HasValue||f.Start<session.NextLogin)).GroupBy(f=>f.Id).Select(g=>g.Last()).ToArray();
        var recap=SessionRecap.Build(session,activity,fights) with {HistoryIncomplete=historyIncomplete||session.Inferred};
        double seconds=CombatMetrics.Duration(fights,new());
        var healing=HealingMetrics.Build(fights,[],new());
        string qualification=recap.HistoryIncomplete?"Partial retained history · ":"Retained history · ";
        qualification+=active?"In progress":$"Session ended · last observed {session.LastObserved:MMM dd HH:mm:ss}";
        return new(recap,seconds,Math.Max(0,(session.LastObserved-session.Start).TotalSeconds),recap.PlayerDamage/Math.Max(1,seconds),recap.PetDamage/Math.Max(1,seconds),healing.TotalsAvailable?healing.Effective:null,healing.Coverage,active,qualification);
    }
}
