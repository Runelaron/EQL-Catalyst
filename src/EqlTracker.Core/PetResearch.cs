using System.Security.Cryptography;
using System.Text.Json;

namespace EqlTracker.Core;

// Personal research is stored separately from catalogs and combat ingestion. Unknown is null.
public sealed class PetResearchState
{
    public string Version { get; set; } = PetResearch.Version;
    public long Revision { get; set; }
    public Dictionary<string, PetResearchSample> Samples { get; set; } = [];
    public List<PetStatSheet> Sheets { get; set; } = [];
    public List<PetBuffObservation> Buffs { get; set; } = [];
    public List<PetResearchCorrection> Corrections { get; set; } = [];
    public List<PetCalibrationModel> Models { get; set; } = [];
}
public sealed class PetResearchContext
{
    public string Pet { get; set; } = "Unknown";
    public string Summon { get; set; } = "";
    public int? Rank { get; set; }
    public int? Level { get; set; }
    public string Patch { get; set; } = "";
    public string Equipment { get; set; } = "";
    public string Buffs { get; set; } = "Unresolved";
    public string Targets { get; set; } = "";
    public string Positioning { get; set; } = "";
    public string FocusAndAA { get; set; } = "";
    public bool Complete { get; set; }
    public string Key => PetResearch.Hash(new { Pet, Summon, Rank, Level, Patch, Equipment, Buffs, Targets, Positioning, FocusAndAA });
}
public sealed class PetResearchSample
{
    public string EncounterId { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Actor { get; set; } = "";
    public string Targets { get; set; } = "";
    public string Zone { get; set; } = "";
    public long Damage { get; set; }
    public double Seconds { get; set; }
    public long RecordedHits { get; set; }
    public long RecordedMisses { get; set; }
    public double Dps => Damage / Math.Max(1, Seconds);
    public double RecordedHitsPerSecond => RecordedHits / Math.Max(1, Seconds);
    public PetResearchContext Context { get; set; } = new();
    public bool Reviewed { get; set; }
    public string Evidence { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public bool DetailComplete { get; set; }
    public Dictionary<string, long> Components { get; set; } = [];
    public Dictionary<string, long> ComponentRecordedHits { get; set; } = [];
    public Dictionary<string, long> ComponentRecordedMisses { get; set; } = [];
    public Dictionary<string, long> Abilities { get; set; } = [];
    public Dictionary<string, long> BuffPeriodDamage { get; set; } = [];
    public Dictionary<string, double> BuffPeriodSeconds { get; set; } = [];
    public string CalibrationWarning { get; set; } = "Unreviewed context; uncertain transitions excluded from calibration";
}
public sealed class PetStatSheet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public PetResearchContext Context { get; set; } = new();
    public Dictionary<string, double?> Values { get; set; } = PetResearch.StatFields.ToDictionary(k => k, _ => (double?)null);
    public Dictionary<string, double> InvalidValues { get; set; } = [];
    public List<string> EvidenceFiles { get; set; } = [];
    public string Effects { get; set; } = "";
    public string Weapons { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Reviewed { get; set; }
    public void Validate()
    {
        foreach (string field in PetResearch.StatFields) Values.TryAdd(field, null);
        foreach (var pair in Values.ToArray())
            if (pair.Value is {} n && (!double.IsFinite(n) || n < 0 && (pair.Key is "HP" or "Mana" or "Endurance" or "HPCurrent" or "ManaCurrent" or "EnduranceCurrent")))
            { if (double.IsFinite(n)) InvalidValues[pair.Key] = n; Values[pair.Key] = null; }
        if (Context.Rank is < 0 || Context.Level is < 1) throw new ArgumentException("Rank and actual level must be valid or unknown.");
    }
}
public sealed class PetBuffObservation
{
    public string Id { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime At { get; set; }
    public string Spell { get; set; } = "";
    public string SpellLine { get; set; } = "";
    public List<string> SpellCandidates { get; set; } = [];
    public string Transition { get; set; } = "Unresolved"; // Landing, Fade, Cast, Interrupted, CorrectionOn/Off
    public string Evidence { get; set; } = "";
    public int? UpgradeRank { get; set; }
    public int? AaRank { get; set; }
    public double? DurationSeconds { get; set; }
    public Dictionary<string, double> Displayed { get; set; } = [];
}
public sealed class PetResearchCorrection
{
    public DateTime From { get; set; }
    public DateTime Until { get; set; }
    public string Actor { get; set; } = "";
    public PetResearchContext Context { get; set; } = new();
    public string Evidence { get; set; } = "";
    public List<string> ProcAbilities { get; set; } = [];
}
public sealed class PetCalibrationTrial
{
    public string RunId { get; set; } = "";
    public bool Holdout { get; set; }
    public double InputDelta { get; set; }
    public double OutputDelta { get; set; }
    public bool BaselineRestored { get; set; }
    public bool Controlled { get; set; }
    public string Evidence { get; set; } = "";
}
public sealed class PetCalibrationModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Input { get; set; } = "STA";
    public string Output { get; set; } = "HP";
    public PetResearchContext Context { get; set; } = new();
    public List<PetCalibrationTrial> Trials { get; set; } = [];
    public double Tolerance { get; set; } = 1;
    public bool LocallyApproved { get; set; }
    public string ReviewEvidence { get; set; } = "";
    public double? Coefficient { get { var training=Training;if(training.Count==0)return null;double value=training.Sum(t=>t.InputDelta*t.OutputDelta)/training.Sum(t=>t.InputDelta*t.InputDelta);return double.IsFinite(value)?value:null; } }
    private List<PetCalibrationTrial> Training => Trials.Where(t => !t.Holdout && t.Controlled && t.BaselineRestored && t.InputDelta != 0 && double.IsFinite(t.InputDelta) && double.IsFinite(t.OutputDelta)).ToList();
    public string Gate
    {
        get
        {
            if (!Context.Complete || !Context.Rank.HasValue || !Context.Level.HasValue || Context.Patch != PetDefinition.Version || Context.Summon.Length == 0 || Context.Pet == "Unknown") return "Complete current-patch context required";
            if (!double.IsFinite(Tolerance) || Tolerance < 0) return "Invalid tolerance";
            var training = Training; var holdouts = Trials.Where(t => t.Holdout && t.Controlled && t.BaselineRestored && t.InputDelta != 0 && double.IsFinite(t.InputDelta) && double.IsFinite(t.OutputDelta)).ToList();
            if (training.Count < 3 || training.Select(t => t.RunId).Distinct().Count() < 3) return "Three independent controlled A/B/A repeats required";
            if (Trials.Any(t => t.RunId.Length == 0 || t.Evidence.Length == 0) || Trials.Select(t => t.RunId).Distinct().Count() != Trials.Count) return "Distinct run IDs and evidence required";
            if (holdouts.Count == 0) return "Independent holdout required";
            if(Coefficient is not {} c) return "Invalid numerical range";
            if (!double.IsFinite(c) || training.Concat(holdouts).Any(t => Math.Abs(t.OutputDelta - c * t.InputDelta) > Tolerance)) return "Training or holdout failed";
            return LocallyApproved && ReviewEvidence.Length > 0 ? "Validated locally" : "Ready for explicit local review";
        }
    }
    public double? EstimateDelta(double inputDelta, PetResearchContext context) => double.IsFinite(inputDelta) && Gate == "Validated locally" && context.Complete && context.Key == Context.Key && Math.Abs(inputDelta) <= Trials.Max(t => Math.Abs(t.InputDelta)) && (inputDelta >= 0 || Trials.Any(t => t.InputDelta < 0)) ? inputDelta * Coefficient : null;
}
public sealed record PetMeasuredSummary(double? Dps, double? Hp, string Ac, int Fights, double Seconds, string Conditions, bool Comparable, string StatConditions = "");

public static class PetResearch
{
    public const string Version = "local-pet-research-v1";
    public static readonly string[] StatFields = ["HP", "Mana", "Endurance", "ACLeft", "ACRight", "AttackLeft", "AttackRight", "STR", "STA", "INT", "WIS", "AGI", "DEX", "CHA", "AttackSpeedPercent", "HPRegen", "ManaRegen", "EnduranceRegen", "ResistFire", "ResistCold", "ResistMagic", "ResistDisease", "ResistPoison", "ResistVoid", "ResistCorruption", "Offense", "Accuracy", "Mitigation", "Evasion", "InnateDamage", "InnateDelay", "DamageBonus", "DualWieldSkill", "DoubleAttackSkill", "BackstabSkill", "DamageShield", "AbsorptionRemaining", "AbsorptionMaximum", "PrimaryRatio", "SecondaryRatio", "HPCurrent", "ManaCurrent", "EnduranceCurrent", "PrimaryDamage", "PrimaryDelay", "SecondaryDamage", "SecondaryDelay", "PrimaryAccuracyPercent", "SecondaryAccuracyPercent", "PrimaryWeight", "SecondaryWeight"];
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string BuffFamily(string spell) => spell.StartsWith("Frenzied Burnout", StringComparison.OrdinalIgnoreCase) ? "Frenzied Burnout" : spell.StartsWith("Burnout", StringComparison.OrdinalIgnoreCase) ? "Burnout" : spell;
    public static bool Observe(PetResearchState state, PetBuffObservation observation)
    {
        if (observation.Id.Length == 0 || state.Buffs.Any(b => b.Id == observation.Id)) return false;
        NormalizeBuff(observation);
        state.Buffs.Add(observation); state.Revision++; return true;
    }
    public static void NormalizeBuff(PetBuffObservation observation)
    {
        var match = System.Text.RegularExpressions.Regex.Match(observation.Spell,@"^(Burnout (?:IV|III|II|I))(?: ([IVX]+))?$");
        if (match.Success)
        {
            observation.SpellLine = match.Groups[1].Value;
            if (match.Groups[2].Success && observation.UpgradeRank == null)
            {
                string roman=match.Groups[2].Value; int result=0,previous=0;
                foreach(char letter in roman.Reverse()){int value=letter=='X'?10:letter=='V'?5:1;result+=value<previous?-value:value;previous=value;}
                observation.UpgradeRank=result;
            }
        }
        if(observation.Spell.StartsWith("Verses of Victory",StringComparison.OrdinalIgnoreCase))
        {var identity=new ConnectedSpellIndex([]).Resolve(observation.Spell);observation.SpellLine=identity.Canonical;observation.UpgradeRank??=identity.Rank;}
    }
    internal static bool ReprojectBuffMetadata(PetResearchState state,ConnectedSpellState history,CharacterProfile profile)
    {
        bool changed=false;
        var byId=state.Buffs.ToDictionary(b=>b.Id);
        foreach(var record in history.History.Where(e=>e.Transition is "Landing" or "Fade"&&BuffFamily(e.Canonical) is "Burnout" or "Frenzied Burnout"))
        {
            var pet=PetIdentityResolver.At(profile,record.At,record.Recipient);
            if(pet==null||!pet.Name.Equals(record.Recipient,StringComparison.OrdinalIgnoreCase))continue;
            string id=record.Id.Replace(":spell-effect",":effect",StringComparison.Ordinal);
            var inspect=state.Buffs.LastOrDefault(b=>b.Transition=="Inspect"&&(b.SpellLine==record.Canonical||b.Spell==record.Canonical)&&(BuffFamily(record.Canonical)=="Frenzied Burnout"||b.UpgradeRank==record.Rank));
            var next=new PetBuffObservation{Id=id,Actor=pet.Name,At=record.At,Spell=record.Canonical+(record.Rank>0?" +"+record.Rank:""),SpellLine=record.Canonical,UpgradeRank=BuffFamily(record.Canonical)=="Frenzied Burnout"?null:record.Rank,SpellCandidates=record.Candidates.ToList(),Transition=record.Transition,DurationSeconds=inspect?.DurationSeconds,Evidence=record.Source};
            // Preserve the exact raw ranked spelling where research normalization already knows it.
            if(byId.TryGetValue(id,out var prior))
            {if(prior.Transition is "CorrectionOn" or "CorrectionOff"||Hash(prior)==Hash(next))continue;state.Buffs[state.Buffs.IndexOf(prior)]=next;}
            else state.Buffs.Add(next);
            byId[id]=next;changed=true;
        }
        if(changed)state.Revision++;return changed;
    }
    public static string ResolveBuffLanding(PetResearchState state, string actor, DateTime at, IReadOnlyList<string> candidates)
    {
        if(candidates.Count==0)return "Buff unresolved";
        var index=new ConnectedSpellIndex([]);var canonical=candidates.Select(c=>index.Resolve(c).Canonical).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cast = state.Buffs.Where(b=>b.Actor==actor && b.Transition=="Cast" && b.At<=at && b.At>=at.AddSeconds(-30) && canonical.Contains(index.Resolve(b.Spell).Canonical)).MaxBy(b=>b.At);
        if(cast!=null && !state.Buffs.Any(b=>b.Actor==actor && b.Spell==cast.Spell && b.Transition=="Interrupted" && b.At>=cast.At && b.At<=at)) return cast.Spell;
        return candidates.Count==1?candidates[0]:BuffFamily(candidates[0])+" · rank unresolved";
    }
    // Casts never turn a buff on. A fade bounds history but cannot establish its start.
    public static string BuffPeriod(IEnumerable<PetBuffObservation> observations, string actor, DateTime time)
    {
        var states = new List<bool?>();
        foreach (string family in new[] { "Burnout", "Frenzied Burnout" })
        {
            var latest = observations.Where(b => b.Actor.Equals(actor, StringComparison.OrdinalIgnoreCase) && BuffFamily(b.Spell) == family && b.At <= time && b.Transition is "Landing" or "Fade" or "CorrectionOn" or "CorrectionOff").MaxBy(b => b.At);
            if (latest == null) states.Add(null);
            else if (latest.Transition is "Fade" or "CorrectionOff") states.Add(false);
            else if (latest.DurationSeconds is {} duration && time > latest.At.AddSeconds(duration)&&!observations.Any(b=>b.Actor.Equals(actor,StringComparison.OrdinalIgnoreCase)&&BuffFamily(b.Spell)==family&&b.At>time&&b.Transition is "Fade" or "CorrectionOff")) states.Add(null);
            else if (latest.DurationSeconds == null && !observations.Any(b => b.Actor.Equals(actor,StringComparison.OrdinalIgnoreCase) && BuffFamily(b.Spell)==family && b.At>time && b.Transition is "Fade" or "CorrectionOff")) states.Add(null);
            else states.Add(true);
        }
        if (states.Contains(null)) return "Unresolved";
        return states[0] == true ? states[1] == true ? "Mixed" : "Burnout" : states[1] == true ? "Frenzied Burnout" : "Neither observed active";
    }
    public static bool Collect(PetResearchState state, IEnumerable<Encounter> encounters, CharacterProfile profile)
    {
        bool changed = false;
        foreach (var fight in encounters.Where(f => f.Finished && f.PetDamage > 0))
        {
            var correction = state.Corrections.LastOrDefault(c => fight.Start >= c.From && fight.End <= c.Until && (c.Actor.Length == 0 || c.Actor.Equals(fight.PetName, StringComparison.OrdinalIgnoreCase)));
            var identity = PetIdentityResolver.At(profile, fight.Start, fight.PetName);
            string fingerprint = Hash(new { Version, Projection="buff-phases-v2", fight.Start, fight.End, fight.PetDamage, fight.PetType, fight.PetElementEvidence, fight.DetailAvailable, Events = fight.Events.Select(e => new { e.Id, e.Time,e.Actor,e.Owner, e.Ability, e.Kind, e.Damage, e.Outcome }), correction, identity, Buffs = state.Buffs.Where(b => b.Actor == fight.PetName).ToArray() });
            state.Samples.TryGetValue(fight.Id, out var prior); if (prior?.SourceFingerprint == fingerprint) continue;
            var context = prior?.Reviewed == true ? prior.Context : correction?.Context ?? new PetResearchContext { Pet = identity?.Element is { Length: > 0 } family ? family : fight.PetType is { Length: > 0 } p ? p : "Unknown", Equipment = fight.Weapons, Buffs = fight.BuffNotes.Length > 0 ? fight.BuffNotes : "Unresolved", Targets = fight.Targets, Patch = fight.Start.Date < OctoberContent.PatchDate ? "Historical" : "Patch unconfirmed" };
            var sample = new PetResearchSample { EncounterId = fight.Id, Start = fight.Start, End = fight.End, Actor = fight.PetName, Targets = fight.Targets, Zone = fight.Zone, Damage = fight.PetDamage, Seconds = fight.Duration, Context = context, Reviewed = prior?.Reviewed == true || correction != null, Evidence = prior?.Reviewed == true ? prior.Evidence : correction?.Evidence ?? identity?.ElementEvidence ?? fight.PetElementEvidence, SourceFingerprint = fingerprint, DetailComplete = fight.DetailAvailable };
            foreach (var e in fight.Events.Where(e => e.Owner == "Pet" && !e.ActivityOnly))
            {
                string component = correction?.ProcAbilities.Contains(e.Ability, StringComparer.OrdinalIgnoreCase) == true ? "Corroborated procs" : e.Kind != "Melee" ? "Spells / unclassified procs" : new[] { "bashes", "kicks", "backstabs", "flying kicks", "round kicks", "dragon punches", "frenzies" }.Contains(e.Ability.ToLowerInvariant()) ? "Supported specials" : e.Ability.Equals("strikes", StringComparison.OrdinalIgnoreCase) || e.Ability.Equals("cleaves", StringComparison.OrdinalIgnoreCase) ? "Unclassified melee" : "Melee";
                sample.Components[component] = sample.Components.GetValueOrDefault(component) + e.Damage;
                sample.Abilities[e.Ability] = sample.Abilities.GetValueOrDefault(e.Ability) + e.Damage;
                string period = BuffPeriod(state.Buffs, e.Actor, e.Time);
                sample.BuffPeriodDamage[period] = sample.BuffPeriodDamage.GetValueOrDefault(period) + e.Damage;
                if (e.Damage > 0) { sample.RecordedHits++; sample.ComponentRecordedHits[component]=sample.ComponentRecordedHits.GetValueOrDefault(component)+1; }
                else if (e.Outcome.Contains("miss", StringComparison.OrdinalIgnoreCase)) { sample.RecordedMisses++; sample.ComponentRecordedMisses[component]=sample.ComponentRecordedMisses.GetValueOrDefault(component)+1; }
            }
            long missing = sample.Damage - sample.Components.Values.Sum(); if (missing != 0) sample.Components["Unresolved detail / retained total"] = missing;
            if(sample.Reviewed)sample.CalibrationWarning=sample.Context.Complete?"Reviewed context; calibration still requires controlled repeats, independent holdout and local approval":"Reviewed history; incomplete controls or uncertain buff transitions excluded from calibration";
            long unallocated = sample.Damage - sample.BuffPeriodDamage.Values.Sum(); if (unallocated != 0) sample.BuffPeriodDamage["Unresolved"] = sample.BuffPeriodDamage.GetValueOrDefault("Unresolved") + unallocated;
            var finish = fight.Start.AddSeconds(fight.Duration);
            var boundaries = state.Buffs.Where(b => b.Actor == fight.PetName).SelectMany(b => b.DurationSeconds is {} d ? new[] { b.At, b.At.AddSeconds(d) } : new[] { b.At }).Where(t => t > fight.Start && t < finish).Append(fight.Start).Append(finish).Distinct().Order().ToArray();
            for (int i = 1; i < boundaries.Length; i++)
            {
                string period = BuffPeriod(state.Buffs, fight.PetName, boundaries[i - 1].AddTicks(1));
                sample.BuffPeriodSeconds[period] = sample.BuffPeriodSeconds.GetValueOrDefault(period) + (boundaries[i] - boundaries[i - 1]).TotalSeconds;
            }
            state.Samples[fight.Id] = sample; changed = true;
        }
        if (changed) state.Revision++; return changed;
    }
    public static PetMeasuredSummary Summary(PetResearchState state, string family)
    {
        var samples = state.Samples.Values.Where(s => s.Context.Pet == family).OrderByDescending(s => s.Start).ToList();
        // One context cohort: never blend different ranks, buffs, patches or gear silently.
        var newest = samples.FirstOrDefault(s => s.Reviewed) ?? samples.FirstOrDefault(); var cohort = newest == null ? [] : samples.Where(s => s.Context.Key == newest.Context.Key).ToList();
        var sheet = state.Sheets.Where(s => s.Reviewed && s.Context.Pet == family).MaxBy(s => s.CapturedAt);
        string Val(string k) => sheet?.Values.GetValueOrDefault(k)?.ToString("0.##") ?? "?";
        double seconds = cohort.Sum(s => s.Seconds);
        return new(seconds > 0 ? cohort.Sum(s => s.Damage) / seconds : null, sheet?.Values.GetValueOrDefault("HP"), sheet == null ? "Unknown" : Val("ACLeft") + " | " + Val("ACRight"), cohort.Count, seconds,
            newest == null ? "No combat sample" : newest.Context.Summon + " · rank " + (newest.Context.Rank?.ToString() ?? "?") + " · actual level " + (newest.Context.Level?.ToString() ?? "?") + " · " + newest.Context.Buffs + " · " + newest.Context.Patch + " · " + (newest.Context.Complete ? newest.Context.Positioning : "Incomplete context; historical measurement"), false,
            sheet == null ? "No reviewed stat capture" : "Stats: rank "+sheet.Context.Rank+" · actual level "+sheet.Context.Level+" · "+sheet.Context.Buffs+" · "+sheet.CapturedAt.ToString("MMM dd HH:mm"));
    }
}
