using System.Text.RegularExpressions;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController : IDisposable
{
    public object Sync { get; } = new();
    public Catalog Catalog { get; private set; }
    public CharacterProfile Profile { get; private set; }
    public InventorySnapshot Inventory { get; private set; }
    public CombatEngine Combat { get; }
    public string Status { get; private set; } = "Starting local tracker…";
    public string Alert { get; private set; } = "";
    public string ThemeName { get; private set; } = "coldain";
    public string DataFolder { get; }
    public bool Demo { get; }
    public DateTime SessionStart { get; private set; } = DateTime.Now;
    public DateTime? LastLogActivity { get; private set; }
    public event Action? Updated;
    private readonly TrackerStore store;
    private readonly CancellationTokenSource cancel = new();
    private readonly WikiClient wiki = new();
    private Task? worker;
    private Task? catalogWorker;
    private EquipmentCatalog? equipmentCatalog;
    public CatalogSyncState EquipmentSync { get; private set; } = new();
    public string GearSearchStatus { get; private set; } = "";
    private FileCheckpoint? checkpoint;
    private readonly List<Encounter> dirty = [];
    private readonly HashSet<string> changedHistoricalSnapshots = [];
    private long historicalSnapshotRevision;
    private readonly List<(string, ItemObservation)> observations = [];
    private readonly List<(string Id, ItemObservation Item)> recentObservationEvidence = [];
    private EncounterEditUndo? historyUndo;
    private readonly HashSet<string> metadataQueue = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> gearMetadataPriority = new();
    private readonly SemaphoreSlim equipmentWake = new(0, 1);
    internal void RequestGearMetadata(string name)
    {
        gearMetadataPriority.TryAdd(Identity.Canonical(name), 0);
        try { equipmentWake.Release(); } catch (SemaphoreFullException) { }
    }
    private bool refreshing;
    private readonly Dictionary<string, ItemObservation> liveObservations = [];
    public TrackerController(string? dataFolder = null, bool demo = false, string character = "", string server = "", string? gameFolder = null)
    {
        if (demo) { if (character.Length == 0) character = "ExampleHero"; if (server.Length == 0) server = "example"; }
        var launch = new LaunchProfile(character, server, gameFolder ?? "");
        if (!launch.Valid) throw new InvalidDataException("Choose a character and server before opening a tracker.");
        Demo = demo; DataFolder = dataFolder ?? launch.DataFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EQLTracker"));
        store = new TrackerStore(Path.Combine(DataFolder, "tracker.db"));
        var bundled = Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data", "catalog.json"));
        var savedCatalog = store.Get<Catalog>("catalog");
        catalogDirty = !demo && (savedCatalog == null || savedCatalog.SchemaVersion < bundled.SchemaVersion);
        Catalog = demo ? bundled : CatalogMigration.Merge(savedCatalog ?? bundled, bundled);
        OctoberContent.Apply(Catalog);
        string snapshotPath = Path.Combine(AppContext.BaseDirectory, "data", "equipment.json");
        var snapshot = File.Exists(snapshotPath) ? Json.Read<EquipmentSnapshot>(snapshotPath) : new EquipmentSnapshot();
        MergeItems(snapshot.Items);
        if (!demo)
        {
            equipmentCatalog = new(Path.Combine(Path.GetDirectoryName(DataFolder)!, "equipment-catalog.db"));
            equipmentCatalog.Store(Catalog.Items.Where(i => i.Slots.Count > 0), []);
            var cached = equipmentCatalog.Items(); MergeItems(cached); EquipmentSync = equipmentCatalog.State();
            if (EquipmentSync.IndexedAt == null && snapshot.Titles.Count > 0)
            {
                equipmentCatalog.Index(snapshot.Titles); equipmentCatalog.Store(snapshot.Items, snapshot.Titles, snapshot.Items.Select(i => i.MetadataVersion).DefaultIfEmpty(0).Min());
                EquipmentSync.IndexedAt = snapshot.GeneratedAt; EquipmentSync.Status = "Bundled full equipment catalog ready offline";
                equipmentCatalog.Counts(EquipmentSync); equipmentCatalog.SaveState(EquipmentSync);
            }
        }
        CatalogSlotEvidence.Apply(Catalog, Path.Combine(AppContext.BaseDirectory,"data","catalog-slot-repairs.json"));
        PetItemRules.ApplyDisplayEvidence(Catalog, Path.Combine(AppContext.BaseDirectory, "data", "pet-item-fixtures.json"));
        QuestStartEvidence.Apply(Catalog,Path.Combine(AppContext.BaseDirectory,"data","quest-starts.json"));
        ItemAcquisitionEvidence.Apply(Catalog,Path.Combine(AppContext.BaseDirectory,"data","acquisition-evidence.json"));
        ItemEffects.Apply(Catalog);
        ItemEvidenceFixtures.Apply(Catalog);
        Profile = store.Get<CharacterProfile>("profile") ?? (demo ? new CharacterProfile { Character = character, Server = server, GameFolder = gameFolder ?? "" } : LauncherPreferences.NewProfile(launch));
        if (!Profile.Character.Equals(character, StringComparison.OrdinalIgnoreCase) || !Profile.Server.Equals(server, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The selected database belongs to another character.");
        if (Profile.ContinuityVersion < 1) { if (Profile.EncounterTimeoutSeconds == 8) Profile.EncounterTimeoutSeconds = 12; Profile.ContinuityVersion = 1; }
        MeterSelectionRepair.ClearIncomingSelfTarget(Profile.Meter, Profile.Character);
        bool presentationMigration=Profile.OverlayMeter==null||Profile.OverlayFooterDisplay==null||!Profile.SecondaryTextByTheme.ContainsKey("Frost")||!Profile.SecondaryTextByTheme.ContainsKey("Vellum")||!Profile.SecondaryTextByTheme.ContainsKey("Shadowman")||!Enum.IsDefined(Profile.TrackerTabs);
        TrackerPresentation.Migrate(Profile);if(presentationMigration&&!Demo)saveRequested=true;
        if (Profile.Loadouts.Count == 0) Profile.Loadouts.Add(new());
        foreach (var owner in Profile.Loadouts) GearBuildEditing.Migrate(owner);
        NativeSyncPolicy.Migrate(Profile.NativeSync);
        Profile.LastWikiRefreshAt ??= store.Get<DateTime?>("lastWikiRevisionCheck");
        Profile.CurrentExpansion = ExpansionRules.Current(Profile); Profile.Era = Profile.CurrentExpansion;
        if (Profile.LayoutVersion < 1)
        {
            if (Profile.Dps.Width == 340 && Profile.Dps.Height == 290) { Profile.Dps.Width = 440; Profile.Dps.Height = 410; }
            Profile.LayoutVersion = 1;
        }
        if (Profile.Weights.Count == 0) Profile.Weights = GearScorer.Defaults.ToDictionary(x => x.Key, x => new Dictionary<string, double>(x.Value));
        GearRatingMigration.Apply(Profile);
        foreach (var loadout in Profile.Loadouts) { if (loadout.ConfirmedPetInventory != null) PetInventoryEditing.EnsureIdentities(loadout.ConfirmedPetInventory); foreach (var build in loadout.PetBuilds) PetInventoryEditing.EnsureIdentities(build); }
        Inventory = store.Get<InventorySnapshot>("inventory") ?? new();
        InitializeInspection();
        statEvidence=store.Get<StatEvidenceState>("statEvidence") ?? new();
        statCheckpoint=store.Get<FileCheckpoint>("statEvidenceCheckpoint");
        InitializeConnectedSpells();
        QuestRecipes.ApplyIdentities(Profile, Inventory);
        checkpoint = store.Get<FileCheckpoint>("checkpoint"); historyUndo = store.Get<EncounterEditUndo>("encounterEditUndo");
        latestExport = store.Get<ExportEvidence>("inventoryExportEvidence");
        Combat = new(Profile) { RosterKnown = store.Get<bool>("rosterKnown") };
        Combat.ProfileChanged += () => { AcquiredQuestTracking.Apply(Catalog, Profile, Inventory); inspectionIdentityRevision++; planningRevision++; equipmentRevision++; profileNeedsPersistence = true; saveRequested = true; RefreshDetectedPetContext(); };
        var saved = store.LoadEncounters(); var current = store.Get<Encounter>("liveEncounter");
        if (current != null && saved.FirstOrDefault(x => x.Id == current.Id) is { } detailed) { current.Events = detailed.Events; current.Heals = detailed.Heals; current.HealingSummaries = detailed.HealingSummaries; current.HealingCoverage = detailed.HealingCoverage; current.HealingDetailAvailable = detailed.HealingDetailAvailable; }
        Combat.Restore(saved, current); InitializePetResearch(); RefreshDetectedPetContext(); InitializeSessions(); InitializeGroups();
        var pendingSummon=store.Get<PendingPetSummonCheckpoint>("petPendingSummon-v1");
        if(pendingSummon!=null&&pendingSummon.Generation==checkpoint?.Generation&&pendingSummon.Offset==checkpoint.Offset)Combat.Elements.RestorePending(pendingSummon.Summon);
        Combat.Healing.Restore(store.LoadHealing()); Combat.Healing.Reattribute(Combat.Encounters.Concat(current == null ? [] : [current]));
        if (Profile.Meter.EncounterId.Length == 0) Profile.Meter.EncounterId = current?.Id ?? Combat.Encounters.MaxBy(f => f.Start)?.Id ?? "";
        Combat.Saved += e => { if (!dirty.Contains(e)) dirty.Add(e); if (e.Finished) { changedHistoricalSnapshots.Add(e.Id); historicalSnapshotRevision++; } };
        if (demo) { Profile.Panels.Clear(); SeedDemo(); }
        else { ReadInventory(); ReadTheme(); }
    }
    public IReadOnlyList<ObjectiveRow> Objectives { get { if (viewWorker != null) return Published?.Objectives ?? []; lock (Sync) return PlanObjectives(); } }
    public List<string> PendingGear(string text) => text.Length < 3 ? [] : equipmentCatalog?.SearchPending(text) ?? [];
    public DpsViewState DpsView() { if (viewWorker != null && Published is { } snapshot) return snapshot.Combat; lock (Sync) return BuildLabCombat(Combat.Encounters, Combat.Current, SessionStart, Profile.Meter, SessionRecords); }
    public DpsViewState OverlayDpsView() { if (viewWorker != null && Published?.OverlayCombat is { } snapshot) return snapshot; lock (Sync) return DpsViews.Build(Combat.Encounters, Combat.Current, SessionStart, Profile.OverlayMeter!, SessionRecords); }
    public void QueueMetadata(string name) { lock (Sync) metadataQueue.Add(Identity.Canonical(name)); _ = RefreshWikiAsync(true); }
    public void Start(bool offline = false) { if (!Demo) { worker = Task.Run(RunAsync); if (!offline) { catalogWorker = Task.Run(SyncEquipmentAsync); wikiRefreshWorker=Task.Run(WikiRefreshLoop); effectReferenceWorker=Task.Run(EffectReferenceLoop); } viewWorker = Task.Run(PublishLoop); } }
    private void MergeItems(IEnumerable<ItemDefinition> items)
    {
        var batch = items.ToList(); if (batch.Count == 0) return;
        CatalogItemMerge.Apply(Catalog, batch); catalogRevision++; catalogDirty = !Demo;
        if (Profile != null && Inventory != null && AcquiredQuestTracking.Apply(Catalog, Profile, Inventory) > 0) { planningRevision++; saveRequested = profileNeedsPersistence = true; }
    }
    public async Task SearchGearAsync(string text, CancellationToken token)
    {
        if (Demo || text.Trim().Length < 3) return;
        GearSearchStatus = "Searching wiki…";
        try
        {
            var items = await wiki.SearchItemsAsync(text.Trim(), token); token.ThrowIfCancellationRequested();
            lock (Sync) { MergeItems(items); equipmentCatalog?.Store(items, []); GearSearchStatus = items.Count == 0 ? "No matching wiki equipment · cached results shown" : "Wiki results saved locally"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { GearSearchStatus = "Wiki unavailable · local results shown"; }
        Updated?.Invoke();
    }
    private async Task SyncEquipmentAsync()
    {
        if (equipmentCatalog == null) return;
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                if (EquipmentSync.IndexedAt == null || EquipmentSync.IndexedAt < DateTime.UtcNow.AddDays(-1))
                {
                    do
                    {
                        var page = await wiki.ItemTitlesAsync(EquipmentSync.Continuation, cancel.Token);
                        equipmentCatalog.Index(page.Titles); EquipmentSync.Continuation = page.Continuation;
                        EquipmentSync.Status = "Indexing wiki items…"; equipmentCatalog.Counts(EquipmentSync);
                        if (page.Continuation.Length == 0) EquipmentSync.IndexedAt = DateTime.UtcNow;
                        equipmentCatalog.SaveState(EquipmentSync);
                    } while (EquipmentSync.Continuation.Length > 0);
                }
                List<string> priorities;
                lock (Sync) priorities = gearMetadataPriority.Keys.Concat(metadataQueue).Concat(Profile.ActiveLoadout.ConfirmedPetInventory?.Items.Select(i => i.Name) ?? []).Concat(Inventory.Entries.Where(e => !e.IsAugment).Select(e => Identity.Canonical(e.Name))).Concat(Catalog.Items.Where(i=>!i.MetadataComplete).Select(i=>i.Name)).Distinct().Where(n => Catalog.Find(n) is not { MetadataVersion: >= PetItemRules.MetadataVersion, MetadataComplete: true }).Take(20).ToList();
                var names = equipmentCatalog.Pending(DateTime.UtcNow.AddDays(-1), 50, priorities);
                if (names.Count == 0) { EquipmentSync.Status = "Full local catalog up to date"; equipmentCatalog.Counts(EquipmentSync); equipmentCatalog.SaveState(EquipmentSync); await equipmentWake.WaitAsync(TimeSpan.FromMinutes(30), cancel.Token); continue; }
                Dictionary<string, long> revisions; lock (Sync) revisions = Catalog.Items.Where(i => i.Revision > 0 && i.MetadataVersion >= PetItemRules.MetadataVersion).GroupBy(i => Identity.Key(i.Name)).ToDictionary(g => g.Key, g => g.First().Revision);
                var items = await wiki.FetchItemsAsync(names, cancel.Token, revisions);
                equipmentCatalog.Store(items, names);
                lock (Sync) { MergeItems(items); foreach (string name in names) { metadataQueue.Remove(name); gearMetadataPriority.TryRemove(name, out _); } }
                equipmentCatalog.Counts(EquipmentSync); EquipmentSync.Status = $"Catalog sync · {EquipmentSync.Checked:N0}/{EquipmentSync.Discovered:N0} pages checked"; equipmentCatalog.SaveState(EquipmentSync);
                Updated?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception error)
            {
                EquipmentSync.Status = "Catalog sync paused; will retry · " + error.Message; equipmentCatalog.SaveState(EquipmentSync);
                try { await Task.Delay(TimeSpan.FromMinutes(1), cancel.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Save()
    {
        lock (Sync) {
            string equipment = System.Text.Json.JsonSerializer.Serialize(new { Profile.SelectedLoadout, Profile.ActiveLoadout.Equipment, Profile.ActiveLoadout.EquipmentPositions, Profile.ActiveLoadout.EquipmentDraft, Profile.ActiveLoadout.GearBuilds, Profile.ActiveLoadout.SelectedGearBuildId, Profile.UpdateCurrentBuildFromInventory, Profile.SelfRoleVersion, Profile.SelfRoleOverrides, Profile.ActiveLoadout.Classes, Profile.ActiveLoadout.Level, Profile.ActiveLoadout.Deity, Profile.ActiveLoadout.Race, Profile.ActiveLoadout.RaceLevel, OctoberContent.Version, Profile.Weights, Profile.ActiveLoadout.Preset });
            string pets = System.Text.Json.JsonSerializer.Serialize(new { Profile.PetComparison, Profile.PetGoalWeights, Profile.ActiveLoadout.Level, Profile.ActiveLoadout.Classes, Profile.ActiveLoadout.PetPreset, Profile.ActiveLoadout.ConfirmedPetInventory, Profile.ActiveLoadout.PetBuilds, Profile.ActiveLoadout.PetLevel });
            string stats = System.Text.Json.JsonSerializer.Serialize(new { Profile.ActiveLoadout.StatProfiles, Profile.ActiveLoadout.PetStatProfiles });
            if (equipment != savedEquipmentSettings) { savedEquipmentSettings = equipment; equipmentRevision++; }
            if (pets != savedPetSettings) { savedPetSettings = pets; petRevision++; }
            if (stats != savedStatSettings) { savedStatSettings = stats; statProfileRevision++; }
            string settings = PlanningSettings; if (settings != savedPlanningSettings) { savedPlanningSettings = settings; planningRevision++; plannedKey = ""; } saveRequested = true; profileNeedsPersistence = true; }
        Updated?.Invoke();
    }
    public void SetProfileFolder(string gameFolder, string character, string server)
    {
        lock (Sync)
        {
            Combat.Finish(); Flush(); Profile.GameFolder = gameFolder; Profile.Character = character; Profile.Server = server;
            // A character change requires a dedicated process/database; do not mix records.
            Save();
        }
    }
    public int ReaderErrors { get; private set; }
    public DateTime? LastReaderPoll { get; private set; }
    private bool catalogDirty;
    private long committedOffset = -1;
    private bool? storedRosterKnown;
    private readonly RecentIds processedItems = new();
    private async Task RunAsync()
    {
        var parser = new LogParser(Profile.Character); DateTime lastInventory = DateTime.MinValue, nextMetadata = DateTime.Now, retained = DateTime.MinValue; bool following = false;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    ReadSessions(parser); ReadGroups(); EnsureSessionLoaded(); BackfillElements(parser); BackfillItemRanks(parser); ReadStatEvidence(); if (following) { BackfillHealing(parser); BackfillAbilityOutcomes(); } LastReaderPoll = DateTime.Now;
                    var log = GameLogPath;
                    if (!File.Exists(log))
                    {
                        lock (Sync) Status = "Log not found. Check the game folder and enable /log on.";
                    }
                    else
                    {
                        TailBatch batch;
                        try { batch = LogTailer.Read(log, checkpoint); }
                        catch (IOException) { lock (Sync) Status = "Log temporarily unavailable; retrying."; await Task.Delay(500, cancel.Token); continue; }
                        if (batch.Warning != null) { ReaderErrors++; File.AppendAllText(Path.Combine(DataFolder, "errors.log"), DateTime.Now + " " + batch.Warning + Environment.NewLine); }
                        bool historical = !following || batch.Reset;
                        lock (Sync)
                        {
                            if (batch.Reset) { Combat.Elements.RestorePending(null);Combat.Finish(); Status = "Log rotated or truncated; following the new file."; }
                            foreach (var line in batch.Lines)
                            {
                                try
                                {
                                    var parsed = parser.Parse(line.Text, line.Id); if (parsed == null) continue;
                                    if (parsed.ParseWarning != null) { ReaderErrors++; File.AppendAllText(Path.Combine(DataFolder, "errors.log"), $"{DateTime.Now:O} Skipped invalid log record {line.Id}: {parsed.ParseWarning}\n"); continue; }
                                    if (!LastLogActivity.HasValue || parsed.Time > LastLogActivity) LastLogActivity = parsed.Time;
                                    ObserveSync(parsed, line.Id, historical); ObserveAbilityOutcome(parsed.AbilityOutcome);
                                    Combat.Process(parsed); ObservePetResearch(parsed, line.Id);
                                    if (parsed.Item is { } item && processedItems.Accept(line.Id))
                                    {
                                        var definition = Catalog.Find(item.Name);
                                        var prior = historical ? [] : Objectives.Where(x => x.Remaining > 0 && (Identity.Key(x.Item) == Identity.Key(item.Name) || definition?.Key == x.Key)).ToList();
                                        observations.Add((line.Id, item)); recentObservationEvidence.Add((line.Id, item)); recentObservationEvidence.RemoveAll(e => e.Item.Time < DateTime.Now.AddMinutes(-10)); InventoryParser.Apply(Inventory, item, line.Id); planningRevision++;
                                        if (item.Action is "Looted" or "Received" or "Created") { if (AcquiredQuestTracking.Apply(Catalog, Profile, Inventory, [item.Name]) > 0) saveRequested = profileNeedsPersistence = true; }
                                        LearnDrop(item);
                                        if (Catalog.Find(item.Name)?.MetadataComplete != true) { metadataQueue.Add(Identity.Canonical(item.Name)); RequestGearMetadata(item.Name); }
                                        if (!historical) HandleAlert(item, prior);
                                        if (!historical) liveObservations[Identity.Key(item.Name)] = item;
                                    }
                                }
                                catch (Exception error) when (error is FormatException or OverflowException or ArgumentException or InvalidDataException)
                                { ReaderErrors++; File.AppendAllText(Path.Combine(DataFolder, "errors.log"), $"{DateTime.Now:O} Skipped invalid log record {line.Id}: {error.GetType().Name} {error.Message}\n"); }
                            }
                            checkpoint = batch.Checkpoint; Combat.Tick(DateTime.Now); Flush();
                            following = batch.CaughtUp; if (batch.Reset) { healingCaughtUp = false; outcomesCaughtUp = false; }
                            Status = batch.CaughtUp ? $"Following {Profile.Character} · {Profile.Server} · last log {File.GetLastWriteTime(log):MMM dd HH:mm}" : $"Importing log history · {checkpoint.Offset / 1048576d:N1} MB read";
                        }
                        Updated?.Invoke();
                        if (!batch.CaughtUp) { await Task.Delay(5, cancel.Token); continue; }
                        if (DateTime.Now >= nextMetadata && metadataQueue.Count > 0) { nextMetadata = DateTime.Now.AddHours(1); _ = RefreshWikiAsync(true); }
                    }
                    lock (Sync)
                    {
                        if ((DateTime.Now - lastInventory).TotalSeconds > 3) { ReadInventory(); ReadTheme(); lastInventory = DateTime.Now; }
                        if (retained.Date != DateTime.Now.Date)
                        {
                            var cutoff = DateTime.Now.AddDays(-30); QueuePersistence(() => store.Retain(cutoff)); Combat.Healing.Retain(cutoff); foreach (var f in Combat.Encounters) { if (f.Heals.RemoveAll(e => e.Time < cutoff) > 0) f.HealingDetailAvailable = false; }
                            foreach (var fight in Combat.Encounters.Where(f => f.Events.Any(e => e.Time < cutoff))) { fight.Events.RemoveAll(e => e.Time < cutoff); fight.DetailAvailable = fight.Events.Count > 0 && fight.Events.Where(x => x.Owner is "Pet" or "Player").Sum(x => x.Damage) == fight.Damage; }
                            historyRevision++; retained = DateTime.Now;
                        }
                    }
                    Updated?.Invoke();
                    await Task.Delay(500, cancel.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    ReaderErrors++; lock (Sync) Status = "Reader recovering · retrying: " + error.Message;
                    File.AppendAllText(Path.Combine(DataFolder, "errors.log"), DateTime.Now + " Reader retry: " + error + Environment.NewLine);
                    Updated?.Invoke(); await Task.Delay(1000, cancel.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { lock (Sync) Status = "Reader paused: " + error.Message + ". Restart after correcting the file/folder."; Updated?.Invoke(); File.AppendAllText(Path.Combine(DataFolder, "errors.log"), DateTime.Now + " " + error + Environment.NewLine); }
    }
    private void Flush()
    {
        FlushAbilityOutcomes();
        // Called while capturing ingestion state. Database work runs on one ordered writer.
        if (catalogDirty) { var frozen = new Catalog { Items = Catalog.Items.ToList(), Quests = Catalog.Quests.ToList(), SchemaVersion = Catalog.SchemaVersion, Documents = Catalog.Documents.ToList(), Zones = Catalog.Zones.ToList() }; QueuePersistence(() => store.Set("catalog", frozen)); catalogDirty = false; }
        if (checkpoint != null && (checkpoint.Offset != committedOffset || dirty.Count > 0 || observations.Count > 0 || saveRequested || Combat.Healing.PendingWrites.Count > 0))
        {
            using var measurement = UpdateDiagnostics.Measure("Ingestion persistence capture");
            string inventoryVersion = $"{Inventory.Revision}|{Inventory.CapturedAt.Ticks}|{Inventory.ExportLogId}";
            bool saveProfile = profileNeedsPersistence || saveRequested, saveInventory = inventoryVersion != persistedInventoryVersion;
            var profile = saveProfile ? EquipmentPositions.Copy(Profile) : null;
            var inventory = saveInventory ? EquipmentPositions.Copy(Inventory) : null;
            var point = EquipmentPositions.Copy(checkpoint); var fights = dirty.Select(CombatSnapshots.Copy).ToList(); var current = Combat.Current == null ? null : CombatSnapshots.Copy(Combat.Current);
            var items = observations.ToArray(); var healingWrites = Combat.Healing.PendingWrites.Select(e => e.Copy()).ToArray();
            var pending=new PendingPetSummonCheckpoint(point.Generation,point.Offset,Combat.Elements.Pending);
            QueuePersistence(() => {store.Commit(point, profile!, inventory!, fights, current, items, healingWrites, saveProfile, saveInventory);store.Set("petPendingSummon-v1",pending);});
            Combat.Healing.MarkSaved(healingWrites); committedOffset = checkpoint.Offset; saveRequested = profileNeedsPersistence = false; persistedInventoryVersion = inventoryVersion;
        }
        dirty.Clear(); observations.Clear(); if (storedRosterKnown != Combat.RosterKnown) { bool known = Combat.RosterKnown; QueuePersistence(() => store.Set("rosterKnown", known)); storedRosterKnown = known; }
    }
    public void ReadInventory()
    {
        if (string.IsNullOrWhiteSpace(Profile.GameFolder)) return;
        var path = Path.Combine(Profile.GameFolder, $"{Profile.Character}_{Profile.Server}-Inventory.txt");
        if (!File.Exists(path)) return;
        var captured = File.GetLastWriteTime(path);
        bool newAnchor = latestExport is { } anchor && Math.Abs((anchor.Time - captured).TotalSeconds) < 10 && anchor.Id != Inventory.ExportLogId;
        if (captured <= Inventory.CapturedAt && Inventory.FilePath == path && !newAnchor && Inventory.EquipmentPositions.Count > 0) return;
        if (SyncRequest.Active && !SyncRequest.Export) return;
        var before = new FileInfo(path); long size = before.Length; var updated = before.LastWriteTimeUtc;
        if ((DateTime.UtcNow - updated).TotalSeconds < 2) return;
        try
        {
            var snapshot = InventoryParser.Parse(File.ReadAllText(path), captured, path);
            var after = new FileInfo(path);
            if (after.Length != size || after.LastWriteTimeUtc != updated || !snapshot.Complete) { Status = "Inventory export incomplete; previous snapshot retained."; return; }
            if (latestExport is { } evidence && Math.Abs((evidence.Time - captured).TotalSeconds) < 10) snapshot.ExportLogId = evidence.Id;
            foreach (var (id, item) in store.ObservationRecordsAfter(captured.AddSeconds(-10)).Concat(recentObservationEvidence.Where(e => e.Item.Time >= captured.AddSeconds(-10))).DistinctBy(e => e.Id)) InventoryParser.Apply(snapshot, item, id);
            Inventory = snapshot; QuestRecipes.ApplyIdentities(Profile, Inventory); if (AcquiredQuestTracking.Apply(Catalog, Profile, Inventory) > 0) profileNeedsPersistence = true; planningRevision++; GearBuildEditing.ApplyExport(Profile, Inventory);
            if (SyncRequest.Active && SyncRequest.Export && updated > SyncRequest.InventoryWriteUtc && snapshot.ExportLogId.Length > 0 && LogPosition.After(snapshot.ExportLogId, SyncRequest.Generation + ":" + (SyncRequest.BaselineLength - 1))) SyncRequest.Imported = true;
            foreach (var e in Inventory.Entries.Where(x => !x.IsAugment && Catalog.Find(x.Name, x.GameId == 0 ? null : x.GameId) == null)) metadataQueue.Add(Identity.Canonical(e.Name));
            saveRequested = true;
        }
        catch (IOException) { Status = "Inventory export is being written; retrying."; }
        catch (InvalidDataException e) { Status = e.Message; }
    }
    private void ReadTheme()
    {
        if (Profile.Theme != "Auto") { ThemeName = Profile.Theme; return; }
        ThemeName = "coldain";
        try
        {
            var path = Directory.Exists(Profile.GameFolder) ? Directory.GetFiles(Profile.GameFolder, $"UI_{Profile.Character}_{Profile.Server}_LO*.ini").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (path != null && GameSettingsReader.TryReadText(path, out var text))
            { var match = Regex.Match(text, @"^UISkin=(.+)$", RegexOptions.Multiline); if (match.Success && match.Groups[1].Value.Trim().Length > 0) ThemeName = match.Groups[1].Value.Trim(); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private void HandleAlert(ItemObservation item, List<ObjectiveRow> prior)
    {
        var resolved = Catalog.Find(item.Name);
        var matches = prior.Count > 0 ? prior : Objectives.Where(x => x.Remaining > 0 && (Identity.Key(x.Item) == Identity.Key(item.Name) || resolved?.Key == x.Key)).ToList();
        if (matches.Count > 0)
        { Alert = $"KEEP · {item.Name} · {string.Join(" / ", matches.Select(x => x.Class).Distinct())} · {item.Action}"; if (item.Action is "Sold" or "Destroyed" or "Merged") Alert = "NEEDED ITEM " + item.Action.ToUpperInvariant() + " · " + item.Name; if (Profile.Sounds) System.Media.SystemSounds.Exclamation.Play(); }
        else if (Catalog.Find(item.Name) is { } definition && definition.Slots.Count > 0)
        {
            foreach (bool pet in new[] { false, true })
            {
                var preset = pet ? Profile.ActiveLoadout.PetPreset : Profile.ActiveLoadout.Preset;
                var role = pet ? null : SelfRoleRatings.Resolve(Profile, Profile.ActiveLoadout, preset);
                var weights = pet ? PetRoleRatings.Weights(Profile,preset) : role?.Weights ?? Profile.Weights.GetValueOrDefault(preset, GearScorer.Defaults["Pet damage"]);
                var candidate = GearRecommendations.For(definition, Identity.Rank(item.Name), Catalog, Profile.ActiveLoadout, pet, weights, role).FirstOrDefault(x => x.Evaluation is { } e && (e.Unbuffed.Delta > 0 || e.Buffed.Delta > 0));
                if(pet && PetRoleRatings.Normalize(preset)=="Balanced") continue;
                if (candidate != null) { Alert = $"POTENTIAL {(pet ? "PET" : "PLAYER")} UPGRADE · {item.Name} · {item.Action} · +{candidate.Delta:N1} {preset} points ({candidate.Confidence})"; break; }
            }
        }
    }
    private void LearnDrop(ItemObservation observation)
    {
        if (observation.Action is not ("Looted" or "Sold" or "Merged")) return;
        if (observation.Source.Length == 0 || Profile.Zone == "Unknown") return;
        var item = Catalog.Find(observation.Name);
        if (item == null && !Catalog.Items.Any(x => x.Aliases.Any(a => Identity.Key(a) == Identity.Key(observation.Name))))
        {
            item = new ItemDefinition { Name = Identity.Canonical(observation.Name), Key = Identity.Key(observation.Name), WikiUrl = "https://eqlwiki.com/" + Uri.EscapeDataString(Identity.Canonical(observation.Name).Replace(' ', '_')) }; Catalog.Items.Add(item);
        }
        if (item != null && !item.Sources.Any(s => s.Zone == Profile.Zone && s.Mob == observation.Source && s.Availability == "Observed drop" && s.Era == Profile.CurrentExpansion))
        { item = EquipmentPositions.Copy(item); Catalog.Items = Catalog.Items.Select(i => i.Key == item.Key ? item : i).ToList(); Catalog.InvalidateIndexes(); item.Sources.Add(new() { Zone = Profile.Zone, Mob = observation.Source, Method = "Drop", Era = Profile.CurrentExpansion, Availability = "Observed drop", Notes = "Observed in this character's log " + observation.Time.ToString("yyyy-MM-dd") }); catalogDirty = true; catalogRevision++; }
    }
    private void BackfillElements(LogParser parser)
    {
        if (Profile.EvidenceVersion >= 6) return;
        bool metadataOnly=Profile.EvidenceVersion>=4; CharacterProfile evidenceProfile;lock(Sync)evidenceProfile=EquipmentPositions.Copy(Profile);
        string log = GameLogPath; if (!File.Exists(log)) return;
        var detector = new PetElementDetector(); var groupProfile = new CharacterProfile(); var groupEngine = new CombatEngine(groupProfile); DateTime cutoff = DateTime.Now.AddDays(-30); int count = 0;
        lock (Sync) Status = "Reviewing retained pet summon evidence…";
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } text)
        {
            if ((++count & 1023) == 0) cancel.Token.ThrowIfCancellationRequested();
            ParsedLine? parsed; try { parsed = parser.Parse(text); } catch (Exception error) when (error is FormatException or OverflowException or ArgumentException) { continue; }
            if (parsed is not { } line || line.Time < cutoff) continue;
            if (!metadataOnly && (line.JoinedGroup || line.LeftGroup || line.GroupRosterReset || line.GroupJoined != null || line.GroupLeft != null || line.AcceptedInviter != null)) groupEngine.Process(line);
            if (line.PlayerCast != null || line.PetActivity != null || line.PetName != null || line.Combat != null || line.CastFailed || line.PetLost || line.SlainActor != null) detector.Process(line,evidenceProfile);
        }
        lock (Sync)
        {
            Profile.EvidenceVersion = 6;
            foreach(var identity in Profile.PetIdentities)
            {
                var recovered=evidenceProfile.PetIdentities.FirstOrDefault(p=>p.Name==identity.Name&&p.From==identity.From);
                if(recovered==null)continue;
                if(identity.ElementConfidence!="User reviewed"&&(!metadataOnly||recovered.ElementConfidence=="Correlated summon")){identity.Element=recovered.Element;identity.ElementConfidence=recovered.ElementConfidence;identity.ElementEvidence=recovered.ElementEvidence;identity.ElementObservedAt=recovered.ElementObservedAt;}
                identity.RawSummonSpell=recovered.RawSummonSpell;identity.CanonicalSummonSpell=recovered.CanonicalSummonSpell;identity.SummonRank=recovered.SummonRank;identity.SummonCasterClass=recovered.SummonCasterClass;identity.SummonedAt=recovered.SummonedAt;identity.SummonEvidence=recovered.SummonEvidence;
            }
            inspectionIdentityRevision++;RefreshDetectedPetContext();
            if(metadataOnly){Save();return;}
            if (!Profile.GroupMembers.Any(x => x.Source == "Manual")) { Profile.GroupMembers = groupProfile.GroupMembers; Profile.InGroup = groupProfile.InGroup; Profile.GroupRosterIncomplete = groupProfile.GroupRosterIncomplete; }
            if (Profile.PetElementOverride.Length == 0) Profile.ActiveLoadout.PetType = Profile.EffectivePetElement;
            foreach (var fight in Combat.Encounters)
            {
                bool changed = false;
                if (fight.DetailAvailable && fight.Peaks.Count == 0) { fight.Peaks = CombatMetrics.Peaks([fight], new("Combined")); changed = true; }
                var identity = Profile.PetIdentities.LastOrDefault(x => x.From <= fight.End && (!x.Until.HasValue || x.Until >= fight.Start) && x.ElementConfidence == "Correlated summon");
                if (identity != null && fight.Loadout is "" or "Current" && fight.PetElementEvidence.Length == 0) { fight.PetType = identity.Element; fight.PetName = identity.Name; fight.PetElementEvidence = identity.ElementEvidence; changed = true; }
                if (changed) dirty.Add(fight);
            }
            Save();
        }
    }
    public void CorrectGroup(IEnumerable<(string Name, bool Pet)> members)
    {
        lock (Sync)
        {
            DateTime now = DateTime.Now; foreach (var member in Profile.GroupMembers.Where(x => x.Until == null)) member.Until = now;
            foreach (var (name, pet) in members.Where(x => x.Name.Length > 0 && !x.Name.Equals(Profile.Character, StringComparison.OrdinalIgnoreCase)).DistinctBy(x => x.Name.ToLowerInvariant())) Profile.GroupMembers.Add(new() { Name = name, From = now, IsPet = pet, Source = "Manual" });
            Profile.InGroup = Profile.GroupMembers.Any(x => x.Until == null); Profile.GroupRosterIncomplete = false; inspectionIdentityRevision++; Save();
        }
    }
    public async Task RefreshWikiAsync(bool onlyPending = false)
    {
        lock (Sync) { if (refreshing) return; refreshing = true; Status = "Refreshing cached item metadata…"; }
        Updated?.Invoke();
        try
        {
            List<string> names;
            lock (Sync) names = onlyPending ? metadataQueue.Take(100).ToList() : Catalog.Items.Select(x => x.Name).Concat(metadataQueue).Distinct().ToList();
            foreach (var batch in names.Chunk(20))
            {
                Dictionary<string, long> revisions;
                lock (Sync) revisions = Catalog.Items.Where(x => x.Revision > 0 && x.MetadataVersion >= PetItemRules.MetadataVersion).GroupBy(x => Identity.Key(x.Name)).ToDictionary(g => g.Key, g => g.First().Revision);
                var items = await wiki.FetchItemsAsync(batch, cancel.Token, revisions);
                lock (Sync)
                {
                    foreach (var item in items)
                    {
                        if (item.GameId == null) { var ids = Inventory.Entries.Where(e => !e.IsAugment && Identity.Key(e.Name) == item.Key && e.GameId > 0).Select(e => e.GameId).Distinct().ToList(); if (ids.Count == 1) item.GameId = ids[0]; }
                    }
                    MergeItems(items);
                    foreach (var item in items)
                    {
                        metadataQueue.Remove(item.Name);
                        var recent = liveObservations.Values.LastOrDefault(x => x.Time >= DateTime.Now.AddMinutes(-5) && (Identity.Key(x.Name) == item.Key || item.Aliases.Any(a => Identity.Key(a) == Identity.Key(x.Name))));
                        if (recent != null) HandleAlert(recent, []);
                    }
                    catalogRevision++; Catalog.InvalidateIndexes(); catalogDirty = true; Status = $"Wiki cache updated · {items.Count} items in this batch";
                }
                Updated?.Invoke();
            }
            if (!onlyPending)
            {
                var revisions = await wiki.CheckRevisionsAsync(Catalog.Quests.Select(x => x.Name), cancel.Token);
                List<string> changedStarts;lock(Sync)changedStarts=revisions.Where(r=>Catalog.Quests.Any(q=>q.Name==r.Title&&q.StartRevision!=r.Revision)).Select(r=>r.Title).ToList();
                var starts=await wiki.QuestStartsAsync(changedStarts,cancel.Token);
                lock(Sync)foreach(var start in starts){var q=Catalog.Quests.FirstOrDefault(q=>q.Name==start.Name);if(q==null)continue;var updated=EquipmentPositions.Copy(q);updated.StartRevision=start.Revision;updated.StartZone=start.StartZone;updated.StartNpc=start.StartNpc;updated.StartEvidence=start.Source;Catalog.Quests=Catalog.Quests.Select(q=>q.Id==updated.Id?updated:q).ToList();catalogRevision++;planningRevision++;}
                lock (Sync)
                {
                    foreach (var document in revisions)
                    {
                        var quest = Catalog.Quests.FirstOrDefault(q => q.Name == document.Title);
                        if (quest != null && quest.Revision != document.Revision && !quest.Coverage.Contains("New wiki revision", StringComparison.Ordinal)) quest.Coverage += $" · New wiki revision {document.Revision}; rebuild/review this cached recipe graph";
                    }
                    catalogDirty = true; DateTime checkedAt = DateTime.Now;Profile.LastWikiRefreshAt=checkedAt;QueuePersistence(() => store.Set("lastWikiRevisionCheck", checkedAt));Save();
                }
            }
            lock (Sync) Status = "Wiki item cache refreshed. Quest recipes retain the bundled, attributed snapshot.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { lock (Sync) Status = "Wiki unavailable; cached data remains usable. " + e.Message; }
        finally { lock (Sync) refreshing = false; Updated?.Invoke(); }
    }
    public void CorrectPet(string name, DateTime from, DateTime? until)
    {
        lock (Sync) { Combat.ConfirmPet(name, from, until); historyRevision++; var corrected = dirty.Select(CombatSnapshots.Copy).ToArray(); QueuePersistence(() => { foreach (var encounter in corrected) store.SaveEncounter(encounter); }); dirty.Clear(); Save(); }
    }
    public void DeleteEncounter(Encounter encounter)
    { lock (Sync) { string removedId = encounter.Id; QueuePersistence(() => store.DeleteEncounter(removedId)); dirty.RemoveAll(f => f.Id == removedId); Combat.Healing.Delete(encounter.Id); Combat.Encounters.Remove(encounter); historyRevision++; } Updated?.Invoke(); }
    public void LoadOlder()
    {
        DateTime? oldest; lock (Sync) oldest = Combat.Encounters.Select(x => (DateTime?)x.Start).Min();
        var old = store.LoadEncounters(oldest, 500);
        lock (Sync) { Combat.Encounters.AddRange(old.Where(f => Combat.Encounters.All(x => x.Id != f.Id))); historyRevision++; Status = $"Loaded {old.Count} older encounters"; }
        Updated?.Invoke();
    }
    public void SeedDemo()
    {
        Profile.Zone = "Permafrost"; Profile.HideWhenUnfocused = false; Profile.Meter = new(); Profile.ColumnWidths.Clear(); Profile.PanelSplits.Clear();
        Profile.CurrentClasses = ["SHD", "BRD", "MAG"]; Profile.OptionalClasses = ["WAR"]; Profile.ActiveLoadout.Classes = Profile.CurrentClasses.ToList(); Profile.ActiveLoadout.Level = 50; Profile.ActiveLoadout.Race = "High Elf"; Profile.ActiveLoadout.PetType = "Water";
        Profile.ActiveLoadout.PetName = "Mistling";
        // Public previews are generated solely from illustrative data, never a local export.
        Inventory = new() { CapturedAt = DateTime.Now, Complete = true, Entries = new[] { ("Cloak of Flames", "Back"), ("Indicolite Breastplate", "Chest"), ("Engineer's Ring", "Bag 1") }.Select(pair => new InventoryEntry { Name = pair.Item1, Location = pair.Item2, Count = 1 }).ToList() };
        var start = DateTime.Now.AddMinutes(-3); SessionStart = start; Profile.PetIdentities = [new() { Name = "Mistling", From = start.AddHours(-1), Confirmed = true, Element = "Water", ElementConfidence = "Preview fixture", ElementEvidence = "Illustrative Water pet; live detection uses logged evidence." }];
        var parser = new LogParser(Profile.Character);
        for (int i = 0; i <= 84; i += 2)
        {
            var t = start.AddSeconds(i); string stamp = t.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var line in new[] { $"[{stamp}] Mistling slashes an ice giant for {128 + i % 11 * 7} points of damage.", $"[{stamp}] Mistling backstabs an ice giant for {650 + i * 3} points of damage. (Critical)", $"[{stamp}] You hit an ice giant for {250 + i} points of magic damage by Spirit Tap.", $"[{stamp}] Mistling hit an ice giant for 84 points of cold damage by Frost Strike.", $"[{stamp}] You slashes an ice giant for 142 points of damage." })
                if (parser.Parse(line, "demo:" + Guid.NewGuid()) is { } parsed) Combat.Process(parsed);
            if (i % 4 == 0) foreach (var line in new[] { $"[{stamp}] Mistling pierces an ice giant for 190 points of damage.", $"[{stamp}] Mistling cleaves an ice giant for 112 points of damage.", $"[{stamp}] Mistling kicks an ice giant for 48 points of damage.", $"[{stamp}] Mistling bashes an ice giant for 16 points of damage.", $"[{stamp}] Mistling hit an ice giant for 150 points of magic damage by Scream of Death Strike.", $"[{stamp}] Mistling hit an ice giant for 96 points of poison damage by Spirit of Vermin Strike IV." })
                if (parser.Parse(line, "demo:" + Guid.NewGuid()) is { } parsed) Combat.Process(parsed);
        }
        for (int i = 0; i <= 84; i += 4) { string stamp = start.AddSeconds(i).ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture); foreach (var text in new[] { $"You healed {Profile.Character} for {125 + i} ({250 + i}) hit points by Spirit Tap.", $"Mistling healed himself for {65 + i} hit points by Scream of Death Strike.", $"You healed Mistling over time for 112 hit points by Regrowth. (Critical)", $"Helpfulcleric healed {Profile.Character} for 240 hit points by Greater Healing." }) if (parser.Parse($"[{stamp}] {text}", "demo-healing:" + Guid.NewGuid()) is { } heal) Combat.Process(heal); }
        HealingImportStatus = "PREVIEW · illustrative healing fixture";
        Combat.Finish(); dirty.Clear(); Status = "PREVIEW · synthetic combat and inventory sample";
        foreach (var fight in Combat.Encounters) { for (int i = 0; i < fight.Events.Count; i++) if (i % 3 == 0) fight.Events[i].Target = "an ice goblin wizard"; CombatEngine.Recount(fight); }
        Alert = "KEEP · Heart of Frost · WAR epic · a goblin wizard";
    }
    private int disposeStarted;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0) return;
        cancel.Cancel(); try { Task.WaitAll(new[] { worker, catalogWorker, viewWorker, inspectionWorker,wikiRefreshWorker,effectReferenceWorker }.OfType<Task>().ToArray(), TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        lock (Sync) { SavePetResearch(); if (!Demo) { Flush(); var finalProfile = EquipmentPositions.Copy(Profile); var finalInventory = EquipmentPositions.Copy(Inventory); bool finalRoster = Combat.RosterKnown; QueuePersistence(() => { store.Set("profile", finalProfile); store.Set("inventory", finalInventory); store.Set("rosterKnown", finalRoster); store.Retain(DateTime.Now.AddDays(-30)); }); } }
        persistence.Writer.TryComplete(); if (persistenceWorker != null) persistenceWorker.GetAwaiter().GetResult();
        wiki.Dispose(); equipmentCatalog?.Dispose(); store.Dispose(); cancel.Dispose();
    }
}
