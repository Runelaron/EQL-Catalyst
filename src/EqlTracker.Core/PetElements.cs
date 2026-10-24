using System.Text.RegularExpressions;

namespace EqlTracker.Core;

internal sealed record PendingPetSummon(string Element,string Spell,DateTime Time,string PreviousPet);
internal sealed record PendingPetSummonCheckpoint(string Generation,long Offset,PendingPetSummon? Summon);
public sealed class PetElementDetector
{
    private sealed record Summon(string Element, string Spell, DateTime Time, string PreviousPet);
    private Summon? pending;
    internal PendingPetSummon? Pending=>pending==null?null:new(pending.Element,pending.Spell,pending.Time,pending.PreviousPet);
    internal void RestorePending(PendingPetSummon? saved){pending=saved==null?null:new(saved.Element,saved.Spell,saved.Time,saved.PreviousPet);lastCast=saved?.Spell??"";candidates.Clear();}
    private readonly PetContextCatalog catalog;
    public PetElementDetector(PetContextCatalog? catalog=null){this.catalog=catalog??PetContextCatalog.Bundled;}
    private string lastCast = "";
    private readonly Dictionary<string, Summon> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> signatures = new(StringComparer.OrdinalIgnoreCase);
    public static readonly string[] Elements = ["Earth", "Fire", "Air", "Water"];
    public static bool HasFamilyIcon(string family) => Elements.Contains(family) || family == "Monster";
    public static string? SummonElement(string spell)
    {
        if(PetContextCatalog.Bundled.ResolveCast(spell) is {} known)return known.Spell.PetType;
        var match = Regex.Match(spell, @"\b(?:Conjuration|Summoning|Elementalkin|Elementaling|Vocarate):\s*(Earth|Fire|Air|Wind|Water)\b", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(spell, @"^Monster Summoning (?:III|II|I)(?:\s+(?:[IVX]+|\+\d+))?$", RegexOptions.IgnoreCase)) return "Monster";
        if (!match.Success) return null;
        return match.Groups[1].Value.ToLowerInvariant() switch { "earth" => "Earth", "fire" => "Fire", "air" or "wind" => "Air", _ => "Water" };
    }
    public bool Process(ParsedLine line, CharacterProfile profile)
    {
        bool changed = false;
        if (line.PlayerCast is { } latest) lastCast = latest;
        if (line.PlayerCast is { } spell && (catalog.ResolveCast(spell)?.Spell.PetType??SummonElement(spell)) is { } element)
        {
            string previous = profile.PetIdentities.LastOrDefault(x => x.From <= line.Time && (!x.Until.HasValue || x.Until > line.Time))?.Name ?? "";
            pending = new(element, spell, line.Time, previous);
        }
        if (line.CastFailed && pending != null && (line.FailedSpell ?? lastCast).Equals(pending.Spell, StringComparison.OrdinalIgnoreCase) && line.Time >= pending.Time && line.Time <= pending.Time.AddSeconds(30))
        {
            foreach (var identity in profile.PetIdentities.Where(x => x.ElementObservedAt == pending.Time && x.ElementConfidence == "Correlated summon")) { identity.Element = "Unknown"; identity.ElementConfidence = "Unknown"; identity.ElementEvidence = "Summon attempt failed"; identity.RawSummonSpell="";identity.CanonicalSummonSpell="";identity.SummonCasterClass="";identity.SummonRank=null;identity.SummonedAt=null;identity.SummonEvidence="Summon attempt failed"; changed = true; }
            foreach (string key in candidates.Where(x => x.Value == pending).Select(x => x.Key).ToList()) candidates.Remove(key); pending = null;
        }
        if (line.PetLost) { pending = null; candidates.Clear(); signatures.Clear(); }
        if (line.SlainActor is { } dead){candidates.Remove(dead);if(pending!=null&&profile.PetIdentities.Any(p=>p.Name.Equals(dead,StringComparison.OrdinalIgnoreCase)&&p.From>=pending.Time&&p.From<=line.Time))pending=null;}
        string? actor = line.PetName ?? line.PetActivity ?? (line.Combat?.Owner != "Player" ? line.Combat?.Actor : null);
        if (actor is { Length: > 0 } && actor != "Unattributed")
        {
            var identities = profile.PetIdentities.Where(x => x.Name.Equals(actor, StringComparison.OrdinalIgnoreCase) && x.From <= line.Time && (!x.Until.HasValue || line.Time < x.Until)).ToList();
            // Ownership may arrive minutes after the successful summon. Keep the cast pending,
            // but associate it only with a newly confirmed owned identity, never arbitrary NPC activity.
            if (pending != null && line.Time >= pending.Time.AddSeconds(3)
                && identities.Any(x => x.From >= pending.Time)
                && !profile.PetIdentities.Any(x=>x.From>pending.Time&&x.From<=line.Time&&!x.Name.Equals(actor,StringComparison.OrdinalIgnoreCase))
                && (line.PetName != null || line.PetActivity != null)) candidates[actor] = pending;
            if (identities.Count > 0 && candidates.TryGetValue(actor, out var summon))
                foreach (var identity in identities.Where(x => x.From >= summon.Time.AddSeconds(-2) && x.From<=line.Time && (!x.Until.HasValue || x.Until > summon.Time))) changed |= AssignSummon(identity,summon,actor);
            if (identities.Count > 0)
            {
                string signature = Signature(line.ActivitySpell ?? line.Combat?.Ability ?? "");
                if (signature.Length > 0)
                {
                    string key = actor + "\0" + identities.Last().From.Ticks;
                    if (!signatures.TryGetValue(key, out var values)) signatures[key] = values = [];
                    values.Add(signature);
                    var votes = values.GroupBy(x => x.Split(':')[0]).Where(x => x.Count() >= (x.Key == "Water" ? 1 : 2)).ToList();
                    if (votes.Count == 1) foreach (var identity in identities.Where(x => !x.Until.HasValue || line.Time < x.Until)) changed |= Assign(identity, votes[0].Key, "Inferred", "Owned-pet signatures: " + string.Join(", ", votes[0]), line.Time);
                    else if (votes.Count > 1) foreach (var identity in identities.Where(x => x.ElementConfidence != "Correlated summon")) changed |= Assign(identity, "Unknown", "Conflicting signatures", "Owned-pet signatures conflict: " + string.Join(", ", values), line.Time);
                }
            }
        }
        if (changed && profile.PetElementOverride.Length == 0 && profile.EffectivePetElement != "Unknown") profile.ActiveLoadout.PetType = profile.EffectivePetElement;
        return changed;
    }
    private bool AssignSummon(PetIdentity identity,Summon summon,string actor)
    {
        if(identity.ElementConfidence=="User reviewed"&&identity.Element!=summon.Element)return false;
        bool changed=Assign(identity,summon.Element,"Correlated summon",$"{summon.Spell} at {summon.Time:MMM dd HH:mm:ss}; subsequent {actor} activity and ownership",summon.Time);
        if(identity.SummonedAt>summon.Time)return changed;
        var resolved=catalog.ResolveCast(summon.Spell);
        var before=(identity.RawSummonSpell,identity.CanonicalSummonSpell,identity.SummonRank,identity.SummonedAt,identity.SummonCasterClass);
        identity.RawSummonSpell=summon.Spell;identity.CanonicalSummonSpell=resolved?.Spell.Name??"";identity.SummonCasterClass=resolved?.Spell.CasterClass??"";identity.SummonRank=resolved?.Rank;identity.SummonedAt=summon.Time;identity.SummonEvidence="Summon cast correlated with subsequent owned-pet evidence";
        return changed||before!=(identity.RawSummonSpell,identity.CanonicalSummonSpell,identity.SummonRank,identity.SummonedAt,identity.SummonCasterClass);
    }
    private static bool Assign(PetIdentity identity, string element, string confidence, string evidence, DateTime time)
    {
        if (identity.ElementConfidence == "User reviewed") return false;
        if (identity.ElementConfidence == "Correlated summon" && confidence == "Inferred") return false;
        if (identity.ElementObservedAt > time && identity.ElementConfidence == "Correlated summon") return false;
        if (identity.Element == element && identity.ElementConfidence == confidence) return false;
        identity.Element = element; identity.ElementConfidence = confidence; identity.ElementEvidence = evidence; identity.ElementObservedAt = time; return true;
    }
    private static string Signature(string value)
    {
        string spell = Regex.Replace(value, @"\s+[IVX]+$", "").ToLowerInvariant();
        if (spell.Contains("ensnaring roots")) return "Earth:Ensnaring Roots";
        if (spell.Contains("skin like steel")) return "Earth:Skin like Steel";
        if (spell.Contains("shield of brambles")) return "Earth:Shield of Brambles";
        if (spell is "backstab" or "backstabs") return "Water:Backstab";
        if (spell is "flying kicks" or "round kicks" or "dragon punches") return "Air:" + spell;
        if (spell.Contains("ice comet")) return "Fire:Ice Comet";
        if (spell.Contains("shock of flame")) return "Fire:Shock of Flame";
        return "";
    }
}
