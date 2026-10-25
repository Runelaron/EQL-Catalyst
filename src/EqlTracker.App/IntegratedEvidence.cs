using System.Globalization;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class IntegratedSetup
{
    public long Revision { get; set; }
    public List<EffectRule> Rules { get; set; } = [];
    public BardSetup Bard { get; set; } = new();
    public List<CoverageObservation> Coverage { get; set; } = [];
    public List<SongCoverageRule> CoverageRules { get; set; } = [];
    public CampNotebook Camps { get; set; } = new();
    public string ObservedZone { get; set; } = "";
    public Dictionary<string,string> Dispositions { get; set; } = [];
}
public sealed partial class TrackerController
{
    private IntegratedSetup integrated = new();
    private readonly Dictionary<string, JournalActivity> journal = [];
    private readonly List<JournalActivity> pendingJournal = [];
    private IReadOnlyList<HoldingsSummary> otherHoldings = [];
    private long journalRevision;
    private Task? integratedWorker;
    private bool journalLoading;
    private bool journalHistoryIncomplete;
    private DateTime nextHoldingsRead;
    private int holdingsReading;
    internal CampTimer? CampEstimate { get { lock(Sync)return integrated.Camps.Timer; } }
    internal long JournalRevision => Interlocked.Read(ref journalRevision);
    internal IReadOnlyList<JournalActivity> JournalSnapshot() { lock (Sync) return journal.Values.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).ToArray(); }
    internal IntegratedSetup IntegratedSnapshot() { lock (Sync) return EquipmentPositions.Copy(integrated); }
    private void StartIntegratedEvidence()
    {
        integratedWorker = Task.Run(() =>
        {
            store.MigrateJournal(Catalog);
            HoldingsIndex.BackfillMissing(Path.GetDirectoryName(DataFolder)!,Profile.Server,Catalog);
            var saved = store.Get<IntegratedSetup>("integrated-setup-v1") ?? new(); var rows = store.LoadActivities(limit: 10000);
            var stock = HoldingsIndex.Read(Path.GetDirectoryName(DataFolder)!, Profile.Server, Profile.Character);
            lock (Sync) { integrated = saved; foreach (var row in rows) journal.TryAdd(row.Id, row); otherHoldings = stock; journalHistoryIncomplete=rows.Count==10000; journalRevision++; activityRevision++; RefreshMechanicalSetup(); }
            Updated?.Invoke();
            var phone = store.Get<EqlTracker.Companion.CompanionHostSettings>("phone-settings-v1") ?? new();
            if (phone.Enabled) ConfigurePhoneAsync(phone).GetAwaiter().GetResult(); else phoneSettings = phone;
        }, cancel.Token);
    }
    internal async Task PrepareIntegratedValidation()
    {
        var state=await Task.Run(()=>{return store.Get<IntegratedSetup>("integrated-setup-v1")??new();});lock(Sync){integrated=state;RefreshMechanicalSetup();}
    }
    internal async Task LoadOlderJournal()
    {
        DateTime? before; string id;
        lock (Sync) { if (journalLoading) return; journalLoading = true; var oldest = journal.Values.OrderBy(a => a.At).ThenBy(a => a.Id).FirstOrDefault(); before = oldest?.At; id = oldest?.Id ?? ""; }
        try { var rows = await Task.Run(() => store.LoadActivities(before, id, 2000)); lock (Sync) { foreach (var row in rows) journal.TryAdd(row.Id, row); journalHistoryIncomplete=rows.Count==2000; journalRevision++; } Updated?.Invoke(); }
        finally { lock (Sync) journalLoading = false; }
    }
    private void Journal(JournalActivity activity)
    { if (journal.TryAdd(activity.Id, activity)) { pendingJournal.Add(activity); journalRevision++; activityRevision++; } }
    private void ObserveIntegrated(ParsedLine parsed, string id, string raw,bool historical=false)
    {
        if(parsed.ZoneVariant!=null)integrated.ObservedZone=parsed.ZoneVariant;
        string zone=integrated.ObservedZone.Length>0?integrated.ObservedZone:Profile.Zone;
        if (parsed.Item is {} item) Journal(ActivityJournal.Receipt(id, item, zone, Catalog, historical?null:Profile));
        if (parsed.SlainActor is {} slain) Journal(new() { Id = id + ":death", At = parsed.Time, Kind = "Death", Name = slain, Zone = zone });
        string message = raw[(raw.IndexOf("] ", StringComparison.Ordinal) + 2)..];
        if (message.StartsWith("You receive ", StringComparison.Ordinal) || message.Contains("sold it for", StringComparison.Ordinal) || message.StartsWith("You sold ", StringComparison.Ordinal))
            if (ActivityJournal.ParseCopper(message) is {} copper) Journal(new() { Id = id + ":coin", At = parsed.Time, Kind = message.Contains("sold", StringComparison.Ordinal) ? "Merchant" : "Coin", Copper = copper, Name = "Recorded income" });
        if (parsed.Boundary || parsed.SlainActor != null || parsed.LeftGroup || parsed.GroupLeft != null || parsed.GroupRosterReset)
        {
            foreach (var coverage in integrated.Coverage.Where(c => !c.InvalidatedAt.HasValue && (parsed.Boundary || parsed.LeftGroup || parsed.GroupRosterReset || c.Caster == parsed.SlainActor || c.Recipient == parsed.SlainActor || c.Caster == parsed.GroupLeft))) {coverage.Until = parsed.Time;coverage.InvalidatedAt=parsed.Time;}
        }
        // Separate generic cast attribution; no pet ownership field is consulted here.
        ConnectedSpellEvidence.Attempt(connectedSpells, parsed, id, Profile.Character, Profile.PetIdentities, SpellIndexSnapshot());
        if (statMessages != null)
        {
            var recipients = Profile.GroupMembers.Where(m => m.Contains(m.Name, parsed.Time) && !m.IsPet).Select(m => new PetIdentity { Name = m.Name, From = m.From, Until = m.Until }).Concat(Profile.PetIdentities).ToArray();
            StatEvidenceReader.Observe(new(), raw, id, Profile.Character, recipients, statMessages, e =>
            {
                ConnectedSpellEvidence.Effect(connectedSpells, e, SpellIndexSnapshot());
                var effect = connectedSpells.History.FirstOrDefault(s => s.Id == id + ":spell-effect");
                if (effect == null || integrated.Coverage.Any(c => c.Id == effect.Id)) return;
                var definition = SpellIndexSnapshot().Resolve(effect.Raw).Definition;
                if (definition?.Levels?.ContainsKey("BRD") != true && !integrated.Bard.Songs.Any(s => s.Identity.Name == effect.Canonical)) return;
                var observed = new CoverageObservation { Id = effect.Id, Song = new(definition?.Id?.ToString(CultureInfo.InvariantCulture) ?? effect.Canonical, effect.Canonical, effect.Rank), At = effect.At, Caster = effect.Caster, Recipient = effect.Recipient, SuccessfulApplication = effect.Transition == "Landing", Attribution = effect.Caster.Length > 0 ? EvidenceQuality.Observed : EvidenceQuality.Unknown, Source = effect.Source };
                foreach(var rule in integrated.CoverageRules)rule.Apply(observed,Profile.Zone);
                integrated.Coverage.Add(observed);
                // Duration is intentionally unknown until reviewed evidence establishes a coverage window.
                if (effect.Transition.Contains("Fade", StringComparison.Ordinal)) foreach (var prior in integrated.Coverage.Where(c => c.Song.Name == effect.Canonical && c.Recipient == effect.Recipient && c.At <= effect.At)) {prior.Until = effect.At;prior.InvalidatedAt=effect.At;}
                integrated.Coverage = integrated.Coverage.OrderByDescending(c => c.At).Take(1000).ToList(); integrated.Revision++;
            });
        }
    }
    internal void SaveIntegrated(IntegratedSetup setup)
    {
        lock (Sync) { integrated = EquipmentPositions.Copy(setup); integrated.Revision++; var frozen = EquipmentPositions.Copy(integrated); QueuePersistence(() => store.Set("integrated-setup-v1", frozen)); journalRevision++; activityRevision++; RefreshMechanicalSetup(); }
        Updated?.Invoke();
    }
    internal void SaveSongPlan(SongRecommendation recommendation)
    {
        lock (Sync) { var swap = BardCoverage.SavePlan(integrated.Bard, recommendation, DateTime.Now); Journal(new() { Id = "plan:" + swap.Id, At = swap.At, Kind = "Planned setup", Name = $"Gem {swap.Gem}: {swap.Before.Name} → {swap.After.Name}", Source = "Saved in Catalyst only" }); SaveIntegrated(integrated); PersistLocalJournal(); }
    }
    private void PersistLocalJournal()
    { var rows = pendingJournal.ToArray(); pendingJournal.Clear(); if (rows.Length > 0) QueuePersistence(() => store.SaveActivities(rows)); }
    internal SessionRecap? Recap(string? sessionId = null)
    {
        lock (Sync) { var session = Sessions?.Sessions.FirstOrDefault(s => s.Id == sessionId) ?? SelectedSession ?? Sessions?.Sessions.LastOrDefault();var fights=(Published?.History??Combat.Encounters).Where(f=>f.Id!=Combat.Current?.Id).Concat(Combat.Current==null?[]:[Combat.Current]);return session == null ? null : SessionRecap.Build(session, journal.Values, fights) with {HistoryIncomplete=journalHistoryIncomplete}; }
    }
    internal IReadOnlyList<OtherCharacterHolding> OtherHoldings(ItemDefinition item) { lock (Sync) return otherHoldings.SelectMany(s => s.Match(item).Select(i => new OtherCharacterHolding(s.Character, s.CapturedAt, i))).ToArray(); }
    private void RefreshHoldings()
    {
        if(DateTime.UtcNow<nextHoldingsRead||Interlocked.Exchange(ref holdingsReading,1)!=0)return;nextHoldingsRead=DateTime.UtcNow.AddSeconds(15);
        _=Task.Run(()=>{try{var next=HoldingsIndex.Read(Path.GetDirectoryName(DataFolder)!,Profile.Server,Profile.Character);lock(Sync){if(PetComparisonEngine.Key(next)!=PetComparisonEngine.Key(otherHoldings)){otherHoldings=next;journalRevision++;}}}finally{Interlocked.Exchange(ref holdingsReading,0);}});
    }
    internal BardCoverageView SongCoverage(DateTime? at = null)
    {
        lock (Sync) { var time = at ?? DateTime.Now; var group = Profile.GroupMembers.Where(m => !m.IsPet && m.Contains(m.Name, time)).Select(m => m.Name).Append(Profile.Character).ToHashSet(StringComparer.OrdinalIgnoreCase); return BardCoverage.Evaluate(integrated.Bard, integrated.Coverage, group, new() { Character = Profile.Character, Recipient = Profile.Character, Server=Profile.Server,Fingerprint=BardContextFingerprint(time),Classes = Profile.CurrentClasses,CasterLevel=Profile.ActiveLoadout.Level>0?Profile.ActiveLoadout.Level:null, Live = true }, integrated.Rules, time); }
    }
    internal string BardContextFingerprint(DateTime? at=null) {lock(Sync)return PetComparisonEngine.Key(new{integrated.Bard.Instrument,integrated.Bard.Singing,integrated.Bard.Focus,integrated.Bard.EffectiveCasterLevel,integrated.Bard.AaContext,Coverage=integrated.Coverage.Where(c=>c.Fresh(at??DateTime.Now,Profile.Character)).Select(c=>new{c.Song,c.Caster,c.Recipient,c.Instrument,c.Singing,c.Focus,c.EffectiveCasterLevel,c.AaContext}).Distinct().OrderBy(c=>c.Caster).ThenBy(c=>c.Song.SpellId).ToArray()});}
    internal IReadOnlyList<BardSong> KnownBardSongs()
    {
        lock (Sync) return SpellIndexSnapshot().BardSongs.Select(s => new BardSong { Identity = new(s.Id?.ToString(CultureInfo.InvariantCulture) ?? s.Name, s.Name, s.UpgradeRank), Mana = s.Mana, CooldownMs = s.RecastMs,RequiredLevel=s.Levels?.GetValueOrDefault("BRD"), NonTargetedArea = s.Target is 4 or 39 or 40 or 41 or 43 ? true : s.Target is 1 or 5 or 6 or 8 or 14 ? false : null, Source = s.Source+" · Symphonic Aura eligibility: "+BardCoverage.AuraSource }).ToArray();
    }
    internal CompatibilityComparison? EvaluateItemCompatibility(ItemDefinition item, int rank, bool pet, PetBuild? build, GearComparison? comparison)
    {
        if (comparison?.Evaluation is {} evaluated && (evaluated.BuffedCompatibility ?? evaluated.UnbuffedCompatibility) is {} existing) return existing;
        lock (Sync)
        {
            var owner=Profile.ActiveLoadout;
            var contextOwner=pet&&build!=null?GuidedPetBuilds.Context(owner,build):owner;
            var positions=pet&&build!=null?GuidedPetBuilds.Positions(build,owner,Catalog):EquipmentPositions.For(owner,pet);
            var selected=positions.FirstOrDefault(p=>p.Id==comparison?.Evaluation?.PositionId)??positions.FirstOrDefault(p=>EquipmentPositions.Fits(item,p,pet));
            if(selected==null)return null;
            var evaluation=new LoadoutEvaluationSession(Catalog,contextOwner,pet,new(),positions,positions,petInventory:pet?build:null).Compare(item,rank,selected.Id);
            return evaluation.BuffedCompatibility??evaluation.UnbuffedCompatibility;
        }
    }
}
