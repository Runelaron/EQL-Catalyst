namespace EqlTracker.Core;

public sealed record CasterSpellEvidence(string Caster, string Spell);
public sealed class CoverageObservation
{
    public string Id { get; set; } = "";
    public EffectIdentity Song { get; set; } = new("", "", null);
    public string Caster { get; set; } = "";
    public string Recipient { get; set; } = "";
    public DateTime At { get; set; }
    public DateTime? Until { get; set; }
    public DateTime? InvalidatedAt { get; set; }
    public bool SuccessfulApplication { get; set; }
    public EvidenceQuality Attribution { get; set; }
    public double? Instrument { get; set; }
    public double? Singing { get; set; }
    public double? Focus { get; set; }
    public int? EffectiveCasterLevel { get; set; }
    public string AaContext { get; set; } = "";
    public string Source { get; set; } = "";
    public List<EffectInput> Effects { get; set; } = [];
    public bool Fresh(DateTime now, string recipient) => SuccessfulApplication && Attribution >= EvidenceQuality.UserConfirmed && Recipient == recipient && At <= now && Until.HasValue && Until > now;
}
public sealed class BardSong
{
    public string Label=>Identity.Name+" · rank "+(Identity.UpgradeRank?.ToString()??"Unknown");
    public int? RequiredLevel { get; set; }
    public EffectIdentity Identity { get; set; } = new("", "", null);
    public int Gem { get; set; }
    public bool Keep { get; set; }
    public bool Available { get; set; }
    public int? Mana { get; set; }
    public int? CooldownMs { get; set; }
    public bool? NonTargetedArea { get; set; }
    public string Source { get; set; } = "";
    public List<EffectInput> Effects { get; set; } = [];
    public bool? AuraEligible => Mana == null || CooldownMs == null || NonTargetedArea == null ? null : Mana == 0 && CooldownMs == 0 && NonTargetedArea == true;
}
public sealed class BardSetup
{
    public double? Instrument { get; set; }
    public double? Singing { get; set; }
    public double? Focus { get; set; }
    public int? EffectiveCasterLevel { get; set; }
    public string AaContext { get; set; } = "";
    public int? ConfirmedAuraRank { get; set; }
    public string AuraEvidence { get; set; } = "";
    public int GemCount { get; set; } = 8;
    public List<BardSong> Songs { get; set; } = [];
    public List<BardSong> Available { get; set; } = [];
    public List<PlannedSongSwap> Planned { get; set; } = [];
}
public sealed record PlannedSongSwap(string Id, DateTime At, int Gem, EffectIdentity Before, EffectIdentity After);
public sealed class SongCoverageRule
{
    public EffectIdentity Song { get; set; } = new("", "", null);
    public string Caster { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Zone { get; set; } = "";
    public double DurationSeconds { get; set; }
    public double Instrument { get; set; }
    public double Singing { get; set; }
    public double Focus { get; set; }
    public int EffectiveCasterLevel { get; set; }
    public string AaContext { get; set; } = "";
    public string Source { get; set; } = "";
    public string Version { get; set; } = "reviewed-coverage-v1";
    public List<EffectInput> Effects { get; set; } = [];
    public void Apply(CoverageObservation observation, string zone)
    {
        if (observation.Song != Song || observation.Caster != Caster || observation.Recipient != Recipient || !observation.SuccessfulApplication || observation.Attribution < EvidenceQuality.UserConfirmed || Zone != zone || Source.Length == 0 || DurationSeconds is <= 0 or > 1800) return;
        observation.Until = observation.At.AddSeconds(DurationSeconds); if(observation.InvalidatedAt<observation.Until)observation.Until=observation.InvalidatedAt; observation.Instrument = Instrument; observation.Singing = Singing; observation.Focus = Focus; observation.EffectiveCasterLevel = EffectiveCasterLevel; observation.AaContext = AaContext; observation.Effects = Effects.Select(EquipmentPositions.Copy).ToList();
    }
}
public sealed record SongRecommendation(int Gem, EffectIdentity Current, EffectIdentity Replacement, string Explanation, IReadOnlyList<string> LostComponents);
public sealed record BardCoverageView(IReadOnlyList<BardSong> AuraSongs, IReadOnlyList<CoverageObservation> Coverage, IReadOnlyList<SongRecommendation> Suggestions, IReadOnlyList<string> Conditions);
public static class BardCoverage
{
    public const string AuraSource = "https://eqlwiki.com/Bard#Symphonic_Aura";
    public static IReadOnlyList<BardSong> AuraSelection(BardSetup setup)
    {
        if (setup.ConfirmedAuraRank is not (>= 1 and <= 5) || string.IsNullOrWhiteSpace(setup.AuraEvidence)) return [];
        if (setup.GemCount is <1 or >16 || setup.Songs.Any(s => s.Gem < 1 || s.Gem > 16) || setup.Songs.GroupBy(s => s.Gem).Any(g => g.Count() > 1)) return [];
        // Unknown eligibility in an earlier selected gem can change the entire selection.
        var ordered = setup.Songs.Where(s=>s.Gem<=setup.GemCount).OrderByDescending(s => s.Gem).ToArray();
        var selected = new List<BardSong>();
        foreach (var song in ordered)
        {
            if (selected.Count == setup.ConfirmedAuraRank.Value) break;
            if (!song.AuraEligible.HasValue) return [];
            if (song.AuraEligible == true) selected.Add(song);
        }
        return selected;
    }
    public static BardCoverageView Evaluate(BardSetup setup, IEnumerable<CoverageObservation> evidence, IReadOnlySet<string> group, EvaluationContext context, IEnumerable<EffectRule> rules, DateTime now)
    {
        var active = evidence.Where(e => group.Contains(e.Caster) && e.Fresh(now, context.Recipient)).GroupBy(e=>(e.Caster,e.Recipient,e.Song)).Select(g=>g.MaxBy(e=>e.At)!).ToArray();
        var suggestions = new List<SongRecommendation>(); var conditions = new List<string>();
        var aura = AuraSelection(setup);
        bool ownPotency = setup.Instrument.HasValue && setup.Singing.HasValue && setup.Focus.HasValue && setup.EffectiveCasterLevel.HasValue && setup.AaContext.Length > 0;
        bool bardAvailable=context.Classes.Contains("BRD")&&context.CasterLevel.HasValue;
        if(!bardAvailable)conditions.Add("Active Bard class and actual character level need confirmation before suggesting a song to load.");
        if (!ownPotency) conditions.Add("Your instrument, singing, focus, effective level or relevant AA context needs review before comparing song potency.");
        if (setup.ConfirmedAuraRank == null) conditions.Add("Confirm Symphonic Aura rank. Five songs are not assumed.");
        else if (aura.Count == 0 && setup.Songs.Count > 0) conditions.Add("Aura selection is unresolved: verify eligible songs and gem positions against client data.");
        if (active.Length == 0) conditions.Add("No fresh, caster-attributed successful coverage for this recipient. Cast attempts and group membership do not establish range.");
        foreach (var song in aura.Where(s => !s.Keep))
        {
            if(!ownPotency||!bardAvailable)continue;
            var other = active.Where(e => e.Caster != context.Character).ToArray();
            // Potency cannot be compared across unspecified modifiers, ranks, or recipients.
            var reliable = other.Where(e => e.Song.UpgradeRank.HasValue && e.Instrument.HasValue && e.Singing.HasValue && e.Focus.HasValue && e.EffectiveCasterLevel.HasValue && e.AaContext.Length > 0).ToArray();
            if (reliable.Length != other.Length) {conditions.Add(song.Identity.Name + ": rank/modifier or recipient uncertainty prevents a definitive overlap recommendation.");continue;}
            var external = reliable.SelectMany(e => e.Effects).ToArray();
            if (external.Length == 0 || song.Effects.Count == 0) continue;
            var current = BuildCompatibility.Evaluate(context, external.Concat(song.Effects), rules);
            var components = current.Contributions.Where(c => song.Effects.Contains(c.Effect)).ToArray();
            bool redundant = components.Length == song.Effects.Count && components.All(c => c.State is ContributionState.Replaced or ContributionState.Blocked || c.State == ContributionState.Capped && c.Usable == 0);
            // Repeated damage is not treated as a redundant persistent benefit.
            if (!redundant || song.Effects.Any(e => e.Channel == EffectChannel.Damage)) continue;
            foreach (var replacement in setup.Available.Where(s => s.Available && s.RequiredLevel.HasValue && s.RequiredLevel<=context.CasterLevel && s.AuraEligible == true && s.Identity.UpgradeRank.HasValue && s.Effects.Count > 0 && !setup.Songs.Any(chosen => chosen.Identity.SpellId == s.Identity.SpellId)))
            {
                var planned = EquipmentPositions.Copy(setup); var slot = planned.Songs.FindIndex(s => s.Gem == song.Gem); planned.Songs[slot] = EquipmentPositions.Copy(replacement); planned.Songs[slot].Gem = song.Gem;
                if (!AuraSelection(planned).Any(s => s.Gem == song.Gem && s.Identity == replacement.Identity)) continue;
                var retained = aura.Where(s => s.Gem != song.Gem).SelectMany(s => s.Effects).ToArray();
                var comparison = BuildCompatibility.Compare(context, external.Concat(retained).Concat(song.Effects), external.Concat(retained).Concat(replacement.Effects), rules);
                if (!comparison.CanClaimUpgrade || !comparison.Changes.Any(c => c.UsableChange > 0) || comparison.Changes.Any(c => c.UsableChange < 0)) continue;
                suggestions.Add(new(song.Gem, song.Identity, replacement.Identity, "Confirmed external coverage replaces every supported component. This available song supplies missing coverage; no cross-family value score is assumed.", []));
            }
        }
        // Multiple incomparable benefits remain options; ordering is stable, never labeled best.
        return new(aura, active, suggestions.OrderBy(s => s.Gem).ThenBy(s => s.Replacement.Name).Take(3).ToArray(), conditions.Distinct().ToArray());
    }
    public static PlannedSongSwap SavePlan(BardSetup setup, SongRecommendation recommendation, DateTime now)
    {
        var song = setup.Songs.SingleOrDefault(s => s.Gem == recommendation.Gem && s.Identity == recommendation.Current);
        if (song == null || song.Keep || !setup.Available.Any(s => s.Available && s.Identity == recommendation.Replacement)) throw new InvalidDataException("The song set changed; review this suggestion again.");
        var change = new PlannedSongSwap(Guid.NewGuid().ToString("N"), now, song.Gem, song.Identity, recommendation.Replacement);
        setup.Planned.RemoveAll(p => p.Gem == song.Gem); setup.Planned.Add(change); return change;
    }
}
