namespace EqlTracker.Core;

public sealed record PetEquipmentSelection(string EntryId, string PositionId, bool Verified, string Evidence);
public sealed record PetGearTotals(IReadOnlyDictionary<string, double> Stats, IReadOnlyList<string> Weapons, IReadOnlyList<string> Procs, IReadOnlyList<string> Notes);
public sealed record EffectivePetEquipment(List<EquipmentPosition> Positions, List<PetEquipmentSelection> Selections, PetGearTotals Totals);

/// <summary>Equipment inference never mutates inventory or upgrades manual evidence to verification.</summary>
public static class PetEquipmentInference
{
    public static EffectivePetEquipment Build(PetBuild build, LoadoutProfile owner, Catalog catalog)
    {
        var positions = new List<EquipmentPosition>(); EquipmentPositions.Complete(positions); positions.RemoveAll(p => p.Slot == "Any Slot");
        foreach (var p in positions) p.Knowledge = EquipmentKnowledge.Empty;
        var selections = new List<PetEquipmentSelection>(); var notes = new List<string>(); var used = new HashSet<string>();
        var context = new LoadoutProfile { Classes = owner.Classes, Level = owner.Level, PetType = build.PetType, PetLevel = build.PetLevel, Race = owner.Race, Deity = owner.Deity };
        var active = build.Items.Where(i => i.Position > 0 && i.Position <= PetEquipmentPlanner.Capacity(owner.Classes)).OrderBy(i => i.Position).ToList();
        ItemDefinition? Def(PetInventoryItem i) => catalog.FindKey(i.ItemKey) ?? catalog.Find(i.Name);
        bool Eligible(PetInventoryItem entry)
        {
            var item = Def(entry); if (item == null) { notes.Add(entry.Name + ": metadata missing; refresh this item."); return false; }
            string result = GearScorer.Eligibility(item, context, true, entry.Entry().Augments, catalog);
            if (build.IsTheorycraft && PetItemRules.CanAssume(result)) { notes.Add("Hypothetical assumption · " + entry.Name + ": " + result); result = "Eligible"; }
            if (result != "Eligible") notes.Add(entry.Name + ": " + result);
            if (result != "Eligible" && !result.StartsWith("Confirm pet level")) return false;
            foreach (var effect in entry.Exaltations.Where(e => e.State is "Planned" or "Socketed"))
            {
                string type = ExaltationPlanner.EffectType(effect.Effect);
                if (type != "Unknown" && entry.Rank < ExaltationPlanner.Unlock(type)) { notes.Add(entry.Name + ": effect requires +" + ExaltationPlanner.Unlock(type)); return false; }
                var source = catalog.Find(effect.SourceName);
                if (source != null && !source.Classes.Contains("ALL") && !source.Classes.Intersect(GearScorer.EligibleClasses(context, true)).Any()) { notes.Add(entry.Name + ": Exaltation class restriction"); return false; }
            }
            return true;
        }
        var candidates = active.Where(Eligible).ToList();
        void Assign(PetInventoryItem item, EquipmentPosition position, string evidence)
        {
            var definition = Def(item)!;
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
            var p = positions.FirstOrDefault(p => p.Item == null && p.Slot == EquipmentPositions.Normalize(item.Slot) && EquipmentPositions.Fits(Def(item)!, p, true));
            if (p != null) Assign(item, p, item.EquipmentVerified ? "Verified in game" : "Manual intended-slot correction"); else notes.Add(item.Name + ": intended slot unavailable; correct conflicting entries.");
        }
        foreach (var item in candidates.Where(i => !used.Contains(i.Id) && !EquipmentPositions.IsSlot(i.Slot) && Def(i)!.Slots.Any(s => EquipmentPositions.Normalize(s) is "Primary" or "Secondary")))
        {
            var p = positions.FirstOrDefault(p => p.Item == null && p.Slot is "Primary" or "Secondary" && EquipmentPositions.Fits(Def(item)!, p, true));
            if (p != null) Assign(item, p, "Inferred from weapon inventory order; correct in Edit item… if needed");
        }
        foreach (var item in candidates.Where(i => !used.Contains(i.Id) && !EquipmentPositions.IsSlot(i.Slot)).OrderByDescending(i => GearScorer.EffectiveStats(Def(i)!, i.Rank).GetValueOrDefault("AC")).ThenBy(i => i.Position))
        {
            var possible = positions.Where(p => p.Item == null && p.Slot is not ("Primary" or "Secondary") && EquipmentPositions.Fits(Def(item)!, p, true)).ToList();
            if (possible.Count > 0) Assign(item, possible[0], "Inferred by ranked AC within wearable limits" + (possible.Count > 1 ? "; multiple positions possible, correction available" : ""));
        }
        var stats = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase); var weapons = new List<string>(); var procs = new List<string>();
        foreach (var p in positions.Where(p => p.Item != null))
        {
            var definition = catalog.FindKey(p.Item!.ConfirmedItemKey)!; var values = GearScorer.EffectiveStats(definition, p.Item.Rank);
            foreach (var pair in values.Where(s => s.Key is not ("Ratio" or "Damage" or "Delay" or "Backstab"))) stats[pair.Key] = pair.Key == "Haste" ? Math.Max(stats.GetValueOrDefault(pair.Key), pair.Value) : stats.GetValueOrDefault(pair.Key) + pair.Value;
            if (p.Slot is "Primary" or "Secondary" || build.IsTheorycraft && p.Slot == "Range") { if (values.TryGetValue("Ratio", out double ratio)) weapons.Add($"{p.Slot}: {definition.Name} +{p.Item.Rank} · ratio {ratio:0.###}"); procs.AddRange(ProcEffects(definition, p.Item).Select(e => p.Slot + ": " + e + (definition.EffectActivationLevel is { } level ? $" · activation {level}" : " · activation unverified"))); }
        }
        if (selections.Any(s => !s.Verified)) notes.Add("Equipment inferred; inventory ownership is manual. Use Edit item… to correct intended/verified slots.");
        return new(positions, selections, new(stats, weapons, procs.Distinct().ToList(), notes));
    }
    public static IEnumerable<string> ProcEffects(ItemDefinition item, InventoryEntry entry) => ItemEffects.Active(item, entry.TransferredOutEffects).Concat(entry.Augments.Select(a => a.EffectText)).Where(e => ExaltationPlanner.EffectType(e) == "Proc").Distinct();
    public static GearComparison? Best(ItemDefinition item, int rank, InventoryEntry? instance, LoadoutEvaluationSession session, IReadOnlyList<EquipmentPosition> positions, string? preferred = null)
    {
        var choices = positions.Where(p => EquipmentPositions.Fits(item, p, true)).ToList();
        if (preferred != null && choices.Any(p => p.Id == preferred)) choices = choices.Where(p => p.Id == preferred).ToList();
        return choices.Select(p => session.Present(item, rank, p.Id, instance)).OrderByDescending(c => c.CanCompare).ThenByDescending(c => c.Evaluation?.Unbuffed.Delta ?? double.NegativeInfinity).FirstOrDefault();
    }
}
