namespace EqlTracker.Core;

public sealed class PetBenchmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string Pet { get; set; } = "";
    public int? Level { get; set; }
    public string Summon { get; set; } = "";
    public int Rank { get; set; }
    public string MechanicsVersion { get; set; } = PetDefinition.Version;
    public string ContextKey { get; set; } = "";
    public DateTime RecordedAt { get; set; } = DateTime.Now;
    public List<string> EncounterIds { get; set; } = [];
    public List<string> Targets { get; set; } = [];
    public string PetIdentityEvidence { get; set; } = "";
    public string Equipment { get; set; } = "";
    public string Buffs { get; set; } = "";
    public string Conditions { get; set; } = "";
    public double? Dps { get; set; }
    public double? Hp { get; set; }
    public double Seconds { get; set; }
    public long? Damage { get; set; }
    public bool ContextConfirmed { get; set; }
    public void Validate()
    {
        if (Label.Trim().Length == 0) throw new ArgumentException("Give the benchmark a name.");
        if (!Dps.HasValue && !Hp.HasValue) throw new ArgumentException("Record measured DPS, HP, or both.");
        if (Dps is {} d && (!double.IsFinite(d) || d < 0) || Hp is {} h && (!double.IsFinite(h) || h <= 0)) throw new ArgumentException("DPS must be finite and nonnegative; HP must be positive.");
        if (Damage.HasValue && (Seconds <= 0 || !double.IsFinite(Seconds) || Math.Abs(Dps.GetValueOrDefault() - Damage.Value / Seconds) > .0000001)) throw new ArgumentException("Measured DPS must reconcile to damage / duration.");
    }
}

public static class PetBenchmarks
{
    public static string Context(PetComparisonResult row) => PetComparisonEngine.Key(new { PetDefinition.Version, row.Pet, row.Plan.PetLevel, row.Plan.SummonSpell, row.Plan.SummonRank, row.Plan.Focus,
        Equipment = EquipmentPositions.Fingerprint(row.Equipment.Positions), row.Scenario.Buffs, row.Scenario.ExternalBuffs, row.Scenario.Encounter, row.Scenario.CastingPolicy, row.Scenario.OwnerMechanicsContext });
    public static PetBenchmark? Matching(PetComparisonResult row, IEnumerable<PetBenchmark> benchmarks) => benchmarks.Where(b => b.ContextConfirmed && b.MechanicsVersion == PetDefinition.Version && b.ContextKey == Context(row)).OrderByDescending(b => b.RecordedAt).FirstOrDefault();
    public static PetBenchmark FromFights(PetComparisonResult row, IEnumerable<Encounter> encounters, string name, bool confirmed)
    {
        var fights = encounters.Where(f => f.Finished && f.PetType == row.Pet && f.PetDamage > 0).DistinctBy(f => f.Id).ToList();
        if (fights.Count == 0) throw new ArgumentException("Select finished encounters with documented pet identity.");
        double seconds = fights.Sum(f => f.Duration); long damage = fights.Sum(f => f.PetDamage);
        return new() { Label = name, Pet = row.Pet, Level = row.Plan.PetLevel, Summon = row.Plan.SummonSpell, Rank = row.Plan.SummonRank, ContextKey = Context(row),
            EncounterIds = fights.Select(f => f.Id).ToList(), Targets = fights.SelectMany(f => f.Events.Where(e => e.Owner == "Pet").Select(e => e.Target)).Distinct().ToList(),
            PetIdentityEvidence = string.Join("; ", fights.Select(f => f.PetName + ": " + (f.PetElementEvidence.Length > 0 ? f.PetElementEvidence : "Type recorded; summon correlation unverified")).Distinct()),
            Equipment = EquipmentPositions.Fingerprint(row.Equipment.Positions), Buffs = PetComparisonEngine.Key(row.Scenario.Buffs), Dps = damage / seconds, Seconds = seconds, Damage = damage,
            Conditions = "Observed encounter DPS, using summed encounter durations. Target differences, casting, buffs and equipment must be reviewed; no causal weapon multiplier inferred.", ContextConfirmed = confirmed };
    }
}
