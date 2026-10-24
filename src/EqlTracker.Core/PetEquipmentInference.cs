namespace EqlTracker.Core;

public sealed record PetEquipmentSelection(string EntryId, string PositionId, bool Verified, string Evidence);
public sealed record PetGearTotals(IReadOnlyDictionary<string, double> Stats, IReadOnlyList<string> Weapons, IReadOnlyList<string> Procs, IReadOnlyList<string> Notes);
public sealed record EffectivePetEquipment(List<EquipmentPosition> Positions, List<PetEquipmentSelection> Selections, PetGearTotals Totals);

/// <summary>Equipment inference never mutates inventory or upgrades manual evidence to verification.</summary>
public static class PetEquipmentInference
{
    public static List<InventoryEntry> CandidateCopies(IEnumerable<InventoryEntry> stock,PetBuild confirmed,ItemDefinition item,bool inventoryOnly=false)
    {
        var result=stock.Where(e=>!e.IsAugment&&!e.IsCollection&&e.Count>0).ToList();
        if (inventoryOnly) return result;
        var unmatched=result.ToList();
        foreach(var p in confirmed.Items.Where(p=>p.ItemKey==item.Key).OrderBy(p=>p.Position)) {
            var entry=p.Entry();entry.Location="Pet inventory " + p.Position;
            var overlap=unmatched.FirstOrDefault(e=>e.Rank==p.Rank&&e.Augments.Count==entry.Augments.Count&&e.Augments.Select(a=>a.EffectIdentity+"|"+a.EffectText).Order().SequenceEqual(entry.Augments.Select(a=>a.EffectIdentity+"|"+a.EffectText).Order()));
            if(overlap!=null)unmatched.Remove(overlap);else result.Add(entry);
        }
        return result;
    }
    public static EffectivePetEquipment Build(PetBuild build, LoadoutProfile owner, Catalog catalog, bool inferStorage = true)
    {
        build = EquipmentPositions.Copy(build); PetSlotPlanning.Migrate(build, catalog); PetSlotPlanning.Sync(build);
        var positions = new List<EquipmentPosition>(); EquipmentPositions.Complete(positions); positions.RemoveAll(p => p.Slot == "Any Slot");
        foreach (var p in positions) p.Knowledge = EquipmentKnowledge.Empty;
        var selections = new List<PetEquipmentSelection>(); var notes = new List<string>(); var used = new HashSet<string>();
        var context = new LoadoutProfile { Classes = owner.Classes, Level = owner.Level, PetType = build.PetType, PetLevel = build.PetLevel, Race = owner.Race, RaceLevel = owner.RaceLevel, Deity = owner.Deity };
        var active = build.Items.Where(i => i.Position > 0 && i.Position <= PetEquipmentPlanner.Capacity(owner.Classes)).OrderBy(i => i.Position).ToList();
        ItemDefinition? Def(PetInventoryItem i) => catalog.FindKey(i.ItemKey) ?? catalog.Find(i.Name);
        bool Eligible(PetInventoryItem entry)
        {
            var item = Def(entry); if (item == null) { notes.Add(entry.Name + ": metadata missing; refresh this item."); return false; }
            var activeEffects = PetSlotPlanning.ActiveEffects(build, entry, item, entry.Slot, owner, catalog, notes).Select(e => new InventoryEntry { Name = e.SourceName + " (Exaltation)", EffectIdentity = e.EffectId, EffectText = e.Effect }).ToList();
            string result = GearScorer.Eligibility(item, context, true, activeEffects, catalog);
            if (build.IsTheorycraft && PetItemRules.CanAssume(result)) { notes.Add("Hypothetical assumption · " + entry.Name + ": " + result); result = "Eligible"; }
            if (result != "Eligible") notes.Add(entry.Name + ": " + result);
            if (result != "Eligible" && !result.StartsWith("Confirm pet level")) return false;
            return true;
        }
        var candidates = active.Where(Eligible).ToList();
        void Assign(PetInventoryItem item, EquipmentPosition position, string evidence)
        {
            var definition = Def(item)!;
            if (used.Contains(item.Id)) { notes.Add(item.Name + ": duplicate physical-copy reference excluded."); return; }
            if (!PetItemRules.Fits(definition,position,build.IsTheorycraft)) { notes.Add(item.Name + ": not an active pet weapon destination."); return; }
            if (position.Slot == "Secondary" && !build.DualWield && definition.Stats.GetValueOrDefault("Delay") > 0) { notes.Add(item.Name + ": Secondary inactive while Dual wield is off."); return; }
            item = EquipmentPositions.Copy(item); item.Exaltations = PetSlotPlanning.ActiveEffects(build, item, definition, position.Slot, owner, catalog, notes);
            if(definition.Display.Flags.Contains("LORE EQUIPPED") && positions.Any(p=>p.Item?.ConfirmedItemKey==definition.Key)){notes.Add(item.Name+": LORE EQUIPPED duplicate excluded.");return;}
            if (item.Exaltations.Where(e => e.State is "Planned" or "Socketed").Select(e => catalog.Find(e.SourceName)).Any(source => source is { Slots.Count: > 0 } && !source.Slots.Any(s => EquipmentPositions.Normalize(s) == position.Slot))) { notes.Add(item.Name + ": Exaltation restricts " + position.Slot + "; correct its intended slot."); return; }
            if (position.Slot == "Secondary" && positions.Any(p => p.Slot == "Primary" && p.Item != null && (catalog.FindKey(p.Item.ConfirmedItemKey)?.TwoHanded ?? false))) { notes.Add(item.Name + ": Secondary excluded by two-handed Primary."); return; }
            if (position.Slot == "Primary" && definition.TwoHanded && positions.Any(p => p.Slot == "Secondary" && p.Item != null)) { notes.Add(item.Name + ": two-handed correction conflicts with Secondary; review intended slots."); return; }
            var entry = item.Entry(); entry.Location = position.Slot;
            foreach (var effect in item.Exaltations.Where(e => e.State == "Planned")) entry.Augments.Add(new() { Name = effect.SourceName + " (Exaltation)", EffectIdentity = effect.EffectId, EffectText = effect.Effect });
            position.Item = entry; position.Knowledge = EquipmentKnowledge.Occupied; used.Add(item.Id);
            selections.Add(new(item.Id, position.Id, item.EquipmentVerified, evidence));
        }
        // Explicit intended slots are corrections; verified choices take precedence over inferred ones.
        foreach (var item in candidates.Where(i => EquipmentPositions.IsSlot(i.Slot)).OrderByDescending(i => i.EquipmentVerified).ThenBy(i => i.Position))
        {
            var p = positions.FirstOrDefault(p => p.Item == null && p.Slot == EquipmentPositions.Normalize(item.Slot) && PetItemRules.Fits(Def(item)!, p, build.IsTheorycraft));
            if (p != null) Assign(item, p, item.EquipmentVerified ? "User-assigned equipment" : "Legacy intended slot; review placement"); else notes.Add(item.Name + ": intended slot unavailable; correct conflicting entries.");
        }
        foreach (var item in candidates.Where(i => inferStorage && !used.Contains(i.Id) && !EquipmentPositions.IsSlot(i.Slot) && Def(i)!.Slots.Any(s => EquipmentPositions.Normalize(s) is "Primary" or "Secondary")))
        {
            var p = positions.FirstOrDefault(p => p.Item == null && p.Slot is "Primary" or "Secondary" && PetItemRules.Fits(Def(item)!, p, build.IsTheorycraft));
            if (p == null && Def(item)!.Stats.GetValueOrDefault("Delay") > 0) {
                var secondary = positions.FirstOrDefault(p => p.Slot == "Secondary");
                if (secondary?.Item != null && Def(item)!.Slots.Any(s => EquipmentPositions.Normalize(s) == "Secondary") && (catalog.FindKey(secondary.Item.ConfirmedItemKey)?.Stats.GetValueOrDefault("Delay") ?? 0) == 0) {
                    p = secondary; selections.RemoveAll(s => s.PositionId == p.Id); notes.Add("Conditional · offhand weapon replaces shield in this user-observed inventory ordering; verify equipped hands in game.");
                }
            }
            if (p != null) Assign(item, p, "Inferred from ordered inventory; Primary-only entries do not automatically swap hands");
        }
        foreach (var item in candidates.Where(i => inferStorage && !used.Contains(i.Id) && !EquipmentPositions.IsSlot(i.Slot)).OrderByDescending(i => GearScorer.EffectiveStats(Def(i)!, i.Rank).GetValueOrDefault("AC")).ThenBy(i => i.Position))
        {
            var possible = positions.Where(p => p.Item == null && p.Slot is not ("Primary" or "Secondary") && PetItemRules.Fits(Def(item)!, p, build.IsTheorycraft)).ToList();
            if (possible.Count > 0) Assign(item, possible[0], "Inferred by ranked AC within wearable limits" + (possible.Count > 1 ? "; multiple positions possible, correction available" : ""));
        }
        notes.AddRange(PetSlotPlanning.Warnings(build, catalog, PetEquipmentPlanner.Capacity(owner.Classes)));
        var stats = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase); var weapons = new List<string>(); var procs = new List<string>();
        foreach (var p in positions.Where(p => p.Item != null))
        {
            var definition = catalog.FindKey(p.Item!.ConfirmedItemKey)!; var projection = RankedItemStats.Resolve(definition, p.Item.Rank); var values = projection.Stats;
            if (projection.UncertainStats.Count > 0) notes.Add("Rank estimate · " + definition.Name + " +" + p.Item.Rank + ": " + string.Join(", ",projection.UncertainStats) + " unverified; no confirmed weapon winner.");
            foreach (var pair in values.Where(s => s.Key is not ("Ratio" or "Damage" or "Delay" or "Backstab"))) stats[pair.Key] = pair.Key == "Haste" ? Math.Max(stats.GetValueOrDefault(pair.Key), pair.Value) : stats.GetValueOrDefault(pair.Key) + pair.Value;
            if (p.Slot is "Primary" or "Secondary" || build.IsTheorycraft && p.Slot == "Range") { if (values.TryGetValue("Ratio", out double ratio)) weapons.Add($"{p.Slot}: {definition.Name} +{p.Item.Rank} · ratio {ratio:0.###}" + (projection.WeaponUncertain ? " (Conditional estimate)" : "")); procs.AddRange(ProcEffects(definition, p.Item).Select(e => p.Slot + ": " + e + (definition.EffectActivationLevel is { } level ? $" · activation {level}" : " · activation unverified"))); }
        }
        if (selections.Any(s => !s.Verified)) notes.Add("Equipment inferred from manual loadout; review item placement if needed.");
        return new(positions, selections, new(stats, weapons, procs.Distinct().ToList(), notes));
    }
    public static IEnumerable<string> ProcEffects(ItemDefinition item, InventoryEntry entry) => PetEffectPolicy.SelectedTexts(entry).Where(e => ExaltationPlanner.EffectType(e) == "Proc").Distinct();
    public static GearComparison? Best(ItemDefinition item, int rank, InventoryEntry? instance, LoadoutEvaluationSession session, IReadOnlyList<EquipmentPosition> positions, string? preferred = null)
    {
        var choices = positions.Where(p => session.Fits(item,p)).ToList();
        if (preferred != null && choices.Any(p => p.Id == preferred)) choices = choices.Where(p => p.Id == preferred).ToList();
        else if (preferred == null) {
            var sameCopy = choices.Where(p => p.Item != null && instance?.PhysicalId.Length > 0 && p.Item.PhysicalId == instance.PhysicalId).ToList();
            var sameItem = choices.Where(p => p.Item != null && p.Item.ConfirmedItemKey == item.Key && p.Item.Rank == rank).ToList();
            if (sameCopy.Count > 0) choices = sameCopy;
            else if (sameItem.Count > 0) choices = sameItem;
        }
        var comparisons=choices.Select(p=>session.Present(item,rank,p.Id,instance ?? (p.Item?.ConfirmedItemKey==item.Key&&p.Item.Rank==rank?p.Item:null))).ToList();
        if(comparisons.Any(c=>c.Evaluation?.PetDefense!=null))
        {
            var frontier=comparisons.Where(c=>c.Evaluation?.Unbuffed.Delta is {} a && c.Evaluation.PetDefense?.Delta is {} b&&!comparisons.Any(other=>other.Evaluation?.Unbuffed.Delta is {} x&&other.Evaluation.PetDefense?.Delta is {} y&&PetRoleRatings.Dominates(x,y,a,b))).ToList();
            var first=frontier.OrderBy(c=>c.Slot,StringComparer.Ordinal).FirstOrDefault()??comparisons.FirstOrDefault();
            if(first?.Evaluation is {} result && frontier.Count>1)result.WeaponNote+=" Multiple destination tradeoffs; choose a Current item to inspect another slot. No combined Balanced score.";
            return first;
        }
        return comparisons.OrderByDescending(c => c.CanCompare).ThenByDescending(c => c.Evaluation?.Unbuffed.Delta ?? double.NegativeInfinity).FirstOrDefault();
    }
}
