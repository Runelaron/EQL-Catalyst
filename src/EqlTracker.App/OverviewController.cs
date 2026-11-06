using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private string overviewSession="";
    private bool overviewRatings;
    private long activityRevision;
    internal string OverviewSession=>overviewSession;
    private void InitializeOverview(){overviewSession=store.Get<string>("overview-session-v1")??"";overviewRatings=store.Get<bool>("overview-ratings-v1");}
    internal bool OverviewRatings=>overviewRatings;
    internal void SetOverviewRatings(bool value){if(overviewRatings==value)return;overviewRatings=value;QueuePersistence(()=>store.Set("overview-ratings-v1",value));}
    internal (string Key,Func<OverviewLootRatings> Create,Catalog Catalog) OverviewRatingContext(IReadOnlyList<LootReceiptGroup> loot)
    {
        lock(Sync)
        {
            var original=Profile.ActiveLoadout;
            var owner=new LoadoutProfile{Name=original.Name,Classes=[..original.Classes],Level=original.Level,Race=original.Race,RaceLevel=original.RaceLevel,Deity=original.Deity,Preset=original.Preset,Equipment=EquipmentPositions.Copy(original.Equipment),EquipmentPositions=EquipmentPositions.Copy(original.EquipmentPositions),StatProfiles=EquipmentPositions.Copy(original.StatProfiles),Mechanics=EquipmentPositions.Copy(original.Mechanics)};
            if(owner.Mechanics is {} mechanics){mechanics.Revision="";mechanics.PetUsualBuffs.Clear();mechanics.Anchors.RemoveAll(a=>a.Pet);}
            foreach(var sheet in owner.StatProfiles){sheet.ConfirmedAt=null;sheet.GameEvidenceReviewedAt=null;}
            var role=SelfRoleRatings.Resolve(Profile,owner);var catalog=EquipmentView?.Catalog??Catalog;
            var definitions=EquipmentPositions.For(owner,false).Where(p=>p.Item!=null).Select(p=>p.Item!.ConfirmedItemKey).Distinct().Order().Select(k=>catalog.FindKey(k)).Concat(loot.Select(l=>catalog.FindKey(l.ItemKey)??catalog.Find(l.Name))).ToArray();
            string key=OverviewLootRatings.Version+PetComparisonEngine.Key(new{owner,Role=role.Identity,Definitions=definitions.Select(i=>i==null?null:new{i.Key,i.Name,i.Classes,i.Races,i.Slots,i.Deity,i.TwoHanded,i.RequiredLevel,i.EffectActivationLevel,i.Stats,i.Effects,i.EffectEvidence,i.MetadataComplete,i.MetadataVersion,i.RequiresSourceIdentity,i.RequiresExplicitIdentity,Type=i.Display.Type,Skill=i.Display.Skill,i.Display.WeaponShape}),Items=loot.Select(l=>new{l.Identity,l.ItemKey,Rank=l.Receipts.FirstOrDefault()?.Rank})});
            return(key,()=>new(catalog,owner,role),catalog);
        }
    }
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
