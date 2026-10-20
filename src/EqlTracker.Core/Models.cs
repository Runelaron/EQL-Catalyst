using System.Text.Json;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException(path);
    public static void Write<T>(string path, T value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options)); File.Move(tmp, path, true); }
}

public static partial class Identity
{
    [GeneratedRegex(@"\s+\+(\d+)\s*$")] private static partial Regex RankPattern();
    [GeneratedRegex(@"\s+\((?:Exaltation|Ornamentation)\)\s*$", RegexOptions.IgnoreCase)] private static partial Regex AugmentPattern();
    public static int Rank(string name) => int.TryParse(RankPattern().Match(name).Groups[1].Value, out var n) ? n : 0;
    public static string Canonical(string name) => RankPattern().Replace(AugmentPattern().Replace(name.Trim(), ""), "").Replace('`', '\'').Replace('’', '\'').Normalize().Trim();
    public static string Key(string name) => Canonical(name).ToLowerInvariant();
    public static bool IsAugment(string name) => AugmentPattern().IsMatch(name);
    public static string Zone(string name) => Regex.Replace(name, @"\s+(?:\d+|\((?:Solo|Group|Raid).*\)|D[0-4].*)$", "", RegexOptions.IgnoreCase).Trim();
    public static string ZoneKey(string name) => ZoneNames.Key(Zone(name).Replace("Clan Crushbone", "Crushbone", StringComparison.OrdinalIgnoreCase).Replace("The ", "", StringComparison.OrdinalIgnoreCase).Replace("Eastern Karana", "East Karana", StringComparison.OrdinalIgnoreCase).Replace("Southern Karana", "South Karana", StringComparison.OrdinalIgnoreCase).Replace("Plane of Air", "Plane of Sky", StringComparison.OrdinalIgnoreCase).Replace("The Ruins of Old Paineel", "The Hole", StringComparison.OrdinalIgnoreCase).Replace("Ruins of Old Paineel", "Hole", StringComparison.OrdinalIgnoreCase).ToLowerInvariant().Trim());
}

public sealed class Catalog
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime GeneratedAt { get; set; }
    public List<QuestDefinition> Quests { get; set; } = [];
    public List<ItemDefinition> Items { get; set; } = [];
    public List<SourceDocument> Documents { get; set; } = [];
    public List<string> Zones { get; set; } = [];
    private readonly object lookupSync = new();
    private int indexedCount = -1;
    private List<ItemDefinition>? indexedList;
    private Dictionary<string, ItemDefinition> keys = [];
    private Dictionary<int, List<ItemDefinition>> ids = [];
    private Dictionary<string, List<ItemDefinition>> names = [];
    public void InvalidateIndexes() => indexedCount = -1;
    private void Index()
    {
        if (indexedCount == Items.Count && ReferenceEquals(indexedList, Items)) return;
        var items = Items;
        keys = items.GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
        ids = items.Where(i => i.GameId.HasValue).GroupBy(i => i.GameId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        names = items.SelectMany(i => i.Aliases.Prepend(i.Name).Select(n => (Key: Identity.Key(n), Item: i))).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Select(x => x.Item).Distinct().ToList());
        indexedList = items; indexedCount = items.Count;
    }
    public ItemDefinition? FindKey(string key) { lock (lookupSync) { Index(); return keys.GetValueOrDefault(key); } }
    public ItemDefinition? Find(string name, int? id = null)
    {
        lock (lookupSync)
        {
        Index();
        if (id.HasValue && ids.TryGetValue(id.Value, out var byId) && byId.Count == 1) return byId[0];
        return names.TryGetValue(Identity.Key(name), out var byName) && byName.Count == 1 ? byName[0] : null;
        }
    }
}
public sealed class SourceDocument
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public long Revision { get; set; }
    public string Timestamp { get; set; } = "";
}
public sealed class ItemSource
{
    public string EvidenceId { get; set; } = "";
    public long Revision { get; set; }
    public string WikiUrl { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Mob { get; set; } = "";
    public string Method { get; set; } = "Drop";
    public string Coordinates { get; set; } = "";
    public string Era { get; set; } = "Classic";
    public string Availability { get; set; } = "Unverified";
    public string Notes { get; set; } = "";
    public override string ToString() => $"{Zone} · {Mob} ({Method}; {Availability})";
}
public sealed class ItemDefinition
{
    public List<QuestStep> CraftRecipes { get; set; } = [];
    public List<ItemEffectEvidence> EffectEvidence { get; set; } = [];
    public ExpansionProvenance ExpansionProvenance { get; set; } = new();
    public ContentAvailability ContentAvailability { get; set; }
    public string AvailabilityEvidence { get; set; } = "";
    public ItemDisplayMetadata Display { get; set; } = new();
    public string IntroducedExpansion { get; set; } = "Unknown";
    public string ExpansionEvidence { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int? GameId { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<string> Classes { get; set; } = [];
    public List<string> Races { get; set; } = [];
    public List<string> Slots { get; set; } = [];
    public string Deity { get; set; } = "";
    public bool TwoHanded { get; set; }
    public int RequiredLevel { get; set; }
    public int? EffectActivationLevel { get; set; }
    public Dictionary<string, double> Stats { get; set; } = [];
    public List<string> Effects { get; set; } = [];
    public List<ItemSource> Sources { get; set; } = [];
    public string WikiUrl { get; set; } = "";
    public long Revision { get; set; }
    public bool MetadataComplete { get; set; }
    public int MetadataVersion { get; set; }
    public bool RequiresSourceIdentity { get; set; }
    public bool RequiresExplicitIdentity { get; set; }
}
public sealed class ItemQuantity
{
    public bool Consumed { get; set; } = true;
    public string ItemKey { get; set; } = "";
    public int Count { get; set; } = 1;
}
public sealed class QuestStep
{
    public RecipeKind Kind { get; set; }
    public string RouteGroup { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public long Revision { get; set; }
    public bool RandomReward { get; set; }
    public string Container { get; set; } = "";
    public List<QuestCondition> Conditions { get; set; } = [];
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string WikiUrl { get; set; } = "";
    public List<ItemQuantity> Inputs { get; set; } = [];
    public List<ItemQuantity> Outputs { get; set; } = [];
    public List<string> Prerequisites { get; set; } = [];
    public string Route { get; set; } = "Default";
    public bool Optional { get; set; }
}
public sealed class QuestDefinition
{
    public int RecipeVersion { get; set; }
    public string ChecklistCoverage { get; set; } = "Legacy";
    public string Kind { get; set; } = "Epic";
    public string SetName { get; set; } = "";
    public List<string> EligibleClasses { get; set; } = [];
    public int RecommendedLevel { get; set; }
    public string Expansion { get; set; } = "Kunark";
    public string Id { get; set; } = "";
    public string Class { get; set; } = "";
    public string Name { get; set; } = "";
    public string WikiUrl { get; set; } = "";
    public long Revision { get; set; }
    public List<QuestStep> Steps { get; set; } = [];
    public List<ItemQuantity> Requirements { get; set; } = [];
    public string RewardKey { get; set; } = "";
    public List<List<string>> RewardAlternatives { get; set; } = [];
    public string Coverage { get; set; } = "Wiki checklist";
}
public sealed class InventoryEntry
{
    public string ConfirmedItemKey { get; set; } = "";
    public override string ToString() => Name + (Location.Length > 0 ? " · " + Location : "");
    public string EffectText { get; set; } = "";
    public string EffectIdentity { get; set; } = "";
    public List<string> TransferredOutEffects { get; set; } = [];
    public string Location { get; set; } = "";
    public string Name { get; set; } = "";
    public int GameId { get; set; }
    public int? Count { get; set; }
    public int Rank => Identity.Rank(Name);
    public bool IsAugment => Identity.IsAugment(Name) || Location.Equals("Augmentation", StringComparison.OrdinalIgnoreCase);
    public bool IsCollection => Count == null;
    public bool Estimated { get; set; }
    public string AcquiredFrom { get; set; } = "";
    public List<InventoryEntry> Augments { get; set; } = [];
}
public sealed class InventorySnapshot
{
    public List<EquipmentPosition> EquipmentPositions { get; set; } = [];
    public DateTime CapturedAt { get; set; }
    public DateTime? LastObservedChangeAt { get; set; }
    public string ExportLogId { get; set; } = "";
    public DateTime UpdatedAt => LastObservedChangeAt > CapturedAt ? LastObservedChangeAt.Value : CapturedAt;
    public List<InventoryEntry> Entries { get; set; } = [];
    public string FilePath { get; set; } = "";
    public bool Complete { get; set; }
    public string Warning { get; set; } = "";
    private int indexedCount = -1;
    private List<InventoryEntry>? indexedEntries;
    private Dictionary<int, List<InventoryEntry>> byId = [];
    private Dictionary<string, List<InventoryEntry>> byName = [];
    public long Revision { get; private set; }
    public void Invalidate() { indexedCount = -1; Revision++; }
    private readonly object identityIndexSync = new();
    private List<InventoryEntry> Matching(ItemDefinition item)
    {
        lock (identityIndexSync)
        {
        if (indexedCount != Entries.Count || !ReferenceEquals(indexedEntries, Entries))
        {
            var physical = Entries.Where(e => !e.IsAugment).ToList();
            byId = physical.Where(e => e.GameId != 0).GroupBy(e => e.GameId).ToDictionary(g => g.Key, g => g.ToList());
            byName = physical.GroupBy(e => Identity.Key(e.Name)).ToDictionary(g => g.Key, g => g.ToList());
            indexedCount = Entries.Count; indexedEntries = Entries;
        }
        var candidates = new HashSet<InventoryEntry>();
        if (item.GameId.HasValue && byId.TryGetValue(item.GameId.Value, out var ids)) candidates.UnionWith(ids);
        foreach (var name in item.Aliases.Prepend(item.Name))
            if (byName.TryGetValue(Identity.Key(name), out var names)) candidates.UnionWith(names);
        return candidates.Where(e => Matches(e, item)).ToList();
            }
    }
    public int Count(ItemDefinition item)
    {
        var rows = Matching(item); int physical = rows.Sum(e => e.Count ?? 0);
        return physical > 0 ? physical : rows.Any(e => e.IsCollection) ? 1 : 0;
    }
    public IReadOnlyList<InventoryEntry> Evidence(ItemDefinition item) => Matching(item);
    public bool Matches(InventoryEntry e, ItemDefinition item)
    {
        if (e.ConfirmedItemKey.Length > 0) return e.ConfirmedItemKey == item.Key;
        if (item.GameId.HasValue && e.GameId != 0) return e.GameId == item.GameId;
        if (Identity.Key(e.Name) == Identity.Key(item.Name)) return true;
        if (!item.Aliases.Any(a => Identity.Key(a) == Identity.Key(e.Name))) return false;
        if (item.RequiresExplicitIdentity) return false;
        return !item.RequiresSourceIdentity || e.AcquiredFrom.Length > 0 && item.Sources.Any(s => s.Mob.Split(" / ", StringSplitOptions.TrimEntries).Contains(e.AcquiredFrom, StringComparer.OrdinalIgnoreCase));
    }
    public string Locations(ItemDefinition item) => string.Join(", ", Matching(item).Select(e => e.Location).Distinct());
    public string Ownership(ItemDefinition item)
    {
        var rows = Matching(item).ToList();
        string Area(InventoryEntry e) => e.IsCollection ? "Collection (presence only)" : e.Location.StartsWith("SharedBank", StringComparison.OrdinalIgnoreCase) ? "Shared bank" : e.Location.StartsWith("Bank", StringComparison.OrdinalIgnoreCase) ? "Bank" : e.Estimated ? "Log estimate" : "Bags / equipment";
        return rows.Count == 0 ? "No ownership evidence in the latest snapshot" : string.Join("\n", rows.GroupBy(Area).Select(g => g.Key + ": " + (g.All(e => e.IsCollection) ? "quantity unknown" : g.Sum(e => e.Count ?? 0).ToString()) + " · " + string.Join(", ", g.Select(e => e.Location).Distinct()))) + $"\nSnapshot {CapturedAt:MMM dd HH:mm}";
    }
}
public sealed class WishlistGoal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Loadout { get; set; } = "Player";
    public string Zone { get; set; } = "";
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Completed { get; set; }
}
public sealed class LoadoutProfile
{
    public int GearBuildVersion { get; set; }
    public List<GearDraft> GearBuilds { get; set; } = [];
    public string SelectedGearBuildId { get; set; } = "current";
    public List<List<EquipmentPosition>> CurrentGearUndo { get; set; } = [];
    public List<EquipmentPosition> EquipmentPositions { get; set; } = [];
    public List<GearStatProfile> StatProfiles { get; set; } = [];
    public List<GearStatProfile> PetStatProfiles { get; set; } = [];
    public GearDraft? EquipmentDraft { get; set; }
    public string PetSummonSpell { get; set; } = "";
    public int PetSummonRank { get; set; }
    public string PetFocus { get; set; } = "";
    public List<ExaltationAssignment> PlannedExaltations { get; set; } = [];
    public int? PetLevel { get; set; }
    public DateTime? PetEquipmentConfirmedAt { get; set; }
    public List<PetBuild> PetBuilds { get; set; } = [];
    public PetBuild? ConfirmedPetInventory { get; set; }
    public string SelectedPetBuildId { get; set; } = "current";
    public string Name { get; set; } = "Current";
    public List<string> Classes { get; set; } = [];
    public int Level { get; set; }
    public string Race { get; set; } = "";
    public string Deity { get; set; } = "";
    public string PetType { get; set; } = "Unknown";
    public string PetName { get; set; } = "";
    public string Weapons { get; set; } = "";
    public string BuffNotes { get; set; } = "";
    public string Preset { get; set; } = "Tank";
    public string PetPreset { get; set; } = "Pet damage";
    public List<InventoryEntry> Equipment { get; set; } = [];
    public List<InventoryEntry> PetEquipment { get; set; } = [];
}
public sealed class OverlaySettings
{
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 320;
    public double Opacity { get; set; } = .94;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
}
public sealed class CharacterProfile
{
    public WikiRefreshMode WikiRefresh { get; set; } = WikiRefreshMode.Daily;
    public DateTime? LastWikiRefreshAt { get; set; }
    public bool UpdateCurrentBuildFromInventory { get; set; } = true;
    public InventoryBadgeStyle InventoryBadge { get; set; } = InventoryBadgeStyle.Chest;
    public int GearRatingVersion { get; set; }
    public int PetRatingVersion { get; set; }
    public string GearTarget { get; set; } = "Self";
    public string GearPositionId { get; set; } = "CHEST:1";
    public string GearPetPositionId { get; set; } = "PET:1";
    public Dictionary<string, string> ItemIdentityConfirmations { get; set; } = [];
    public string AppIconId { get; set; } = "blue";
    public IconArtworkStyle IconStyle { get; set; } = IconArtworkStyle.Vibrant;
    public DamageEmblem DamageEmblem { get; set; } = DamageEmblem.Fire;
    public Dictionary<string, bool> QuestTracking { get; set; } = [];
    public Dictionary<string, int> QuestQuantities { get; set; } = [];
    public HashSet<string> ExpandedQueueEntries { get; set; } = [];
    public List<string> GearExpansions { get; set; } = ["Classic", "Kunark", "Velious", "Unknown"];
    public List<string> PetExpansions { get; set; } = ["Classic", "Kunark", "Velious", "Unknown"];
    public string Character { get; set; } = "";
    public string Server { get; set; } = "";
    public string GameFolder { get; set; } = "";
    public string Theme { get; set; } = "Auto";
    public string Era { get; set; } = "Classic";
    public string CurrentExpansion { get; set; } = "";
    public string PlanThrough { get; set; } = "Velious";
    public string Zone { get; set; } = "Unknown";
    public List<string> CurrentClasses { get; set; } = [];
    public HashSet<string> Level50Classes { get; set; } = [];
    public HashSet<string> OptionalClasses { get; set; } = [];
    public HashSet<string> ExcludedClasses { get; set; } = [];
    public List<string> GoalPriority { get; set; } = [];
    public HashSet<string> CompletedSteps { get; set; } = [];
    public HashSet<string> CompletedQuests { get; set; } = [];
    public Dictionary<string, string> Routes { get; set; } = [];
    public List<WishlistGoal> Wishlist { get; set; } = [];
    public List<LoadoutProfile> Loadouts { get; set; } = [new()];
    public string SelectedLoadout { get; set; } = "Current";
    public Dictionary<string, Dictionary<string, double>> Weights { get; set; } = [];
    public int EncounterTimeoutSeconds { get; set; } = 12;
    public int EncounterResumeSeconds { get; set; } = 30;
    public int ContinuityVersion { get; set; }
    public bool HideWhenUnfocused { get; set; } = true;
    public bool Sounds { get; set; }
    public bool SmoothMeterMotion { get; set; } = true;
    public string MeterStyle { get; set; } = "Frost";
    public string SecondaryText { get; set; } = "Bright Frost";
    public Dictionary<string, string> SecondaryTextByTheme { get; set; } = [];
    public TrackerTabStyle TrackerTabs { get; set; } = TrackerTabStyle.Dwarf;
    public CombatLabPreferences CombatLab { get; set; } = new();
    public bool ShowLinkedHealing { get; set; } = true;
    public string ObjectiveMode { get; set; } = "My queue";
    public List<QueuedGoal> GoalQueue { get; set; } = [];
    public NativeSyncSettings NativeSync { get; set; } = new();
    public OverlaySettings Objectives { get; set; } = new();
    public OverlaySettings Dps { get; set; } = new() { Left = 430, Top = 120, Width = 240, Height = 160 };
    public int LayoutVersion { get; set; }
    public DpsSelection Meter { get; set; } = new();
    public OverlayMenuSize OverlayMenus { get; set; } = OverlayMenuSize.Compact;
    public DpsSelection? OverlayMeter { get; set; }
    public HealingSelection Healing { get; set; } = new();
    public DateTime? ClassesObservedAt { get; set; }
    public string NativeSetupFolder { get; set; } = "";
    public string DisplaySetupFolder { get; set; } = "";
    public bool BorderlessEnabled { get; set; } = true;
    public string PetElementOverride { get; set; } = "";
    public bool InGroup { get; set; }
    public bool GroupRosterIncomplete { get; set; }
    public List<GroupMembership> GroupMembers { get; set; } = [];
    public Dictionary<string, double> PanelSplits { get; set; } = [];
    public Dictionary<string, double> ColumnWidths { get; set; } = [];
    public Dictionary<string, PanelState> Panels { get; set; } = [];
    public HashSet<string> ExpandedQuestNodes { get; set; } = [];
    public HashSet<string> CollapsedQuestNodes { get; set; } = [];
    public HashSet<string> CollapsedGoalGroups { get; set; } = [];
    public int EvidenceVersion { get; set; }
    public List<PetIdentity> PetIdentities { get; set; } = [];
    public IEnumerable<string> EnabledClasses => CurrentClasses.Concat(Level50Classes).Concat(OptionalClasses).Except(ExcludedClasses).Distinct();
    public LoadoutProfile ActiveLoadout => Loadouts.FirstOrDefault(x => x.Name == SelectedLoadout) ?? Loadouts[0];
    public string EffectivePetElement => PetElementOverride.Length > 0 ? PetElementOverride : PetIdentities.Where(x => x.Until == null && x.Element.Length > 0).OrderByDescending(x => x.From).FirstOrDefault()?.Element ?? "Unknown";
}
public sealed class PanelState
{
    public bool Collapsed { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
public sealed class GroupMembership
{
    public string Owner { get; set; } = "";
    public bool PointOnly { get; set; }
    public string Name { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime? Until { get; set; }
    public bool IsPet { get; set; }
    public string Source { get; set; } = "Log";
    public bool Contains(string name, DateTime time) => Name.Equals(name, StringComparison.OrdinalIgnoreCase) && time >= From && (!Until.HasValue || time < Until);
}
public sealed class PetIdentity
{
    public string Name { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime? Until { get; set; }
    public bool Confirmed { get; set; }
    public string Element { get; set; } = "";
    public string ElementConfidence { get; set; } = "Unknown";
    public string ElementEvidence { get; set; } = "";
    public DateTime? ElementObservedAt { get; set; }
}
public sealed class CombatEvent
{
    public string Id { get; set; } = "";
    public DateTime Time { get; set; }
    public string Actor { get; set; } = "";
    public string Owner { get; set; } = "Unknown";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "Melee";
    public string Ability { get; set; } = "";
    public string DamageType { get; set; } = "";
    public long Damage { get; set; }
    public bool Critical { get; set; }
    public string Outcome { get; set; } = "Hit";
    public string Zone { get; set; } = "";
    public bool ActivityOnly { get; set; }
    public string Relevance { get; set; } = "";
}
public sealed class Encounter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Zone { get; set; } = "";
    public string Loadout { get; set; } = "";
    public string PetType { get; set; } = "";
    public string PetName { get; set; } = "";
    public string PetElementEvidence { get; set; } = "";
    public string Weapons { get; set; } = "";
    public string BuffNotes { get; set; } = "";
    public bool Finished { get; set; }
    public bool IdleResumable { get; set; }
    public int ResumeCount { get; set; }
    public HashSet<string> ResolvedTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string EndReason { get; set; } = "";
    public bool DetailAvailable { get; set; } = true;
    public List<CombatEvent> Events { get; set; } = [];
    public List<HealingEvent> Heals { get; set; } = [];
    public List<HealingSummary> HealingSummaries { get; set; } = [];
    public string HealingCoverage { get; set; } = "Unavailable";
    public bool HealingDetailAvailable { get; set; } = true;
    public long PlayerDamage { get; set; }
    public long PetDamage { get; set; }
    public long UnknownDamage { get; set; }
    public List<PeakHit> Peaks { get; set; } = [];
    public double Duration => Math.Max(1, (End - Start).TotalSeconds);
    public long Damage => PlayerDamage + PetDamage;
    public double Dps => Damage / Duration;
    public string Targets => string.Join(", ", Events.Where(x => !x.ActivityOnly && x.Owner is "Player" or "Pet").Select(x => x.Target).Distinct().Take(3));
    public string Display => $"{Start:MMM dd HH:mm} · {Zone} · {Dps:N1} DPS · {Duration:N0}s" + (Loadout is "" or "Current" ? "" : " · " + Loadout);
}
public sealed record ItemObservation(DateTime Time, string Name, string Action, int Count, string Source, string Result = "");
public sealed class ParsedLine
{
    public AbilityOutcome? AbilityOutcome { get; set; }
    public DateTime Time { get; set; }
    public string? ParseWarning { get; set; }
    public CombatEvent? Combat { get; set; }
    public HealingEvent? Healing { get; set; }
    public ItemObservation? Item { get; set; }
    public string? Zone { get; set; }
    public string? PetName { get; set; }
    public List<string>? Classes { get; set; }
    public int? Level { get; set; }
    public bool RosterObserved { get; set; }
    public bool Boundary { get; set; }
    public string? SlainActor { get; set; }
    public bool PetLost { get; set; }
    public string? PlayerCast { get; set; }
    public bool CastFailed { get; set; }
    public bool GroupRosterReset { get; set; }
    public string? FailedSpell { get; set; }
    public string? PetActivity { get; set; }
    public string? ActivitySpell { get; set; }
    public string? GroupJoined { get; set; }
    public string? GroupLeft { get; set; }
    public bool JoinedGroup { get; set; }
    public bool LeftGroup { get; set; }
    public string? AcceptedInviter { get; set; }
    public string? InventoryExport { get; set; }
    public bool LoggingEnabled { get; set; }
}
