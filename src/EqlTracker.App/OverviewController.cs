using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private string overviewSession="";
    private long activityRevision;
    internal string OverviewSession=>overviewSession;
    private void InitializeOverview()=>overviewSession=store.Get<string>("overview-session-v1")??"";
    internal void SelectOverviewSession(string id)
    {
        if(overviewSession==id)return;overviewSession=id;QueuePersistence(()=>store.Set("overview-session-v1",id));
    }
    internal PlaySession? OverviewSelectedSession=>Sessions?.Sessions.FirstOrDefault(s=>s.Id==overviewSession)??(overviewSession.Length==0?Sessions?.Sessions.LastOrDefault():null);
    internal string OverviewFingerprint()
    {
        var session=OverviewSelectedSession;
        return SessionOverview.Version+"|"+PetComparisonEngine.Key(new{overviewSession,session?.Id,session?.Start,session?.LastObserved,session?.NextLogin,session?.Inferred,CombatRevision,Activity=Interlocked.Read(ref activityRevision),gameSessionLive});
    }
    internal async Task<SessionOverview?> ReadOverview(CancellationToken token)
    {
        var chosen=OverviewSelectedSession;if(chosen==null)return null;var session=EquipmentPositions.Copy(chosen);
        Encounter[] fights;JournalActivity[] pending;bool active;
        lock(Sync)
        {
            // Retained summaries cover sessions older than the hydrated encounter window.
            var memory=Published?.History??Combat.Encounters;
            fights=sessionSummaries.Concat(memory).Concat(Combat.Current==null?[]:[Combat.Current]).GroupBy(f=>f.Id).Select(g=>g.Last()).Where(f=>f.Start>=session.Start&&f.Start<=session.LastObserved&&(!session.NextLogin.HasValue||f.Start<session.NextLogin)).Select(f=>new Encounter{Id=f.Id,Start=f.Start,End=f.End,Zone=f.Zone,Finished=f.Finished,PlayerDamage=f.PlayerDamage,PetDamage=f.PetDamage,UnknownDamage=f.UnknownDamage,HealingCoverage=f.HealingCoverage,HealingSummaries=f.HealingSummaries.ToList(),HealingDetailAvailable=false}).ToArray();
            pending=journal.Values.Where(a=>ActivityJournal.InSession(a,session)).ToArray();active=gameSessionLive&&Sessions?.Sessions.LastOrDefault()?.Id==session.Id;
        }
        return await Task.Run(()=>
        {
            token.ThrowIfCancellationRequested();var saved=Demo?[]:store.ActivitiesForSession(session,token);
            token.ThrowIfCancellationRequested();
            // Database summaries retain healing totals even when event detail has expired.
            foreach(var fight in fights)if(fight.Heals.Count==0)fight.HealingDetailAvailable=false;
            return SessionOverview.Build(session,saved.Concat(pending).DistinctBy(a=>a.Id),fights,active);
        },token);
    }
}
