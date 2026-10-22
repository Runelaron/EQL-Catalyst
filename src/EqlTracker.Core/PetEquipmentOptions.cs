namespace EqlTracker.Core;

public static class PetEquipmentProjection
{
    public static PetBuild Prepare(PetBuild source, LoadoutProfile owner, Catalog catalog)
    {
        var plan = EquipmentPositions.Copy(source);
        bool current = source.Id == owner.ConfirmedPetInventory?.Id;
        if (!current) return plan;
        var effective = PetEquipmentInference.Build(plan, owner, catalog);
        foreach (var selection in effective.Selections)
        {
            var item = plan.Items.First(i => i.Id == selection.EntryId);
            item.Slot = effective.Positions.First(p => p.Id == selection.PositionId).Slot;
        }
        plan.LevelEvidence += " Current equipment uses the same intended/inferred assignments as the Pet tab; inference is not in-game verification.";
        return plan;
    }
}

public sealed record PetEquipmentOption(string Id, ItemDefinition Item, int Rank, InventoryEntry? Instance, PetInventoryItem? PetItem, string Location, bool Hypothetical);
public sealed record PetOptimization(PetBuild Plan, IReadOnlyList<string> Requirements, bool SearchComplete, int Evaluations)
{ public List<PetBuild> Alternatives { get; init; } = []; }
public sealed record PetRankUpgrade(string ItemKey, string Name, int CurrentRank, int RequiredRank, string Replaces, string PositionId, double DamagePoints, double DefensePoints, string Evidence)
{ public int RankDelta => RequiredRank - CurrentRank; }

public static class PetEquipmentOptions
{
    public static List<PetEquipmentOption> Pool(PetBuild source, LoadoutProfile owner, Catalog catalog, InventorySnapshot inventory, PetEquipmentMode mode, IEnumerable<string> expansions)
    {
        var allowed = expansions.ToHashSet(); var context = GuidedPetBuilds.Context(owner, source); var result = new List<PetEquipmentOption>();
        foreach (var item in catalog.Items.Where(i => allowed.Contains(ItemPresentation.Expansion(i)) && i.Slots.Count > 0 && GearScorer.Eligibility(i, context, true, null, catalog) == "Eligible"))
        {
            if (mode == PetEquipmentMode.BestPossible)
            {
                int copies = item.Slots.Any(s => EquipmentPositions.Normalize(s) is "Ear" or "Wrist" or "Finger") && !item.Display.Flags.Any(f => f.Contains("LORE")) ? 2 : 1;
                for (int n = 0; n < copies; n++) result.Add(new("catalog:" + item.Key + ":" + n, item, 10, null, null, "Catalog +10 · obtain item / motes", true));
                continue;
            }
            var pet = source.Items.Where(p => p.ItemKey == item.Key).ToList();
            foreach (var p in pet) result.Add(new("pet:" + p.Id, item, p.Rank, p.Entry(), p, p.EquipmentVerified ? "Recorded Pet equipment" : "Manual Pet inventory · physical placement unverified", false));
            var physical = inventory.Evidence(item).Where(e => !e.IsAugment && !e.IsCollection && e.Count > 0).OrderBy(e => e.Location, StringComparer.Ordinal).ToList();
            foreach (var e in physical)
            {
                // Stale player snapshots can still contain a manually transferred Pet copy.
                // Conservatively overlap matching evidence instead of inventing two copies.
                int overlap = pet.Count(p => p.Rank == e.Rank && p.Exaltations.Count == e.Augments.Count);
                for (int n = 0; n < Math.Min(e.Count ?? 0, PetEquipmentPlanner.Capacity(owner.Classes)); n++)
                {
                    if (overlap-- > 0) { pet.Remove(pet.First(p => p.Rank == e.Rank && p.Exaltations.Count == e.Augments.Count)); continue; }
                    if (GearScorer.Eligibility(item, context, true, e.Augments, catalog) == "Eligible")
                        result.Add(new("stock:" + item.Key + ":" + e.Location + ":" + n, item, e.Rank, e, null, e.Location + (InventoryParser.IsEquipped(e) ? " · transfer from player equipment" : " · transfer to pet") + (e.Estimated ? " · log estimate" : ""), false));
                }
            }
        }
        return result;
    }
    private static PetInventoryItem Entry(PetEquipmentOption option, int position, string slot)
    {
        var entry = option.PetItem == null ? new PetInventoryItem { Id = option.Id, ItemKey = option.Item.Key, Name = option.Item.Name, Rank = option.Rank,
            TransferredOutEffects = option.Instance?.TransferredOutEffects.ToList() ?? [],
            Exaltations = option.Instance?.Augments.Select(a => new ExaltationAssignment { SourceName = Identity.Canonical(a.Name), EffectId = a.EffectIdentity, Effect = a.EffectText, State = "Socketed" }).ToList() ?? [] } : EquipmentPositions.Copy(option.PetItem);
        entry.Id = option.Id; entry.Position = position; entry.Slot = slot; entry.EquipmentVerified = false; return entry;
    }
    public static PetOptimization Optimize(PetBuild source, LoadoutProfile owner, Catalog catalog, IReadOnlyList<PetEquipmentOption> pool, CharacterProfile profile, CancellationToken token)
    {
        var variants=new List<PetOptimization>();string goal=PetRoleRatings.Normalize(profile.PetComparison.Goal);
        foreach(string dimension in goal=="Balanced"?new[]{"DPS","Tank"}:new[]{goal})
        {
            var context=new CharacterProfile{PetComparison=new(){Goal=dimension},PetGoalWeights=profile.PetGoalWeights,Weights=profile.Weights};
            variants.Add(OptimizeGreedy(source,owner,catalog,pool,context,token));
            if(pool.Any(p=>p.Item.TwoHanded)&&pool.Any(p=>!p.Item.TwoHanded&&p.Item.Stats.GetValueOrDefault("Damage")>0))
                variants.Add(OptimizeGreedy(source,owner,catalog,pool.Where(p=>!p.Item.TwoHanded).ToList(),context,token));
        }
        if(source.Items.Count>0&&source.Items.All(i=>pool.Any(p=>p.PetItem?.Id==i.Id)))
            variants.Add(new(EquipmentPositions.Copy(source),source.Items.Select(i=>i.Name+" +"+i.Rank+": recorded Pet equipment").ToList(),true,0));
        var attack=PetRoleRatings.Weights(profile,"DPS");var defense=PetRoleRatings.Weights(profile,"Tank");
        var scored=variants.Select(v=>{var stats=PetReassignment.Stats(PetEquipmentInference.Build(v.Plan,owner,catalog,false),catalog);return (Result:v,A:GearScorer.Score(stats,attack),B:GearScorer.Score(stats,defense));}).ToList();
        var frontier=scored.Where(v=>!scored.Any(o=>PetRoleRatings.Dominates(o.A,o.B,v.A,v.B))).ToList();
        var selected=goal=="Balanced"?frontier.OrderBy(v=>EquipmentPositions.Fingerprint(PetEquipmentInference.Build(v.Result.Plan,owner,catalog,false).Positions),StringComparer.Ordinal).First():goal=="Tank"?scored.OrderByDescending(v=>v.B).ThenByDescending(v=>v.A).First():scored.OrderByDescending(v=>v.A).ThenByDescending(v=>v.B).First();
        return selected.Result with { SearchComplete=variants.All(v=>v.SearchComplete),Evaluations=variants.Sum(v=>v.Evaluations),Alternatives=goal=="Balanced"?frontier.Where(v=>v.Result!=selected.Result).DistinctBy(v=>(v.A,v.B)).Take(2).Select(v=>v.Result.Plan).ToList():[] };
    }
    private static PetOptimization OptimizeGreedy(PetBuild source, LoadoutProfile owner, Catalog catalog, IReadOnlyList<PetEquipmentOption> pool, CharacterProfile profile, CancellationToken token)
    {
        var offense = PetRoleRatings.Weights(profile, "DPS"); var defense = PetRoleRatings.Weights(profile, "Tank"); string goal = profile.PetComparison.Goal;
        double Score(PetEquipmentOption p, bool tank) => GearScorer.Score(GearScorer.EffectiveStats(p.Item, p.Rank), tank ? defense : offense);
        // Keep both dimensions and distinct slot/effect restrictions during candidate pruning.
        var candidates = pool.GroupBy(p => string.Join(',', p.Item.Slots.Order()) + "|" + p.Item.TwoHanded + "|" + string.Join(';', p.Instance?.Augments.Select(a => a.EffectIdentity) ?? []))
            .SelectMany(g => g.OrderByDescending(p => Score(p, false)).ThenBy(p => p.Id, StringComparer.Ordinal).Take(8).Concat(g.OrderByDescending(p => Score(p, true)).ThenBy(p => p.Id, StringComparer.Ordinal).Take(8)))
            .DistinctBy(p => p.Id).ToList();
        var plan = EquipmentPositions.Copy(source); plan.Items = []; int visited = 0; bool complete = candidates.Count == pool.Count;
        (double A, double B, int Count) Evaluate(PetBuild p) { var eq = PetEquipmentInference.Build(p, owner, catalog, false); var stats = PetReassignment.Stats(eq, catalog); return (GearScorer.Score(stats, offense), GearScorer.Score(stats, defense), eq.Selections.Count); }
        var current = Evaluate(plan);
        int capacity = PetEquipmentPlanner.Capacity(owner.Classes);
        for (int step = 0; step < capacity; step++)
        {
            PetBuild? best = null; var value = current;
            foreach (var option in candidates.Where(c => plan.Items.All(i => i.Id != c.Id)))
            foreach (var slot in option.Item.Slots.Select(EquipmentPositions.Normalize).Where(EquipmentPositions.IsSlot).Distinct())
            {
                token.ThrowIfCancellationRequested();
                if (++visited > 2400) { complete = false; break; }
                var trial = EquipmentPositions.Copy(plan); trial.Items.Add(Entry(option, trial.Items.Count + 1, slot));
                if (slot == "Secondary" && option.Item.Stats.GetValueOrDefault("Delay") > 0) trial.DualWield = true;
                var score = Evaluate(trial); if (score.Count != trial.Items.Count) continue;
                bool better = goal == "Tank" ? score.B > value.B || score.B == value.B && score.A > value.A : score.A > value.A || score.A == value.A && score.B > value.B;
                if (goal == "Balanced") better = PetRoleRatings.Dominates(score.A, score.B, value.A, value.B);
                if (better) { best = trial; value = score; }
            }
            if (best == null || visited > 2400) break; plan = best; current = value;
        }
        // Greedy finite assignment is explicitly preference guidance, never a global DPS optimum.
        complete &= candidates.Count <= 1;
        if (plan.Items.Any(i => catalog.FindKey(i.ItemKey) is {} d && RankedItemStats.Resolve(d,i.Rank).WeaponUncertain)) plan.TheorycraftAssumptions.Add("Conditional ranked-weapon estimates; this assignment does not establish a confirmed winner.");
        var needs = plan.Items.Select(i => pool.First(p => p.Id == i.Id)).Select(p => p.Item.Name + " +" + p.Rank + ": " + p.Location).ToList();
        plan.IsTheorycraft |= pool.Any(p => p.Hypothetical && plan.Items.Any(i => i.Id == p.Id));
        plan.TheorycraftAssumptions.Add("Best supported preference assignment; global optimum and pet DPS response are unverified.");
        return new(plan, needs, complete, visited);
    }
    public static List<PetRankUpgrade> Upgrades(PetBuild source, LoadoutProfile owner, Catalog catalog, CharacterProfile profile, IReadOnlyList<PetEquipmentOption> pool, string? selectedEntryId, CancellationToken token)
    {
        var equipment = PetEquipmentInference.Build(source, owner, catalog);
        var goal = PetRoleRatings.Normalize(profile.PetComparison.Goal);
        var session = new LoadoutEvaluationSession(catalog, GuidedPetBuilds.Context(owner, source), true, PetRoleRatings.Weights(profile, goal), equipment.Positions, equipment.Positions, petGoal: goal, petDefenseWeights: PetRoleRatings.Weights(profile,"Tank"), petInventory: source,effectivePetEquipment:equipment);
        var destinations = equipment.Positions.Where(p => p.Item != null && (selectedEntryId == null || equipment.Selections.Any(s => s.EntryId == selectedEntryId && s.PositionId == p.Id))).ToList();
        var rows = new List<PetRankUpgrade>();
        foreach (var option in pool.Where(p => !p.Hypothetical))
        foreach (var position in destinations.Where(p => EquipmentPositions.Fits(option.Item,p,true)))
        {
            if (source.Items.Any(i => ("pet:" + i.Id == option.Id || i.Id == option.Id) && equipment.Selections.Any(s => s.EntryId == i.Id))) continue;
            for (int rank = option.Rank; rank <= Math.Max(10, option.Rank); rank++)
            {
                token.ThrowIfCancellationRequested(); var result = session.Present(option.Item, rank, position.Id, option.Instance); var e = result.Evaluation;
                if (e == null || result.Eligibility != "Eligible") break;
                double? a = e.Unbuffed.Delta, b = e.PetDefense?.Delta;
                bool better = goal == "Balanced" ? a.HasValue && b.HasValue && a >= 0 && b >= 0 && (a > 0 || b > 0) : a > 0;
                if (!better) continue;
                rows.Add(new(option.Item.Key,option.Item.Name,option.Rank,rank,result.Replaces,position.Id,a ?? 0,b ?? 0,(e.Status=="Conditional"?"Conditional · ":"") + "Preference improvement; DPS not predicted. " + option.Location)); break;
            }
        }
        return rows.OrderBy(r => r.RankDelta).ThenByDescending(r => goal=="Balanced" ? 0 : r.DamagePoints).ThenBy(r => r.Name,StringComparer.Ordinal).ThenBy(r=>r.PositionId,StringComparer.Ordinal).DistinctBy(r=>r.ItemKey).Take(5).ToList();
    }
}
