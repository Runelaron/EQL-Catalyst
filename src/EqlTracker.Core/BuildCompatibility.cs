namespace EqlTracker.Core;

public enum EvidenceQuality { Unknown, Estimated, UserConfirmed, Observed }
public enum ContributionState { Applied, Replaced, Blocked, Capped, Conditional, Unresolved }
public enum EffectChannel { Attribute, WornHaste, OrdinaryHaste, Overhaste, Damage, Utility }
public enum EffectRuleKind { Sum, Strongest, Coexists, BlocksWholeSpell, ReplacesWholeSpell }

public sealed record EffectIdentity(string SpellId, string Name, int? UpgradeRank);
public sealed class EvaluationContext
{
    public string Character { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string PetType { get; set; } = "";
    public int? PetLevel { get; set; }
    public bool SummonedPet { get; set; }
    public bool CharmedPet { get; set; }
    public string Scenario { get; set; } = "Unbuffed";
    public string Fingerprint { get; set; } = "";
    public string Patch { get; set; } = "";
    public string Server { get; set; } = "";
    public List<string> Classes { get; set; } = [];
    public Dictionary<string, int?> AAs { get; set; } = [];
    public double? InstrumentModifier { get; set; }
    public double? SingingModifier { get; set; }
    public double? FocusModifier { get; set; }
    public int? EffectiveCasterLevel { get; set; }
    public int? CasterLevel { get; set; }
    public double? ObservedOrdinaryAttackSpeed { get; set; }
    public EvidenceQuality SpeedEvidence { get; set; }
    public string SpeedSource { get; set; } = "";
    // Only a reviewed conversion can combine worn/spell parameters into display units.
    public double? OrdinaryDisplayBaseline { get; set; }
    public bool DisplayCompositionConfirmed { get; set; }
    [System.Text.Json.Serialization.JsonInclude] internal double? ReviewedOrdinaryCeiling {get;set;}
    [System.Text.Json.Serialization.JsonInclude] internal string CeilingFingerprint {get;set;}="";
    public bool Live { get; set; }
}
public sealed class EffectRule
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "1";
    public string Source { get; set; } = "";
    public EvidenceQuality Quality { get; set; }
    public EffectRuleKind Kind { get; set; }
    public string Family { get; set; } = "";
    public List<string> SpellIds { get; set; } = [];
    public Dictionary<string, int?> Ranks { get; set; } = [];
    public string ContextFingerprint { get; set; } = "";
    public string Patch { get; set; } = "";
    public string Server { get; set; } = "";
    public string RecipientKind { get; set; } = "";
    public int? PetLevel { get; set; }
    public string Winner { get; set; } = "";
    public bool Applies(EvaluationContext context, IReadOnlyList<EffectInput> effects) =>
        Quality >= EvidenceQuality.UserConfirmed && Source.Length > 0 && Version.Length > 0 &&
        (ContextFingerprint.Length == 0 || ContextFingerprint == context.Fingerprint) &&
        (Patch.Length == 0 || Patch == context.Patch) &&
        (Server.Length == 0 || Server == context.Server) &&
        (!PetLevel.HasValue || PetLevel == context.PetLevel) &&
        (RecipientKind.Length == 0 || RecipientKind == "SummonedPet" && context.SummonedPet && !context.CharmedPet || RecipientKind == "Self" && !context.SummonedPet && !context.CharmedPet) &&
        SpellIds.All(id => effects.Any(e => e.Identity.SpellId == id && (!Ranks.TryGetValue(id, out int? rank) || e.Identity.UpgradeRank == rank)));
}
public sealed class EffectInput
{
    public string Instance { get; set; } = "";
    public EffectIdentity Identity { get; set; } = new("", "", null);
    public string Caster { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Family { get; set; } = "";
    public EffectChannel Channel { get; set; }
    public double? Amount { get; set; }
    public EvidenceQuality Quality { get; set; }
    public string Source { get; set; } = "";
    public bool Equipment { get; set; }
    public bool Burst { get; set; }
    public bool DisplayUnitsConfirmed { get; set; }
    public int? ItemRank { get; set; }
}
public sealed record EffectContribution(EffectInput Effect, ContributionState State, double? Usable, string Explanation, string Rule = "");
public sealed class CompatibilityResult
{
    public string Version { get; init; } = BuildCompatibility.Version;
    public List<EffectContribution> Contributions { get; init; } = [];
    public Dictionary<string, double?> Totals { get; init; } = [];
    public double? OrdinaryAttackSpeed { get; set; }
    public bool OrdinaryCapApplies { get; set; }
    public bool Uncertain => Contributions.Any(e => e.State is ContributionState.Unresolved or ContributionState.Conditional);
    public List<string> Notes { get; init; } = [];
}
public sealed record CompatibilityDelta(string Family, double? Before, double? After, double? UsableChange);
public sealed record CompatibilityComparison(CompatibilityResult Before, CompatibilityResult After, IReadOnlyList<CompatibilityDelta> Changes, string Conclusion, bool CanClaimUpgrade);

/// <summary>Pure evaluation of explicitly supported mechanics. Client slots/names are never stacking rules.</summary>
public static class BuildCompatibility
{
    public const string Version = "compatibility-v2-reviewed-cap";
    public const double Level50SummonedPetOrdinaryCeiling = 171;
    public const string CeilingSource = "Reviewed ordinary displayed attack-speed ceiling in the matching equipped pet/buff capture; hidden overflow and other contexts unresolved";
    public static bool HasOrdinaryCeiling(EvaluationContext context) => context.SummonedPet && !context.CharmedPet && context.PetLevel == 50 && context.ReviewedOrdinaryCeiling==171 && context.Fingerprint.Length>0 && context.CeilingFingerprint==context.Fingerprint && context.Patch==PetDefinition.Version && context.SpeedSource.Length>0;
    public static CompatibilityResult Evaluate(EvaluationContext context, IEnumerable<EffectInput> inputs, IEnumerable<EffectRule> rules)
    {
        var effects = inputs.Where(e => !e.Burst || context.Scenario == "Burst").ToList();
        var applicable = rules.Where(r => r.Applies(context, effects)).OrderByDescending(r => r.ContextFingerprint.Length).ThenByDescending(r => r.Quality).ToArray();
        var result = new CompatibilityResult { OrdinaryCapApplies = HasOrdinaryCeiling(context) };
        var excluded = new Dictionary<string, (ContributionState State, EffectRule Rule)>();
        foreach (var rule in applicable.Where(r => r.Kind is EffectRuleKind.BlocksWholeSpell or EffectRuleKind.ReplacesWholeSpell))
            foreach (string id in rule.SpellIds.Where(id => id != rule.Winner))
                excluded.TryAdd(id, (rule.Kind == EffectRuleKind.BlocksWholeSpell ? ContributionState.Blocked : ContributionState.Replaced, rule));
        foreach (var e in effects.Where(e => excluded.ContainsKey(e.Identity.SpellId)))
        {
            var exclusion = excluded[e.Identity.SpellId];
            result.Contributions.Add(new(e, exclusion.State, 0, "Complete spell excluded, including its other components. " + exclusion.Rule.Source, exclusion.Rule.Id));
        }
        foreach (var group in effects.Where(e => !excluded.ContainsKey(e.Identity.SpellId)).GroupBy(e => e.Family + "/" + e.Channel))
        {
            var members = group.ToArray(); string family = members[0].Family;
            var rule = applicable.FirstOrDefault(r => r.Family == family && r.Kind is EffectRuleKind.Sum or EffectRuleKind.Strongest && members.All(e => r.SpellIds.Contains(e.Identity.SpellId)));
            // Equipment attributes sum; worn haste is already the supported strongest-item rule.
            bool worn = members.All(e => e.Equipment && e.Channel == EffectChannel.WornHaste);
            bool sum = members.All(e => e.Equipment && e.Channel == EffectChannel.Attribute) || rule?.Kind == EffectRuleKind.Sum;
            bool strongest = worn || rule?.Kind == EffectRuleKind.Strongest;
            bool coexist = members.Length == 1 || applicable.Any(r => r.Kind == EffectRuleKind.Coexists && members.All(e => r.SpellIds.Contains(e.Identity.SpellId)));
            bool known = members.All(e => e.Amount.HasValue && double.IsFinite(e.Amount.Value) && e.Quality != EvidenceQuality.Unknown) && (members[0].Channel != EffectChannel.Overhaste || rule != null);
            if (!known || !sum && !strongest && !coexist)
            {
                result.Totals[group.Key] = null;
                foreach (var effect in members) result.Contributions.Add(new(effect, ContributionState.Unresolved, null, known ? "Simultaneous contribution needs a context-matched rule; names and client slots do not prove stacking." : "Magnitude or rank is unknown; it is not treated as zero."));
                continue;
            }
            if (coexist && members.Length > 1 && !sum && !strongest)
            {
                result.Totals[group.Key] = null;
                foreach (var effect in members) result.Contributions.Add(new(effect, ContributionState.Conditional, null, "Whole spells coexist; aggregation of this stat is not established."));
                continue;
            }
            var winner = strongest ? members.MaxBy(e => e.Amount) : null;
            result.Totals[group.Key] = strongest ? winner!.Amount : members.Sum(e => e.Amount!.Value);
            foreach (var effect in members)
            {
                bool contributes = !strongest || ReferenceEquals(effect, winner);
                result.Contributions.Add(new(effect, contributes ? ContributionState.Applied : ContributionState.Replaced, contributes ? effect.Amount : 0,
                    contributes ? (rule?.Source ?? (worn ? "Strongest worn haste; spell haste and overhaste remain separate." : "Supported equipment contribution.")) : "Spells/equipment may coexist; a stronger source supplies this stat. Other components are evaluated independently.", rule?.Id ?? ""));
            }
        }
        var ordinary = result.Contributions.Where(c => c.Effect.Channel is EffectChannel.WornHaste or EffectChannel.OrdinaryHaste && c.State is not (ContributionState.Blocked or ContributionState.Replaced)).ToArray();
        bool convertible = context.DisplayCompositionConfirmed && context.OrdinaryDisplayBaseline.HasValue && ordinary.All(c => c.Usable.HasValue && c.Effect.DisplayUnitsConfirmed);
        if (convertible)
        {
            double raw = context.OrdinaryDisplayBaseline!.Value + ordinary.Sum(c => c.Usable!.Value);
            result.OrdinaryAttackSpeed = result.OrdinaryCapApplies ? Math.Min(raw, Level50SummonedPetOrdinaryCeiling) : raw;
            if (result.OrdinaryCapApplies && raw > Level50SummonedPetOrdinaryCeiling) result.Notes.Add("Ordinary contribution is limited by the displayed 171 ceiling. " + CeilingSource);
        }
        else if (context.ObservedOrdinaryAttackSpeed is {} observed && context.SpeedEvidence >= EvidenceQuality.UserConfirmed)
        {
            result.OrdinaryAttackSpeed = observed;
            result.Notes.Add("Observed ordinary display "+observed.ToString("0.###")+" · "+context.SpeedSource+". Comparison assumes the captured buff, focus and AA context; this is not a live buff roster.");
        }
        if (result.OrdinaryCapApplies) result.Notes.Add(CeilingSource + ". Hidden overflow is unknown unless independently reviewed.");
        if (effects.Any(e => e.Channel == EffectChannel.Overhaste)) result.Notes.Add("Overhaste remains separate; the ordinary 171 ceiling establishes no overhaste result.");
        return result;
    }
    public static CompatibilityComparison Compare(EvaluationContext context, IEnumerable<EffectInput> before, IEnumerable<EffectInput> after, IEnumerable<EffectRule> rules)
    {
        var left = before.ToArray(); var right = after.ToArray(); var ruleSet = rules.ToArray();
        return ComparePrepared(context,left,right,ruleSet,null);
    }
    internal static CompatibilityComparison ComparePrepared(EvaluationContext context,EffectInput[] left,EffectInput[] right,EffectRule[] ruleSet,CompatibilityResult? baseline)
    {
        var a = baseline??Evaluate(context, left, ruleSet); var b = Evaluate(context, right, ruleSet);
        static bool Ordinary(EffectInput e)=>e.Channel is EffectChannel.WornHaste or EffectChannel.OrdinaryHaste;
        static bool SameInput(EffectInput a,EffectInput b)=>ReferenceEquals(a,b)||a.Instance==b.Instance&&a.Identity==b.Identity&&a.ItemRank==b.ItemRank&&a.Family==b.Family&&a.Channel==b.Channel&&a.Amount==b.Amount&&a.Caster==b.Caster&&a.Recipient==b.Recipient&&a.Quality==b.Quality&&a.DisplayUnitsConfirmed==b.DisplayUnitsConfirmed;
        bool ordinaryChanged=left.Count(Ordinary)!=right.Count(Ordinary)||left.Where(Ordinary).Any(e=>!right.Any(other=>SameInput(e,other)));
        var ordinaryKeys=a.Totals.Keys.Concat(b.Totals.Keys).Where(k=>k.EndsWith("/WornHaste",StringComparison.Ordinal)||k.EndsWith("/OrdinaryHaste",StringComparison.Ordinal)).Distinct().ToArray();
        bool ordinaryParametersEqual=ordinaryKeys.All(k=>a.Totals.GetValueOrDefault(k).HasValue&&b.Totals.GetValueOrDefault(k).HasValue&&a.Totals[k]==b.Totals[k]);
        bool displayChange=ordinaryChanged&&!ordinaryParametersEqual;
        if(displayChange&&!context.DisplayCompositionConfirmed)b.OrdinaryAttackSpeed=null;
        var changes = a.Totals.Keys.Concat(b.Totals.Keys).Distinct().Select(k =>
        {
            double? old = a.Totals.TryGetValue(k, out var av) ? av : 0;
            double? next = b.Totals.TryGetValue(k, out var bv) ? bv : 0;
            return new CompatibilityDelta(k, old, next, next - old);
        }).ToList();
        string conclusion = "";
        bool noSpeedGain = ordinaryChanged&&context.SpeedEvidence >= EvidenceQuality.UserConfirmed && context.ObservedOrdinaryAttackSpeed >= Level50SummonedPetOrdinaryCeiling && HasOrdinaryCeiling(context) &&
            !b.Contributions.Any(c=>(c.State is ContributionState.Unresolved or ContributionState.Conditional)&&!left.Any(e=>SameInput(e,c.Effect)))&&OrdinaryNondecreasing(a.Contributions.Where(c=>c.State is not(ContributionState.Blocked or ContributionState.Replaced)).Select(c=>c.Effect).ToArray(),b.Contributions.Where(c=>c.State is not(ContributionState.Blocked or ContributionState.Replaced)).Select(c=>c.Effect).ToArray()) && left.Any(Ordinary) && right.Any(Ordinary);
        if (noSpeedGain || ordinaryChanged&&context.DisplayCompositionConfirmed && a.OrdinaryCapApplies && a.OrdinaryAttackSpeed == Level50SummonedPetOrdinaryCeiling && b.OrdinaryAttackSpeed == a.OrdinaryAttackSpeed)
        {
            conclusion = "No additional ordinary attack speed with this setup. Other applicable stats still contribute.";
            changes.Add(new("Ordinary displayed attack speed", a.OrdinaryAttackSpeed, a.OrdinaryAttackSpeed, 0));
            b.OrdinaryAttackSpeed=a.OrdinaryAttackSpeed;
            b.Notes.Add("No exact wasted amount or safe reduction follows from a capped display; hidden overflow is unknown.");
            for (int i = 0; i < b.Contributions.Count; i++)
                if (b.Contributions[i] is { Effect.Channel: EffectChannel.WornHaste or EffectChannel.OrdinaryHaste, State: ContributionState.Applied } c)
                    b.Contributions[i] = c with { State = ContributionState.Capped, Explanation = "No additional ordinary attack speed at the scoped ceiling. This does not block the whole effect. " + c.Explanation };
        }
        else if (a.Uncertain || b.Uncertain) conclusion = "Some contributions remain unresolved; an overall upgrade is not established.";
        if(displayChange&&!changes.Any(c=>c.Family=="Ordinary displayed attack speed"))changes.Add(new("Ordinary displayed attack speed",a.OrdinaryAttackSpeed,b.OrdinaryAttackSpeed,b.OrdinaryAttackSpeed-a.OrdinaryAttackSpeed));
        if(changes.Any(c=>c.UsableChange==null)&&conclusion.Length==0)conclusion="Usable contributions remain unresolved; an overall upgrade is not established.";
        return new(a, b, changes, conclusion, !a.Uncertain && !b.Uncertain && changes.All(c => c.UsableChange.HasValue));
    }
    private static bool OrdinaryNondecreasing(EffectInput[] before, EffectInput[] after)
    {
        foreach (var channel in new[] { EffectChannel.WornHaste, EffectChannel.OrdinaryHaste })
        {
            var a = before.Where(e => e.Channel == channel).ToArray(); var b = after.Where(e => e.Channel == channel).ToArray();
            if (a.Any(e => e.Amount == null) || b.Any(e => e.Amount == null)) return false;
            if (channel == EffectChannel.WornHaste) { if (b.Select(e => e.Amount!.Value).DefaultIfEmpty().Max() < a.Select(e => e.Amount!.Value).DefaultIfEmpty().Max()) return false; }
            else if (!a.Select(e => (e.Identity, e.Amount, e.Caster, e.Recipient)).OrderBy(e => e.Identity.SpellId).SequenceEqual(b.Select(e => (e.Identity, e.Amount, e.Caster, e.Recipient)).OrderBy(e => e.Identity.SpellId))) return false;
        }
        return true;
    }
}
