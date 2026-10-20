namespace EqlTracker.Core;

public sealed record BuildContribution(string Stat, double ItemChange, double Current, double Candidate, double UsableChange, double Weight, bool Uncertain, string Note)
{
    public double Points => UsableChange * Weight;
    public double Capped => ItemChange - UsableChange;
    public string Category => Stat is "AC" or "HP" or "STA" or "AGI" || Stat.StartsWith("Resist") ? "Defense" : Stat is "Mana" or "INT" or "WIS" or "ManaRegen" or "HPRegen" ? "Sustain" : "Offense/procs";
    public string Explanation => $"{ItemChange:+0.##;-0.##;0} {Stat} item change → {UsableChange:+0.##;-0.##;0} usable → {Points:+0.##;-0.##;0} points" + (Weight == 0 ? " · Not scored by this role" : "") + (Note.Length > 0 ? " · " + Note : "");
}
public sealed class ContextRating
{
    public string Name { get; init; } = "";
    public double? CurrentScore { get; init; }
    public double? CandidateScore { get; init; }
    public double? MarginalEstimate { get; init; }
    public double? Delta => CandidateScore - CurrentScore ?? MarginalEstimate;
    public List<BuildContribution> Changes { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public string Quality { get; init; } = "";
}
public sealed class LoadoutComparison
{
    public bool IsHypothetical { get; set; }
    public List<string> Assumptions { get; set; } = [];
    public bool Pet { get; init; }
    public string Status { get; set; } = "Rated";
    public string WeaponNote { get; set; } = "";
    public string ActivationNote { get; set; } = "";
    public ContextRating Unbuffed { get; init; } = new() { Name = "Unbuffed" };
    public ContextRating Buffed { get; init; } = new() { Name = "Usual buffs" };
    public string PositionId { get; init; } = "";
    public List<string> Replaced { get; init; } = [];
    public List<string> EffectsGained { get; init; } = [];
    public List<string> EffectsLost { get; init; } = [];
    public string Eligibility { get; init; } = "Eligible";
    public int? BreakEvenRank { get; set; }
    public string Group => Status == "Conditional" ? "Conditional" : Pet ? Unbuffed.Delta is > 0 ? "Positive" : Unbuffed.Delta == null ? "Data needed" : "Not higher rated" : Unbuffed.Delta is > 0 && Buffed.Delta is > 0 ? "Positive in both" : Unbuffed.Delta is > 0 || Buffed.Delta is > 0 ? "Context-dependent / estimate" : Unbuffed.Delta == null && Buffed.Delta == null ? "Data needed" : "Not higher rated";
}

/// <summary>One immutable, cached baseline for a batch of candidates. No ingestion locks or game access.</summary>
public sealed class LoadoutEvaluationSession
{
    private readonly Catalog catalog;
    private readonly LoadoutProfile loadout;
    private readonly bool pet;
    private readonly bool theorycraft;
    private readonly Dictionary<string, double> weights;
    private readonly List<EquipmentPosition> before, reference;
    private readonly Dictionary<string, double> currentStats, referenceStats;
    private readonly Dictionary<string, (Dictionary<string, double> Stats, List<string> Effects, bool Complete)> cache = [];
    private readonly List<string> currentEffects;
    private readonly bool baselineComplete;
    private readonly string referenceFingerprint;
    private readonly HashSet<string> missingMetadata = [];
    public LoadoutEvaluationSession(Catalog catalog, LoadoutProfile loadout, bool pet, Dictionary<string, double> weights,
        List<EquipmentPosition>? positions = null, List<EquipmentPosition>? reference = null, bool theorycraft = false)
    {
        this.catalog = catalog; this.loadout = loadout; this.pet = pet; this.weights = weights;
        this.theorycraft = theorycraft;
        before = positions ?? EquipmentPositions.For(loadout, pet); this.reference = reference ?? EquipmentPositions.For(loadout, pet);
        referenceFingerprint = EquipmentPositions.Fingerprint(this.reference);
        (currentStats, currentEffects, baselineComplete) = Aggregate(before); referenceStats = Aggregate(this.reference).Stats;
    }
    private ItemDefinition? Definition(InventoryEntry entry) => catalog.FindKey(entry.ConfirmedItemKey) ?? catalog.Find(entry.Name, entry.GameId == 0 ? null : entry.GameId);
    private (Dictionary<string, double> Stats, List<string> Effects, bool Complete) Item(InventoryEntry entry, string slot)
    {
        string key = slot + "|" + entry.Rank + "|" + entry.Name + "|" + entry.GameId + "|" + entry.ConfirmedItemKey + "|" + string.Join(';', entry.TransferredOutEffects) + "|" + string.Join(';', entry.Augments.Select(a => a.Name + ":" + a.EffectIdentity + ":" + a.EffectText)); if (cache.TryGetValue(key, out var found)) return found;
        var item = Definition(entry); var stats = item == null ? new Dictionary<string, double>() : GearScorer.EffectiveStats(item, entry.Rank);
        if (pet && !theorycraft && item != null && PetItemRules.Unsupported(item).Length > 0) return cache[key] = (new(), [], true);
        if (item?.MetadataComplete != true) missingMetadata.Add(entry.Name);
        if (slot is not ("Primary" or "Secondary" or "Range")) foreach (string combat in new[] { "Ratio", "Damage", "Delay", "Backstab" }) stats.Remove(combat);
        var effects = (item?.Effects ?? []).Except(entry.TransferredOutEffects).ToList(); bool complete = item?.MetadataComplete == true;
        foreach (var augment in entry.Augments)
        {
            var source = Definition(augment);
            if (augment.EffectText.Length > 0) effects.Add(augment.EffectText);
            else if (source?.Effects.Count == 1) effects.Add(source.Effects[0]);
            else { effects.Add("Unclassified socketed effect: " + augment.Name); missingMetadata.Add("socketed effect " + augment.Name + " (review its source/effect in Edit item…)"); complete = false; }
        }
        return cache[key] = (stats, effects, complete);
    }
    private (Dictionary<string, double> Stats, List<string> Effects, bool Complete) Aggregate(IEnumerable<EquipmentPosition> positions)
    {
        var stats = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase); var effects = new List<string>(); bool complete = true;
        foreach (var position in positions.Where(p => p.Knowledge == EquipmentKnowledge.Occupied && p.Item != null))
        {
            var item = Item(position.Item!, position.Slot); complete &= item.Complete; effects.AddRange(item.Effects);
            foreach (var stat in item.Stats) stats[stat.Key] = stat.Key == "Haste" ? Math.Max(stats.GetValueOrDefault(stat.Key), stat.Value) : stats.GetValueOrDefault(stat.Key) + stat.Value;
        }
        return (stats, effects.Distinct().ToList(), complete);
    }
    public static string PetSignature(LoadoutProfile p) => p.PetType + "|" + p.PetLevel + "|" + p.PetSummonSpell + "|" + p.PetSummonRank + "|" + p.PetFocus;
    private ContextRating Rate(string name, Dictionary<string, double> after, bool complete, bool includeAAs, bool fullTotals)
    {
        var profiles = pet ? loadout.PetStatProfiles : loadout.StatProfiles; var profile = profiles.FirstOrDefault(p => p.Name == name);
        var notes = new List<string>(); bool observed = profile?.ConfirmedAt != null;
        if (observed && (profile!.EquipmentFingerprint != referenceFingerprint || profile.Level != (pet ? loadout.PetLevel ?? 0 : loadout.Level) || !profile.Classes.Order().SequenceEqual(loadout.Classes.Order()) || pet && profile.PetSignature != PetSignature(loadout)))
        { observed = false; notes.Add("Stat profile is stale: equipment, level, classes or pet context changed. Confirm totals again."); }
        if (!complete) { if (missingMetadata.Count > 0) notes.Add("Metadata pending: " + string.Join(", ",missingMetadata)); return new() { Name = name, Quality = "Metadata or selected position incomplete", Notes = notes }; }
        if (!fullTotals)
        {
            notes.Add("Unchanged equipment metadata pending: " + string.Join(", ",missingMetadata) + ". Only the known replacement's marginal preference change is shown; whole-build totals remain unknown.");
            if (weights.GetValueOrDefault("Haste") != 0 && after.GetValueOrDefault("Haste") != currentStats.GetValueOrDefault("Haste") && !(observed && profile!.Stats.ContainsKey("Haste"))) return new() {Name=name,Quality="Missing metadata can change haste stacking; comparison unavailable",Notes=notes};
        }
        if (!observed && name == "Usual buffs") return new() { Name = name, Quality = profile == null ? "Confirm Usual buffs profile" : "Confirm stale Usual buffs profile", Notes = notes };
        if (observed && name == "Usual buffs" && !profile!.BuffsIncludedInTotals) return new() { Name = name, Quality = "Buff bonuses are not included: confirm observed buffed totals", Notes = notes };
        if (!includeAAs && (!observed || !profile!.AAListComplete)) return new() { Name=name,Quality="AA-off unavailable: confirm a complete AA profile first",Notes=notes };
        if (!observed) notes.Add("Gear-only estimate; total stats, caps and AAs are not confirmed.");
        if (weights.GetValueOrDefault("Haste") != 0) notes.Add("Worn haste uses the strongest listed item as a preference estimate; spell/overhaste stacking remains unverified.");
        var activeAAs = observed ? profile!.AAs.Where(a => a.Target == (pet ? "Pet" : "Self")).ToList() : [];
        if (activeAAs.Where(a=>a.Rank>0 && a.Confirmed && a.Kind==AaModifierKind.SoftCapPercent).GroupBy(a=>a.Stat,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1) && (!includeAAs || profile!.Limits.Any(l=>!l.IncludesAAs)))
            return new() { Name=name,Quality="AA percentage stacking is unverified; confirm a combined cap/baseline instead" };
        if (!includeAAs && activeAAs.Any(a => a.Rank == null || a.Rank > 0 && (!a.Confirmed || a.Kind == AaModifierKind.Information)))
            return new() { Name = name, Quality = "AA-off unavailable: an AA effect is unknown or not reversibly modeled" };
        foreach (var aa in activeAAs.Where(a => a.Rank == null || a.Rank > 0 && a.Kind == AaModifierKind.Information)) notes.Add($"AA {aa.Name}: " + (aa.Rank == null ? "rank unknown" : $"rank {aa.Rank} · influence documented; exact response not modeled") + " · " + aa.Source);
        var changes = new List<BuildContribution>(); double oldScore = 0, newScore = 0;
        foreach (string stat in currentStats.Keys.Concat(after.Keys).Concat(weights.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            double rawBefore = currentStats.GetValueOrDefault(stat), rawAfter = after.GetValueOrDefault(stat), delta = rawAfter - rawBefore;
            double old = rawBefore, next = rawAfter; bool uncertain = false; string note = "";
            var limit = observed ? profile!.Limits.FirstOrDefault(l => l.Stat.Equals(stat, StringComparison.OrdinalIgnoreCase) && l.Confirmed && l.Kind != StatLimitKind.ProcLimit) : null;
            bool wornAc = limit?.Kind == StatLimitKind.WornAcSoftCap;
            if (observed && !wornAc && profile!.Stats.TryGetValue(stat, out var observation))
            {
                double anchor = observation.Value + (observation.Overflow ?? 0);
                old = anchor + rawBefore - referenceStats.GetValueOrDefault(stat); next = anchor + rawAfter - referenceStats.GetValueOrDefault(stat);
                if (limit != null && observation.Value >= limit.Value && !observation.UncappedTotalKnown && observation.Overflow == null && (rawBefore < referenceStats.GetValueOrDefault(stat) || rawAfter < referenceStats.GetValueOrDefault(stat)))
                { uncertain = true; note = "Cap-crossing loss: hidden overflow unknown; conservative estimate"; }
            }
            else if (observed && !wornAc && limit != null) { limit = null; uncertain = true; note = "Total stat missing; confirmed cap cannot be applied to gear alone"; }
            double bonus = activeAAs.Where(a => a.Rank > 0 && a.Confirmed && a.Kind == AaModifierKind.FlatStat && a.Stat.Equals(stat, StringComparison.OrdinalIgnoreCase)).Sum(a => a.Value);
            if (observed && !wornAc) { double adjustment = includeAAs ? profile!.AAsIncludedInTotals ? 0 : bonus : profile!.AAsIncludedInTotals ? -bonus : 0; old += adjustment; next += adjustment; }
            if (limit != null)
            {
                double cap = limit.Value;
                var modifiers = activeAAs.Where(a => a.Rank > 0 && a.Confirmed && a.Stat.Equals(stat, StringComparison.OrdinalIgnoreCase));
                foreach (var aa in modifiers)
                {
                    double direction = includeAAs ? limit.IncludesAAs ? 0 : 1 : limit.IncludesAAs ? -1 : 0;
                    if (aa.Kind == AaModifierKind.HardCapIncrease) cap += direction * aa.Value;
                    if (aa.Kind == AaModifierKind.SoftCapPercent && wornAc) cap = direction < 0 ? cap / (1 + aa.Value / 100) : cap * (1 + direction * aa.Value / 100);
                }
                double Apply(double value) => Math.Min(value, cap) + Math.Max(0, value - cap) * (wornAc ? limit.AboveCapWeight : 0);
                old = Apply(old); next = Apply(next); if (Math.Abs((next - old) - delta) > .001) note += (note.Length > 0 ? "; " : "") + $"{limit.Kind} {cap:0.##} · {limit.Source}";
            }
            if (stat == "DEX") note += (note.Length > 0 ? "; " : "") + "Proc-related preference; no verified proc-rate prediction";
            if (stat == "AGI") note += (note.Length > 0 ? "; " : "") + "Avoidance-related preference, not Dodge percentage";
            double weight = weights.GetValueOrDefault(stat); oldScore += old * weight; newScore += next * weight;
            if (delta != 0) changes.Add(new(stat, delta, old, next, next - old, weight, uncertain, note));
        }
        if (observed) { notes.Add($"{profile!.Source} · {profile.ConfirmedAt:g} · " + (profile.AAsIncludedInTotals ? "AA bonuses already included" : "Only confirmed flat AA bonuses added")); if (profile.Buffs.Length > 0) notes.Add("Buffs: " + profile.Buffs); foreach (var limit in profile.Limits.Where(l => l.Kind == StatLimitKind.ProcLimit)) notes.Add($"Proc limit {limit.Stat} {limit.Value}: separate from attribute caps; not used for predicted procs · {limit.Source}"); }
        return new() { Name = name, CurrentScore = fullTotals ? oldScore : null, CandidateScore = fullTotals ? newScore : null, MarginalEstimate = fullTotals ? null : newScore-oldScore, Changes = changes, Notes = notes, Quality = !fullTotals ? "Partial marginal estimate · whole-build totals incomplete" : observed ? "Confirmed context · preference estimate; effects and unverified mechanics separate" : "Gear-only estimate" };
    }
    public LoadoutComparison CompareBuild(List<EquipmentPosition> after, string id = "", string eligibility = "Eligible", bool includeAAs = true)
    {
        var aggregate = Aggregate(after);
        static string Signature(InventoryEntry? item) => item==null?"":item.Rank+"|"+item.Name+"|"+item.GameId+"|"+item.ConfirmedItemKey+"|"+string.Join(';',item.TransferredOutEffects)+"|"+string.Join(';',item.Augments.Select(a=>a.Name+":"+a.EffectIdentity+":"+a.EffectText));
        static bool Different(EquipmentPosition p,EquipmentPosition? other) => other==null || other.Slot!=p.Slot || !ReferenceEquals(p.Item,other.Item) && Signature(p.Item)!=Signature(other.Item);
        bool knownChanges = before.Where(p=>Different(p,after.FirstOrDefault(a=>a.Id==p.Id))).Where(p=>p.Item!=null).All(p=>Item(p.Item!,p.Slot).Complete) && after.Where(p=>Different(p,before.FirstOrDefault(b=>b.Id==p.Id))).Where(p=>p.Item!=null).All(p=>Item(p.Item!,p.Slot).Complete);
        bool complete = knownChanges && eligibility == "Eligible";
        if (id.Length == 0 && before.Any(p => p.Knowledge == EquipmentKnowledge.Unknown) && !before.Any(p => p.Knowledge == EquipmentKnowledge.Occupied)) complete = false;
        var selected = before.FirstOrDefault(s => s.Id == id); if (selected?.Knowledge == EquipmentKnowledge.Unknown) complete = false;
        var result = new LoadoutComparison() { Pet = pet, PositionId = id, Eligibility = eligibility, Replaced = before.Where(p => after.FirstOrDefault(a => a.Id == p.Id)?.Item?.Name != p.Item?.Name && p.Item != null).Select(p => p.Label + ": " + p.Item!.Name).ToList(),
            Unbuffed = Rate("Unbuffed", aggregate.Stats, complete, includeAAs, baselineComplete && aggregate.Complete), Buffed = pet ? new() { Name = "Legacy buff context" } : Rate("Usual buffs", aggregate.Stats, complete, includeAAs, baselineComplete && aggregate.Complete), EffectsGained = aggregate.Effects.Except(currentEffects).ToList(), EffectsLost = currentEffects.Except(aggregate.Effects).ToList() };
        if (pet)
        {
            bool Weapon(EquipmentPosition p) => p.Slot is "Primary" or "Secondary" && p.Item != null && Definition(p.Item) is { } d && GearScorer.EffectiveStats(d, p.Item.Rank).GetValueOrDefault("Ratio") > 0;
            bool twoBefore = before.Any(p => p.Slot == "Primary" && p.Item != null && Definition(p.Item)?.TwoHanded == true);
            bool twoAfter = after.Any(p => p.Slot == "Primary" && p.Item != null && Definition(p.Item)?.TwoHanded == true);
            bool handChanged = twoBefore != twoAfter && (before.Count(Weapon) > 1 || after.Count(Weapon) > 1);
            string Ratios(IEnumerable<EquipmentPosition> values) => string.Join(" · ", values.Where(Weapon).Select(p => p.Slot + " " + GearScorer.EffectiveStats(Definition(p.Item!)!, p.Item!.Rank).GetValueOrDefault("Ratio").ToString("0.###")));
            if (before.Any(p => p.Slot is "Primary" or "Secondary" && Different(p, after.FirstOrDefault(a => a.Id == p.Id)))) result.WeaponNote = "Current ratios: " + Ratios(before) + " → planned: " + Ratios(after) + ". Ratio preference is not a pet DPS prediction.";
            if (handChanged)
            {
                result.Status = "Conditional";
                var old = result.Unbuffed;
                result = new() { Pet = true, Status = "Conditional", PositionId = result.PositionId, Eligibility = result.Eligibility, Replaced = result.Replaced, EffectsGained = result.EffectsGained, EffectsLost = result.EffectsLost, ActivationNote = result.ActivationNote, WeaponNote = result.WeaponNote,
                    Unbuffed = new() { Name = "Upgrade rating", Changes = old.Changes, Notes = old.Notes, Quality = "Conditional: off-hand contribution unknown; compare ratios, stats and procs. No overall DPS winner is inferred." } };
            }
        }
        result.IsHypothetical = theorycraft;
        if (theorycraft)
        {
            result.Assumptions = after.Where(p => p.Item != null).Select(p => Definition(p.Item!) is { } d ? d.Name + ": " + GearScorer.Eligibility(d, loadout, pet, p.Item!.Augments, catalog) : "").Where(s => !s.EndsWith(": Eligible") && s.Length > 0).Distinct().ToList();
            if (result.Status == "Rated") result.Status = "Hypothetical";
        }
        return result;
    }
    public LoadoutComparison Compare(ItemDefinition candidate, int rank, string id, InventoryEntry? instance = null, bool includeAAs = true)
    {
        var position = before.FirstOrDefault(s => s.Id == id);
        if (position == null) return new() { Eligibility = "Select a replacement position" };
        var entry = instance == null ? new InventoryEntry() : EquipmentPositions.Copy(instance); entry.Name = candidate.Name + (rank > 0 ? " +" + rank : ""); entry.GameId = candidate.GameId ?? 0; entry.ConfirmedItemKey = candidate.Key; entry.Count = 1; entry.Location = position.Slot;
        string eligible = GearScorer.Eligibility(candidate, loadout, pet, entry.Augments, catalog);
        if (theorycraft && (PetItemRules.CanAssume(eligible) || !pet && eligible is "Verify race restriction" or "Verify deity restriction")) eligible = "Eligible";
        string levelNote = "";
        if (pet && eligible.StartsWith("Confirm pet level")) { levelNote = eligible + ". Gear-only rating; eligibility remains unconfirmed. "; eligible = "Eligible"; }
        foreach (var augment in entry.Augments)
        {
            var source = Definition(augment); string effect = augment.EffectText.Length>0 ? augment.EffectText : source?.Effects.Count==1 ? source.Effects[0] : "";
            string type = ExaltationPlanner.EffectType(effect);
            if (type == "Unknown") eligible = "Exaltation effect type unverified";
            else if (rank < ExaltationPlanner.Unlock(type)) eligible = "Exaltation requires destination +" + ExaltationPlanner.Unlock(type);
            if (position.Slot != "Any Slot" && source != null && !source.Slots.Any(s=>EquipmentPositions.Normalize(s)==position.Slot)) eligible = "Slot restricted by Exaltation";
        }
        if (!EquipmentPositions.Fits(candidate, position, pet)) eligible = "Incompatible selected position";
        var result = CompareBuild(EquipmentPositions.Replace(before, id, entry, catalog), id, eligible, includeAAs);
        if (candidate.EffectActivationLevel is { } activation) result.ActivationNote = $"Item effect activation level {activation}; " + ((pet?loadout.PetLevel:loadout.Level) is { } actual ? actual<activation ? $"inactive at current level {actual}." : $"current level {actual} meets it." : "actual pet level needs confirmation.");
        result.ActivationNote = levelNote + result.ActivationNote;
        if (levelNote.Length > 0 && result.Status == "Rated") result.Status = "Eligibility unconfirmed";
        return result;
    }
    public int? BreakEven(ItemDefinition candidate, string id, InventoryEntry? entry = null, bool includeAAs = true)
    {
        for (int rank = 0; rank <= 20; rank++) { var value = Compare(candidate, rank, id, entry, includeAAs); if (value.Unbuffed.Delta is > 0 && (pet || value.Buffed.Delta is > 0)) return rank; } return null;
    }
    public GearComparison Present(ItemDefinition item, int rank, string id, InventoryEntry? instance = null, bool includeAAs = true)
    {
        var value = Compare(item, rank, id, instance, includeAAs); var position = before.FirstOrDefault(s => s.Id == id);
        var contributions = value.Unbuffed.Changes.Select(c => new ScoreContribution(c.Stat, c.Current, c.Candidate, c.Weight)).ToList();
        return new(item.Name + (rank > 0 ? " +" + rank : ""), position?.Label ?? "Select position", value.Replaced.Count > 0 ? string.Join(" + ", value.Replaced) : position?.Name ?? "Unknown", value.Unbuffed.CandidateScore ?? 0, value.Unbuffed.CurrentScore ?? 0, value.Unbuffed.Delta ?? 0,
            string.Join('\n', value.Unbuffed.Changes.Select(c => c.Explanation)), "Effects gained: " + string.Join(", ", value.EffectsGained) + "\nEffects lost: " + string.Join(", ", value.EffectsLost), value.Eligibility,
            value.Unbuffed.Delta == null ? "Unknown: " + value.Unbuffed.Quality : value.Unbuffed.Quality) { Evaluation = value, Breakdown = contributions, ItemScore = item.MetadataComplete ? GearScorer.Score(GearScorer.EffectiveStats(item, rank), weights) : null };
    }
}
