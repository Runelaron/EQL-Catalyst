using System.Text.Json;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class CombatLabBrowseState
{
    public bool FollowCurrent {get;set;}=true;
    public string Zone {get;set;}="";
    public bool NamesOnly {get;set;}
    public bool FiltersExpanded {get;set;}
}

public sealed partial class TrackerController
{
    private bool labBrowsingEnabled;
    private CombatLabBrowseState labBrowsing=new();
    private NamedNpcIndex? namedNpcIndex;
    private NamedNpcIndex NamedNpcs=>namedNpcIndex??=new(File.Exists(Path.Combine(AppContext.BaseDirectory,"data/named-npcs.json"))?Json.Read<NamedNpcDocument>(Path.Combine(AppContext.BaseDirectory,"data/named-npcs.json")):new());
    internal CombatLabBrowseState LabBrowsing=>labBrowsing;
    internal void SetReviewNamedNpcs(NamedNpcDocument evidence){if(!Demo)throw new InvalidOperationException("Synthetic review only");namedNpcIndex=new(evidence);}
    private string LabBrowseKey=>labBrowsingEnabled?JsonSerializer.Serialize(new{labBrowsing.FollowCurrent,labBrowsing.Zone,labBrowsing.NamesOnly})+"|"+NamedNpcs.Version:"legacy";
    internal void EnableLabBrowsing()
    {
        lock(Sync)
        {
            if(labBrowsingEnabled)return;
            try{labBrowsing=store.Get<CombatLabBrowseState>("combat-lab-browsing-v1")??new(){FollowCurrent=Profile.Meter.SessionId.Length==0};}catch(JsonException){labBrowsing=new();}
            labBrowsingEnabled=true;_ = NamedNpcs;
            Profile.Meter.ClassCombinations.Clear();
            if(Profile.Meter.Scope!="History")Profile.Meter.Scope="Session";
            if(labBrowsing.FollowCurrent&&Profile.Meter.GroupPeriodId.Length==0)Profile.Meter.SessionId="";
            Save();EnsureSessionLoaded();
        }
    }
    internal void SetLabSession(string id)
    {
        lock(Sync)
        {
            ExitGroupPeriod(true);Profile.Meter.Scope="Session";Profile.Meter.SessionId=id;Profile.Meter.EncounterId="";
            Profile.Meter.From=Profile.Meter.Until=null;Profile.Meter.ClassCombinations.Clear();labBrowsing.FollowCurrent=id.Length==0;
            if(labBrowsing.Zone.Length>0&&!LabSessionEncounters().Any(f=>Identity.ZoneKey(f.Zone)==Identity.ZoneKey(labBrowsing.Zone)))labBrowsing.Zone="";
            SaveLabBrowsing();Save();EnsureSessionLoaded();
        }
    }
    internal void SetLabFilters(string zone,bool namesOnly,bool? expanded=null)
    {
        lock(Sync)
        {
            labBrowsing.Zone=zone;labBrowsing.NamesOnly=namesOnly;if(expanded.HasValue)labBrowsing.FiltersExpanded=expanded.Value;
            SaveLabBrowsing();
        }
    }
    private void SaveLabBrowsing(){if(!Demo){var copy=EquipmentPositions.Copy(labBrowsing);QueuePersistence(()=>store.Set("combat-lab-browsing-v1",copy));}}
    private static bool LabMatches(Encounter f,CombatLabBrowseState state,NamedNpcIndex named)=>(state.Zone.Length==0||Identity.ZoneKey(f.Zone)==Identity.ZoneKey(state.Zone))&&(!state.NamesOnly||named.Matches(f));
    private DpsViewState BuildLabCombat(IEnumerable<Encounter> history,Encounter? current,DateTime start,DpsSelection selection,IReadOnlyList<PlaySession>? sessions,CombatLabBrowseState? browse=null)
    {
        if(!labBrowsingEnabled)return DpsViews.Build(history,current,start,selection,sessions);
        var state=browse??labBrowsing;var chosen=selection.Copy();chosen.ClassCombinations.Clear();
        if(chosen.Scope!="History")chosen.Scope="Session";
        if(state.FollowCurrent&&chosen.GroupPeriodId.Length==0)chosen.SessionId="";
        if(Demo&&(sessions==null||sessions.Count==0))start=history.Select(f=>f.Start).DefaultIfEmpty(start).Min();
        var session=sessions?.FirstOrDefault(s=>s.Id==chosen.SessionId)??sessions?.LastOrDefault();
        bool Eligible(Encounter f)=>(chosen.Scope=="History"?f.Id==chosen.EncounterId:session==null?f.End>=start:f.Start>=session.Start&&(!session.NextLogin.HasValue||f.Start<session.NextLogin))&&(!chosen.From.HasValue||f.End>=chosen.From)&&(!chosen.Until.HasValue||f.Start<=chosen.Until)&&LabMatches(f,state,NamedNpcs);
        return DpsViews.Build(history.Where(Eligible),current!=null&&Eligible(current)?current:null,start,chosen,sessions);
    }
    internal IReadOnlyList<Encounter> LabSessionEncounters(IEnumerable<Encounter>? source=null)
    {
        var s=SelectedSession;var records=source??Combat.Encounters.Concat(Combat.Current==null?[]:[Combat.Current]);
        return records.Where(f=>s==null||f.Start>=s.Start&&(!s.NextLogin.HasValue||f.Start<s.NextLogin)).DistinctBy(f=>f.Id).ToArray();
    }
    internal IReadOnlyList<Encounter> LabHistory(IEnumerable<Encounter> source)
    {
        if(!labBrowsingEnabled)return source.ToArray();var p=Profile.Meter;
        return LabSessionEncounters(source).Where(f=>LabMatches(f,labBrowsing,NamedNpcs)&&(!p.From.HasValue||f.End>=p.From)&&(!p.Until.HasValue||f.Start<=p.Until)).ToArray();
    }
    internal string LabNamedCoverage()
    {
        if(!labBrowsing.NamesOnly)return "";
        IReadOnlyList<Encounter> all=LabSessionEncounters(Published?.History).Where(f=>labBrowsing.Zone.Length==0||Identity.ZoneKey(f.Zone)==Identity.ZoneKey(labBrowsing.Zone)).ToArray();int named=all.Count(NamedNpcs.Matches);
        return $"Names only · {named}/{all.Count} encounters match documented named NPCs; coverage is incomplete and other or unclassified encounters are excluded. Mixed fights retain all targets. Outside-combat healing has no named-fight attribution.";
    }
    internal DpsViewState LabExportView()=>BuildLabCombat(Combat.Encounters,Combat.Current,SessionStart,Profile.Meter,SessionRecords);
    private bool LabOutsideHealing(HealingEvent e)=>!labBrowsingEnabled||!labBrowsing.NamesOnly&&(labBrowsing.Zone.Length==0||Identity.ZoneKey(e.Zone)==Identity.ZoneKey(labBrowsing.Zone));
    internal bool SelectedSessionLive=>gameSessionLive&&SelectedSession?.Id==Sessions?.Sessions.LastOrDefault()?.Id;
}
