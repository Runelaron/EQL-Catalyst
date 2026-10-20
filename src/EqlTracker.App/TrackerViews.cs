using System.Diagnostics;
using System.Text.Json;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed record TrackerViewSnapshot(long Revision, long QuestRevision, IReadOnlyList<ObjectiveRow> Objectives,
    IReadOnlyDictionary<string, List<QuestNode>> Hierarchies, DpsViewState Combat)
{ public GroupProjection? OverlayGroup { get; init; } public double RollingDps { get; init; } public DpsViewState? OverlayCombat { get; init; } public double OverlayRollingDps { get; init; } public IReadOnlyList<Encounter> History { get; init; } = []; public HealingViewState? Healing { get; init; } }

public sealed record EquipmentViewSnapshot(long Revision, CharacterProfile Profile, InventorySnapshot Inventory, Catalog Catalog);

public sealed partial class TrackerController
{
    private Task? viewWorker;
    private long planningRevision, catalogRevision, historyRevision, equipmentRevision, petRevision, statProfileRevision;
    private string savedEquipmentSettings = "", savedPetSettings = "", savedStatSettings = "";
    private EquipmentViewSnapshot? equipmentInputs;
    private CharacterProfile? planningProfile;
    public CharacterProfile? PlanningProfile => Volatile.Read(ref planningProfile);
    public EquipmentViewSnapshot? EquipmentView => Volatile.Read(ref equipmentInputs);
    public long EquipmentRevision => EquipmentView?.Revision ?? equipmentRevision;
    public long PetRevision => petRevision;
    public long StatProfileRevision => statProfileRevision;
    public long CatalogRevision => catalogRevision;
    private bool saveRequested;
    private TrackerViewSnapshot? published;
    private List<ObjectiveRow>? plannedObjectives;
    private string plannedKey = "";
    private string savedPlanningSettings = "";
    private string PlanningSettings => JsonSerializer.Serialize(new { Classes = Profile.EnabledClasses.ToArray(), Profile.GoalQueue, Profile.QuestTracking, Profile.QuestQuantities, Profile.GoalPriority, Profile.CompletedSteps, Profile.CompletedQuests, Profile.Routes, Profile.CurrentExpansion, Profile.Era, Profile.PlanThrough, Profile.Zone, Profile.Wishlist, Profile.SelectedLoadout });
    public TrackerViewSnapshot? Published => Volatile.Read(ref published);
    public long QuestRevision => Published?.QuestRevision ?? planningRevision;
    public long CombatRevision => Combat.Revision + historyRevision;
    public double LastSnapshotMilliseconds { get; private set; }
    private string PlanningKey => $"{planningRevision}|{catalogRevision}|{Inventory.Revision}|{Inventory.CapturedAt.Ticks}|{Inventory.Entries.Count}|{Profile.Zone}";
    private List<ObjectiveRow> PlanObjectives()
    {
        string key = PlanningKey;
        if (plannedObjectives == null || plannedKey != key) { plannedObjectives = QuestPlanner.Plan(Catalog, Profile, Inventory); plannedKey = key; }
        return plannedObjectives;
    }
    public List<QuestNode> Hierarchy(QuestDefinition quest) => viewWorker != null && Published is { } snapshot
        ? snapshot.Hierarchies.GetValueOrDefault(quest.Id) ?? [] : QuestHierarchy.Build(quest, Catalog, Profile, Inventory);
    internal void StartValidationViews()
    {
        validationAllSessions = true;
        SessionStart = Combat.Encounters.Select(f => f.Start).DefaultIfEmpty(DateTime.Now).Min();
        Profile.Meter.Scope = "Session"; Profile.Meter.Target = ""; Profile.Meter.Ability = "";
        Profile.Meter.Kind = ""; Profile.Meter.From = Profile.Meter.Until = null; Profile.Meter.CriticalOnly = false;
        viewWorker = Task.Run(PublishLoop);
    }
    internal void StartProjectionValidation() { if(!Demo)throw new InvalidOperationException("Isolated preview required");viewWorker=Task.Run(PublishLoop); }

    private async Task PublishLoop()
    {
        string lastPlan = "", lastCombat = "", lastOverlay = "", lastSource = "", lastHealing = "", lastEquipment = "";
        List<Encounter> sourceHistory = []; Encounter? sourceCurrent = null; IReadOnlyList<Encounter> displayHistory=[];
        long equipmentSerial = 0;
        long serial = 0, questSerial = 0, lastHistoryRevision = -1;
        var historical = new Dictionary<string, Encounter>();
        Encounter? rollingCurrent = null;
        IReadOnlyList<ObjectiveRow> objectives = [];
        IReadOnlyDictionary<string, List<QuestNode>> hierarchies = new Dictionary<string, List<QuestNode>>();
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                var watch = Stopwatch.StartNew();
                List<Encounter>? history = null; Encounter? current = null; DpsSelection? selection = null, overlaySelection = null;
                string nextCombat = "", nextOverlay = "", sourceKey = ""; DateTime session = SessionStart;
                IReadOnlyList<PlaySession>? sessionRecords = null;
                CharacterProfile? groupProfile = null; IReadOnlyList<GroupPeriod> groups = [];
                CharacterProfile? planProfile = null, persistedProfile = null; InventorySnapshot? planInventory = null, persistedInventory = null; Catalog? frozenCatalog = null, persistedCatalog = null; string nextPlan = "";
                EquipmentViewSnapshot? nextEquipment = null;
                using var captureMeasure = UpdateDiagnostics.Measure("Snapshot capture + lock wait");
                using (UpdateDiagnostics.Lock(Sync))
                {
                    if (catalogDirty) { persistedCatalog = new Catalog { SchemaVersion = Catalog.SchemaVersion, Items = Catalog.Items.ToList(), Quests = Catalog.Quests.ToList(), Documents = Catalog.Documents.ToList(), Zones = Catalog.Zones.ToList() }; var catalogToSave = persistedCatalog; QueuePersistence(() => store.Set("catalog", catalogToSave)); catalogDirty = false; }
                    if (MeterSelectionRepair.ClearIncomingSelfTarget(Profile.Meter, Profile.Character)) saveRequested = true;
                    if (MeterSelectionRepair.ClearIncomingSelfTarget(Profile.OverlayMeter!, Profile.Character)) saveRequested = true;
                    if (saveRequested)
                    { persistedProfile = EquipmentPositions.Copy(Profile); persistedInventory = EquipmentPositions.Copy(Inventory); var savedProfile = persistedProfile; var savedInventory = persistedInventory; QueuePersistence(() => { store.Set("profile", savedProfile); store.Set("inventory", savedInventory); }); saveRequested = false; }
                    string plan = PlanningKey; nextPlan = plan;
                    if (lastPlan != plan)
                    {
                        planProfile = persistedProfile ?? EquipmentPositions.Copy(Profile); planInventory = persistedInventory ?? EquipmentPositions.Copy(Inventory);
                        frozenCatalog = persistedCatalog ?? new Catalog { Items = Catalog.Items.ToList(), Quests = Catalog.Quests.ToList(), Documents = Catalog.Documents.ToList(), Zones = Catalog.Zones.ToList() };
                    }
                    string equipmentKey = $"{equipmentRevision}|{petRevision}|{statProfileRevision}|{catalogRevision}|{Inventory.Revision}|{Inventory.CapturedAt.Ticks}";
                    if (equipmentKey != lastEquipment)
                    {
                        nextEquipment = new(++equipmentSerial, planProfile ?? persistedProfile ?? EquipmentPositions.Copy(Profile), planInventory ?? persistedInventory ?? EquipmentPositions.Copy(Inventory), frozenCatalog ?? persistedCatalog ?? new Catalog { Items = Catalog.Items.ToList(), Quests = Catalog.Quests.ToList() });
                        lastEquipment = equipmentKey;
                    }
                    sourceKey = $"{Combat.Revision}|{Combat.Healing.Revision}|{historicalSnapshotRevision}|{historyRevision}|{Combat.Encounters.Count}|{Sessions?.ContentRevision}|{Volatile.Read(ref groupRevision)}|{Volatile.Read(ref inspectionIdentityRevision)}";
                    nextCombat = sourceKey + "|" + JsonSerializer.Serialize(Profile.Meter);
                    nextOverlay = sourceKey + "|" + JsonSerializer.Serialize(Profile.OverlayMeter);
                    if (sourceKey != lastSource)
                    {
                        if (lastHistoryRevision != historyRevision) { historical.Clear(); lastHistoryRevision = historyRevision; }
                        foreach (var id in changedHistoricalSnapshots) historical.Remove(id); changedHistoricalSnapshots.Clear();
                        foreach (var f in Combat.Encounters)
                            if (!historical.ContainsKey(f.Id)) historical[f.Id] = CombatSnapshots.Copy(f);
                        var ids = Combat.Encounters.Select(x => x.Id).ToHashSet();
                        foreach (var id in historical.Keys.Where(id => !ids.Contains(id)).ToList()) historical.Remove(id);
                        sourceHistory = historical.Values.ToList(); sourceCurrent = Combat.Current == null ? null : CombatSnapshots.Copy(Combat.Current);
                        displayHistory=sourceHistory.Concat(sourceCurrent==null?[]:new[]{sourceCurrent}).OrderByDescending(f=>f.Start).ToArray();
                        lastSource = sourceKey;
                    }
                    if (nextCombat != lastCombat || nextOverlay != lastOverlay)
                    {
                        history = sourceHistory; current = sourceCurrent; sessionRecords = SessionRecords;
                        if (nextCombat != lastCombat) selection = Profile.Meter.Copy();
                        if (nextOverlay != lastOverlay) { overlaySelection = Profile.OverlayMeter!.Copy(); if (overlaySelection.DisplayMode == OverlayCombatMode.Group) { groupProfile = new() { Character = Profile.Character, PetIdentities = Profile.PetIdentities.Select(EquipmentPositions.Copy).ToList(), GroupMembers = Profile.GroupMembers.Select(EquipmentPositions.Copy).ToList() }; groups = GroupPeriods; } }
                    }
                }
                captureMeasure.Dispose();
                if (nextEquipment != null) Volatile.Write(ref equipmentInputs, nextEquipment);
                
                if (planProfile != null && planInventory != null && frozenCatalog != null)
                {
                    using var planningMeasure = UpdateDiagnostics.Measure("Quest planning + hierarchy");
                    var planned = QuestPlanner.Plan(frozenCatalog, planProfile, planInventory);
                    var trees = frozenCatalog.Quests.ToDictionary(q => q.Id, q => QuestHierarchy.Build(q, frozenCatalog, planProfile, planInventory));
                    // A newer source revision makes this background result obsolete.
                    if (nextPlan == PlanningKey) { Volatile.Write(ref planningProfile, planProfile); objectives = planned; hierarchies = trees; lastPlan = nextPlan; questSerial++; }
                }
                if (history != null)
                {
                    rollingCurrent = current;
                    using var combatMeasure = UpdateDiagnostics.Measure("Combat aggregates + chart");
                    var combat = selection == null ? Published!.Combat : DpsViews.Build(history, current, session, selection, sessionRecords);
                    var overlay = overlaySelection == null ? Published!.OverlayCombat : DpsViews.Build(history, current, session, overlaySelection, sessionRecords);
                    var overlayGroup = groupProfile != null && overlay != null ? GroupProjection.Build(overlay, null, groupProfile, groups, nextOverlay, cancel.Token) : overlaySelection == null ? Published?.OverlayGroup : null;
                    bool valid;
                    using (UpdateDiagnostics.Lock(Sync)) valid = nextCombat == sourceKey + "|" + JsonSerializer.Serialize(Profile.Meter) && nextOverlay == sourceKey + "|" + JsonSerializer.Serialize(Profile.OverlayMeter) && sourceKey == $"{Combat.Revision}|{Combat.Healing.Revision}|{historicalSnapshotRevision}|{historyRevision}|{Combat.Encounters.Count}|{Sessions?.ContentRevision}|{Volatile.Read(ref groupRevision)}|{Volatile.Read(ref inspectionIdentityRevision)}";
                    if (valid)
                    {
                        lastCombat = nextCombat; lastOverlay = nextOverlay;
                        Volatile.Write(ref published, new(++serial, questSerial, objectives, hierarchies, combat) { OverlayGroup = overlayGroup, OverlayCombat = overlay, History = displayHistory, Healing = Published?.Healing, RollingDps = Published?.RollingDps ?? 0, OverlayRollingDps = Published?.OverlayRollingDps ?? 0 });
                    }
                }
                else if (Published?.QuestRevision != questSerial && Published is { } prior)
                    Volatile.Write(ref published, prior with { Revision = ++serial, QuestRevision = questSerial, Objectives = objectives, Hierarchies = hierarchies });
                if (HealingVisible && Published is { } selected)
                {
                    List<Encounter>? healingFights = null; List<HealingEvent> outside = []; HealingFilter? healingFilter = null; string healingSort = "", healingKey;
                    using (UpdateDiagnostics.Lock(Sync))
                    {
                        healingKey = $"{Combat.Healing.Revision}|{Combat.Revision}|{historicalSnapshotRevision}|{historyRevision}|{JsonSerializer.Serialize(Profile.Healing)}|{JsonSerializer.Serialize(Profile.Meter)}|{Sessions?.ContentRevision}|{Volatile.Read(ref groupRevision)}|{Volatile.Read(ref inspectionIdentityRevision)}";
                        if (healingKey != lastHealing)
                        {
                            var ids = selected.Combat.Fights.Select(f => f.Id).ToHashSet();
                            healingFights = Combat.Encounters.Concat(Combat.Current == null ? [] : [Combat.Current]).Where(f => ids.Contains(f.Id)).Select(HealingCopy).ToList();
                            healingFilter = Profile.Healing.Filter(selected.Combat.Filter.From, selected.Combat.Filter.Until); healingSort = Profile.Healing.Sort;
                            if (Profile.Meter.Scope == "Session") outside = Combat.Healing.Events.Where(e => e.EncounterId.Length == 0 && (selected.Combat.Session?.NextLogin == null || e.Time < selected.Combat.Session!.NextLogin) && (Profile.Meter.ClassCombinations.Count == 0 || Profile.Meter.ClassCombinations.Contains(SessionEvidence.ClassesAt(selected.Combat.Session, e.Time))) && HealingMetrics.Matches(e, healingFilter)).Select(e => e.Copy()).ToList();
                        }
                    }
                    if (healingFights != null && healingFilter != null)
                    {
                        using var healingMeasure = UpdateDiagnostics.Measure("Healing aggregates + chart");
                        var healing = HealingMetrics.Build(healingFights, outside, healingFilter, healingSort, string.Join('|', healingFights.Select(f => f.Id)) + JsonSerializer.Serialize(healingFilter));
                        Volatile.Write(ref published, Published! with { Revision = ++serial, Healing = healing }); lastHealing = healingKey;
                    }
                }
                else lastHealing = "";
                if (Published is { } rollingView)
                {
                    DateTime now = DateTime.Now;
                    double rolling = rollingCurrent?.Events.Where(e => e.Time > now.AddSeconds(-5) && e.Time <= now && CombatMetrics.Matches(e, rollingView.Combat.Filter)).Sum(e => e.Damage) / 5d ?? 0;
                    double overlayRolling = rollingView.OverlayCombat is { } ov && rollingCurrent!=null && ov.Fights.Any(f=>f.Id==rollingCurrent.Id) ? rollingCurrent.Events.Where(e => e.Time > now.AddSeconds(-5) && e.Time <= now && CombatMetrics.Matches(e, ov.Filter)).Sum(e => e.Damage) / 5d : 0;
                    if (rolling != rollingView.RollingDps || overlayRolling != rollingView.OverlayRollingDps) Volatile.Write(ref published, rollingView with { RollingDps = rolling, OverlayRollingDps = overlayRolling });
                }
                LastSnapshotMilliseconds = watch.Elapsed.TotalMilliseconds;
                await Task.Delay(200, cancel.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { File.AppendAllText(Path.Combine(DataFolder, "errors.log"), DateTime.Now + " View retry: " + error + Environment.NewLine); await Task.Delay(1000, cancel.Token); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { File.AppendAllText(Path.Combine(DataFolder, "errors.log"), DateTime.Now + " View worker: " + error + Environment.NewLine); }
    }
}
