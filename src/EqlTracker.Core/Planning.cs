namespace EqlTracker.Core;

public static class QuestTracking
{
    public static IEnumerable<string> Classes(QuestDefinition quest) => quest.EligibleClasses.Count > 0 ? quest.EligibleClasses : [quest.Class];
    public static bool Relevant(QuestDefinition quest, IEnumerable<string> classes) => Classes(quest).Contains("ALL") || Classes(quest).Intersect(classes).Any();
    public static bool Selected(QuestDefinition quest, CharacterProfile profile, InventorySnapshot? inventory = null, Catalog? catalog = null) => profile.QuestTracking.TryGetValue(quest.Id, out bool selected) ? selected : quest.Kind == "Epic" && Relevant(quest, profile.EnabledClasses) && !(inventory != null && catalog != null && Fulfilled(quest, profile, inventory, catalog));
    public static bool Fulfilled(QuestDefinition quest, CharacterProfile profile, InventorySnapshot inventory, Catalog catalog) => profile.CompletedQuests.Contains(quest.Id) ||
        (quest.RewardAlternatives.Count > 0 ? quest.RewardAlternatives : new List<List<string>> { new() { quest.RewardKey } }).Any(g => g.Count > 0 && g.All(k => catalog.FindKey(k) is { } i && inventory.Count(i) + (k == quest.RewardKey ? CoveredCount(quest, profile, inventory, catalog) : 0) >= Math.Max(1, profile.QuestQuantities.GetValueOrDefault(quest.Id, 1))));
    public static int CoveredCount(QuestDefinition quest, CharacterProfile profile, InventorySnapshot inventory, Catalog catalog)
    {
        if (quest.RecipeVersion == 0) return 0;
        int covered = 0;
        foreach (var later in catalog.Quests.Where(q => q.RecipeVersion > 0 && q.RewardKey != quest.RewardKey))
        {
            int copies = profile.CompletedQuests.Contains(later.Id) ? Math.Max(1, profile.QuestQuantities.GetValueOrDefault(later.Id, 1)) : catalog.FindKey(later.RewardKey) is { } reward ? inventory.Count(reward) : 0;
            if (copies == 0) continue;
            var steps = QuestRecipes.Steps(later, profile);
            int Cost(string key, int count, HashSet<string> seen)
            {
                if (key == quest.RewardKey) return count;
                if (!seen.Add(key)) return 0;
                var step = steps.FirstOrDefault(s => s.Outputs.Any(o => o.ItemKey == key)); if (step == null || step.RandomReward) return 0;
                int yield = Math.Max(1, step.Outputs.First(o => o.ItemKey == key).Count), runs = (count + yield - 1) / yield;
                return step.Inputs.Sum(i => Cost(i.ItemKey, i.Count * (i.Consumed ? runs : 1), new(seen)));
            }
            covered = Math.Max(covered, Cost(later.RewardKey, copies, []));
        }
        return covered;
    }
    public static void Track(CharacterProfile profile, QuestDefinition quest, bool enabled, int quantity = 1)
    { profile.QuestTracking[quest.Id] = enabled; profile.QuestQuantities[quest.Id] = Math.Clamp(quantity, 1, 99); }
}

public sealed class PetBuild
{
    public bool FollowDetectedSummon { get; set; } = true;
    public bool SummonRankKnown { get; set; } = true;
    public int SlotConfigurationVersion { get; set; }
    public bool DualWield { get; set; }
    public List<PetSlotConfiguration> SlotConfigurations { get; set; } = [];
    public PetInventoryItem? DormantSecondary { get; set; }
    public PetSlotConfiguration? DormantSecondaryConfiguration { get; set; }
    public PetComparisonScenario? ComparisonScenario { get; set; }
    public bool IsTheorycraft { get; set; }
    public string TheorycraftRulesVersion { get; set; } = "";
    public List<string> TheorycraftAssumptions { get; set; } = [];
    public List<List<PetInventoryItem>> Undo { get; set; } = [];
    public List<PetBuildEdit> EditHistory { get; set; } = [];
    public string Preset { get; set; } = "";
    public string SummonSpell { get; set; } = "";
    public string SummonClass { get; set; } = "";
    public int SummonRank { get; set; }
    public string Focus { get; set; } = "";
    public List<GearStatProfile> StatProfiles { get; set; } = [];
    public override string ToString() => Name;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Planned build";
    public string PetType { get; set; } = "Unknown";
    public int? PetLevel { get; set; }
    public PetLevelMode LevelMode { get; set; }
    public string LevelEvidence { get; set; } = "";
    public List<PetInventoryItem> Items { get; set; } = [];
}
public sealed class PetInventoryItem
{
    public string Id { get; set; } = "";
    public bool EquipmentVerified { get; set; }
    public override string ToString() => Name + (Rank > 0 ? " +" + Rank : "") + " · " + Slot;
    public int Position { get; set; } = 1;
    public string Slot { get; set; } = "Primary";
    public string ItemKey { get; set; } = "";
    public string Name { get; set; } = "";
    public int Rank { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public List<ExaltationAssignment> Exaltations { get; set; } = [];
    public List<string> TransferredOutEffects { get; set; } = [];
    public InventoryEntry Entry() => new() { PhysicalId = Id.Length > 0 ? "pet:" + Id : "", Location = Slot, Name = Name + (Rank > 0 ? " +" + Rank : ""), Count = 1, ConfirmedItemKey = ItemKey,
        TransferredOutEffects = TransferredOutEffects.ToList(),
        Augments = Exaltations.Where(a => a.State == "Socketed").Select(a => new InventoryEntry { Name = a.SourceName + " (Exaltation)", Location = "Augmentation", EffectText = a.Effect, EffectIdentity = a.EffectId }).ToList() };
}
public sealed class ExaltationAssignment
{
    public string SocketType { get; set; } = "";
    public string DestinationKey { get; set; } = "";
    public string DestinationSlot { get; set; } = "";
    public int DestinationRank { get; set; }
    public string Target { get; set; } = "Pet";
    public string EffectId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Effect { get; set; } = "";
    public string State { get; set; } = "Planned";
}
public sealed record ExaltationOption(string Id, ItemDefinition Source, string Type, string Effect, int UnlockRank, bool Owned, bool SourceOwned)
{
    public ExaltationOwnership OwnershipEvidence { get; init; } = new();
    public EffectPurpose Purpose => ItemEffects.Purpose(Effect, Type);
    public string Content => Source.ContentAvailability == ContentAvailability.Future ? "Future" : Source.ContentAvailability == ContentAvailability.Current ? "Current" : "Unverified";
    public string State => Owned ? OwnershipEvidence.Summary.Length > 0 ? OwnershipEvidence.Summary : "Owned effect · quantity unknown" : SourceOwned ? OwnershipEvidence.SourceReady ? "Source ready to extract" : "Source owned · extraction rank required" : "Obtain source item";
    public string Name => Source.Name + " · " + Effect;
    public bool MatchesId(string id)
    {
        if (Id == id) return true;
        string prefix = Source.Key + ":" + Type.ToLowerInvariant() + ":";
        if (!Id.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string legacy = Effect.Contains("Focus", StringComparison.OrdinalIgnoreCase) ? "focus" : Effect.Contains("Click", StringComparison.OrdinalIgnoreCase) ? "click" : Effect.Contains("Worn", StringComparison.OrdinalIgnoreCase) ? "worn" : Effect.Contains("Proc", StringComparison.OrdinalIgnoreCase) || Effect.Contains("Combat", StringComparison.OrdinalIgnoreCase) ? "proc" : "unknown";
        foreach (string kind in new[] { "unknown", legacy }.Distinct())
        {
            if (id != Source.Key + ":" + kind + ":" + Id[prefix.Length..]) continue;
            var sameName = Source.Effects.Where(e => ItemEffects.NameKey(e) == ItemEffects.NameKey(Effect)).ToList();
            // A real current ID is never an alias of another socket type. An old
            // untyped collision needs explicit resolution rather than two owners.
            if (sameName.Any(e => ExaltationPlanner.EffectType(e) != Type && ExaltationPlanner.EffectId(Source.Key, e) == id)) return false;
            if (kind == "unknown" && sameName.Select(ExaltationPlanner.EffectType).Distinct().Count() > 1) return false;
            return true;
        }
        return false;
    }
}
public sealed class ExaltationOwnership
{
    public bool Stored { get; set; }
    public bool Socketed { get; set; }
    public bool Keyring { get; set; }
    public bool SourceReady { get; set; }
    public string Summary { get; set; } = "";
    public string Details { get; set; } = "";
}
public static class ExaltationPlanner
{
    public static bool Assign(PetInventoryItem item, ExaltationOption effect, ItemDefinition destination, int rank, string slot, LoadoutProfile loadout, Catalog catalog, string state, bool replace, out string error)
    {
        error = Validate(effect, destination, rank, slot, loadout, true, catalog);
        if (error != "Compatible") return false;
        var occupied = item.Exaltations.Where(a => !effect.MatchesId(a.EffectId) && PetEffectPolicy.Kind(a) == effect.Type).ToList();
        if (occupied.Count > 0 && !replace) { error = effect.Type + " effect slot occupied by " + string.Join(", ", occupied.Select(a => a.SourceName)) + ". Enable explicit replacement first."; return false; }
        var proposed = item.Exaltations.Where(a => !effect.MatchesId(a.EffectId) && !(replace && PetEffectPolicy.Kind(a) == effect.Type)).ToList();
        proposed.Add(new() { SocketType = effect.Type, EffectId = effect.Id, SourceName = effect.Source.Name, Effect = effect.Effect, State = state, DestinationKey = destination.Key, DestinationRank = rank, DestinationSlot = slot, Target = "Pet" });
        error = PetEffectPolicy.Validate(proposed); if (error.Length > 0) return false;
        item.Exaltations = proposed;
        error = ""; return true;
    }
    public static string EffectId(string itemKey, string effect)
    {
        string name = System.Text.RegularExpressions.Regex.Replace(effect, @"^(?:focus(?:[_ ]effect)?|proc(?:[_ ]effect)?|worn(?:[_ ]effect)?|click(?:[_ ]effect)?|effect):\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Split('(')[0].Trim();
        return itemKey + ":" + EffectType(effect).ToLowerInvariant() + ":" + Identity.Key(name);
    }
    public static ObjectiveRow? Objective(QueuedGoal goal, ExaltationOption? option)
    {
        if (option == null || option.Owned) return null;
        var source = option.Source;
        return new() { Key = goal.ItemKey + "|" + goal.EffectId, QuestId = "exaltation", Quest = "Exaltation acquisition", Item = goal.Name, Class = "EXALT", Needed = 1,
            Sources = option.SourceOwned ? [] : source.Sources, Zone = option.SourceOwned ? "" : string.Join(", ", source.Sources.Select(s => s.Zone).Distinct()), Mob = option.SourceOwned ? "Upgrade / extract owned source" : string.Join(", ", source.Sources.Select(s => s.Mob).Distinct()),
            Instructions = option.State + $" · {option.Type} extraction at +{option.UnlockRank}. Source ownership alone does not complete this goal.", WikiUrl = source.WikiUrl, Availability = option.SourceOwned ? "Upgrade / extraction" : "Wiki documented" };
    }
    public static int Unlock(string type) => type switch { "Focus" => 1, "Click" => 2, "Worn" => 3, "Proc" => 4, _ => 0 };
    public static string EffectType(string effect) => ItemEffects.Kind(effect);
    public static List<ExaltationOption> Build(Catalog catalog, InventorySnapshot inventory)
    {
        var evidence = inventory.Entries.SelectMany(e => e.Augments.Prepend(e)).Where(e => e.IsAugment).ToList();
        var rows = new List<ExaltationOption>();
        var indexed = catalog.Items.SelectMany(i => i.Effects.Select(e => (Name: EffectName(e), Kind: EffectType(e), Source: i.Key))).ToList();
        var nameSources = indexed.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.Select(e => (e.Source,e.Kind)).Distinct().Count());
        var kindSources = indexed.GroupBy(e => (e.Name,e.Kind)).ToDictionary(g => g.Key, g => g.Select(e => e.Source).Distinct().Count());
        foreach (var item in catalog.Items)
        foreach (string effect in item.Effects.DistinctBy(e => (EffectType(e), ItemEffects.Canonical(e))))
        {
            string type = EffectType(effect); var option = new ExaltationOption(EffectId(item.Key, effect), item, type, effect, Unlock(type), false, inventory.Count(item) > 0);
            string effectName = EffectName(effect);
            bool Matches(InventoryEntry entry)
            {
                if (entry.EffectIdentity.Length > 0) return option.MatchesId(entry.EffectIdentity);
                if (entry.EffectText.Length > 0 && EffectName(entry.EffectText) == effectName)
                {
                    string kind = EffectType(entry.EffectText);
                    return kind == "Unknown" ? nameSources.GetValueOrDefault(effectName) == 1 : kind == type && kindSources.GetValueOrDefault((effectName,type)) == 1;
                }
                return nameSources.GetValueOrDefault(effectName) == 1 && EffectName(entry.Name) == effectName || item.Effects.Distinct().Count() == 1 && Identity.Key(entry.Name) == Identity.Key(item.Name);
            }
            var matches = evidence.Where(Matches).Distinct().ToList();
            var source = inventory.Evidence(item); bool ready = source.Any(e => !e.TransferredOutEffects.Any(t => EffectName(t) == effectName) && e.Rank >= option.UnlockRank && !e.IsCollection);
            bool socketed = matches.Any(e => e.Location.Contains("-Slot", StringComparison.OrdinalIgnoreCase)); bool keyring = matches.Any(e => e.IsCollection); bool stored = matches.Any(e => !e.IsCollection && !e.Location.Contains("-Slot", StringComparison.OrdinalIgnoreCase));
            var states = new List<string>(); if (stored) states.Add("Stored effect"); if (socketed) states.Add("Socketed effect"); if (keyring) states.Add("Effect keyring · quantity unknown");
            rows.Add(option with { Owned = matches.Count > 0, OwnershipEvidence = new() { Stored = stored, Socketed = socketed, Keyring = keyring, SourceReady = ready, Summary = string.Join(" / ", states), Details = string.Join("\n", matches.Select(e => e.Location + " · " + e.Name + (e.IsCollection ? " · quantity unknown" : " ×" + e.Count))) + "\nSource: " + inventory.Ownership(item) + (source.Any(e => e.TransferredOutEffects.Any(t => EffectName(t) == effectName)) ? "\nIntrinsic effect recorded as transferred out on at least one source copy." : "") } });
        }
        rows = rows.DistinctBy(e => e.Id).ToList();
        foreach (var group in evidence.GroupBy(e => Identity.Key(e.Name)))
        {
            if (rows.Any(r => r.Owned && (Identity.Key(r.Source.Name) == group.Key || EffectName(r.Effect) == group.Key))) continue;
            var source = catalog.Find(group.First().Name) ?? new ItemDefinition { Name = Identity.Canonical(group.First().Name), Key = group.Key };
            bool ornament = group.First().Name.Contains("Ornamentation", StringComparison.OrdinalIgnoreCase);
            rows.Add(new(source.Key + ":unclassified", source, ornament ? "Ornamentation" : "Unknown", ornament ? "Appearance only" : "Owned Exaltation · effect identity pending", 0, true, inventory.Count(source) > 0));
        }
        return rows;
    }
    private static string EffectName(string effect) => Identity.Key(System.Text.RegularExpressions.Regex.Replace(ItemEffects.Canonical(effect), @"^(?:focus(?:[_ ]effect)?|proc(?:[_ ]effect)?|worn(?:[_ ]effect)?|click(?:[_ ]effect)?):\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Split('(')[0].Trim());
    public static string Validate(ExaltationOption effect, ItemDefinition destination, int rank, string slot, LoadoutProfile loadout, bool pet, Catalog catalog)
    {
        if (effect.Type == "Unknown") return "Effect type unverified";
        if (rank < effect.UnlockRank) return "Destination requires +" + effect.UnlockRank;
        if (!effect.Source.MetadataComplete || !destination.MetadataComplete) return "Restrictions pending";
        var sharedSlots = destination.Slots.Select(EquipmentPositions.Normalize).Intersect(effect.Source.Slots.Select(EquipmentPositions.Normalize), StringComparer.OrdinalIgnoreCase).ToList();
        string intended = EquipmentPositions.Normalize(slot);
        // Storage describes where an item is kept, never where an effect can be worn.
        if (sharedSlots.Count == 0 || !slot.Equals("Storage", StringComparison.OrdinalIgnoreCase) && !sharedSlots.Contains(intended, StringComparer.OrdinalIgnoreCase)) return "No shared wearable slot";
        if (effect.Source.TwoHanded && !sharedSlots.Contains("Primary", StringComparer.OrdinalIgnoreCase)) return "Two-handed effect requires Primary";
        var allowed = GearScorer.EligibleClasses(loadout, pet);
        bool shared = allowed.Any(c => (effect.Source.Classes.Contains("ALL", StringComparer.OrdinalIgnoreCase) || effect.Source.Classes.Contains(c, StringComparer.OrdinalIgnoreCase)) && (destination.Classes.Contains("ALL", StringComparer.OrdinalIgnoreCase) || destination.Classes.Contains(c, StringComparer.OrdinalIgnoreCase)));
        return shared ? "Compatible" : "No shared class";
    }
}
public static class PetEquipmentPlanner
{
    public static int Capacity(IEnumerable<string> classes) { var c = classes.ToHashSet(); return 4 + (c.Contains("MAG") ? 3 : 0) + (c.Contains("BST") ? 3 : 0) + (c.Contains("NEC") ? 2 : 0) + new[] { "ENC", "DRU", "SHM" }.Count(c.Contains); }
    public static string Validate(PetBuild build, PetInventoryItem item, LoadoutProfile loadout, Catalog catalog)
    {
        if (item.Position < 1 || item.Position > Capacity(loadout.Classes)) return "Inactive · outside current storage capacity";
        if (build.Items.Any(x => x != item && x.Position == item.Position)) return "Duplicate storage position";
        if (item.Slot.Equals("Storage", StringComparison.OrdinalIgnoreCase)) return "Stored · no equipment slot";
        int limit = item.Slot.ToUpperInvariant() is "EAR" or "WRIST" or "FINGER" ? 2 : 1;
        var active = build.Items.Where(x => x.Position <= Capacity(loadout.Classes)).ToList();
        if (active.Count(x => x.Slot.Equals(item.Slot, StringComparison.OrdinalIgnoreCase)) > limit) return "Too many items in slot";
        var definition = catalog.FindKey(item.ItemKey) ?? catalog.Find(item.Name); if (definition == null) return "Metadata pending";
        if (!definition.Slots.Contains(item.Slot, StringComparer.OrdinalIgnoreCase)) return "Incompatible intended slot";
        if (definition.TwoHanded && active.Any(x => x.Slot.Equals("Secondary", StringComparison.OrdinalIgnoreCase))) return "Two-handed weapon replaces Secondary";
        if (item.Slot.Equals("Secondary", StringComparison.OrdinalIgnoreCase) && active.Any(x => catalog.FindKey(x.ItemKey)?.TwoHanded == true)) return "Secondary occupied by two-handed replacement";
        if (definition.Slots.Count == 1 && definition.Slots[0] == "PRIMARY" && active.Any(x => x.Slot.Equals("Secondary", StringComparison.OrdinalIgnoreCase) && x.Position < item.Position)) return "Primary-only weapon must precede Secondary in storage order";
        var copy = new LoadoutProfile { Classes = loadout.Classes.ToList(), Level = loadout.Level, Race = loadout.Race, RaceLevel = loadout.RaceLevel, Deity = loadout.Deity, PetType = build.PetType, PetLevel = build.PetLevel };
        return GearScorer.Eligibility(definition, copy, true, item.Entry().Augments, catalog);
    }
}
public sealed record WishlistRow(WishlistGoal Goal)
{ public string Display => $"{(Goal.Completed ? "✓" : "○")} {Goal.Name} ×{Goal.Quantity} · {Goal.Loadout} · {Goal.Zone}"; }
