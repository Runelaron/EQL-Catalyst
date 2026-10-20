using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed class PetElementDetector
{
    private sealed record Summon(string Element, string Spell, DateTime Time, string PreviousPet);
    private Summon? pending;
    private string lastCast = "";
    private readonly Dictionary<string, Summon> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> signatures = new(StringComparer.OrdinalIgnoreCase);
    public static readonly string[] Elements = ["Earth", "Fire", "Air", "Water"];
    public static string? SummonElement(string spell)
    {
        var match = Regex.Match(spell, @"\b(?:Conjuration|Summoning|Elementalkin|Elementaling|Vocarate):\s*(Earth|Fire|Air|Wind|Water)\b", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups[1].Value.ToLowerInvariant() switch { "earth" => "Earth", "fire" => "Fire", "air" or "wind" => "Air", _ => "Water" };
    }
    public bool Process(ParsedLine line, CharacterProfile profile)
    {
        bool changed = false;
        if (line.PlayerCast is { } latest) lastCast = latest;
        if (line.PlayerCast is { } spell && SummonElement(spell) is { } element)
        {
            string previous = profile.PetIdentities.LastOrDefault(x => x.From <= line.Time && (!x.Until.HasValue || x.Until > line.Time))?.Name ?? "";
            pending = new(element, spell, line.Time, previous);
        }
        if (line.CastFailed && pending != null && (line.FailedSpell ?? lastCast).Equals(pending.Spell, StringComparison.OrdinalIgnoreCase) && line.Time >= pending.Time && line.Time <= pending.Time.AddSeconds(30))
        {
            foreach (var identity in profile.PetIdentities.Where(x => x.ElementObservedAt == pending.Time && x.ElementConfidence == "Correlated summon")) { identity.Element = "Unknown"; identity.ElementConfidence = "Unknown"; identity.ElementEvidence = "Summon attempt failed"; changed = true; }
            foreach (string key in candidates.Where(x => x.Value == pending).Select(x => x.Key).ToList()) candidates.Remove(key); pending = null;
        }
        if (line.PetLost) { pending = null; candidates.Clear(); signatures.Clear(); }
        if (line.SlainActor is { } dead) candidates.Remove(dead);
        string? actor = line.PetName ?? line.PetActivity ?? (line.Combat?.Owner != "Player" ? line.Combat?.Actor : null);
        if (actor is { Length: > 0 } && actor != "Unattributed")
        {
            if (pending != null && line.Time >= pending.Time.AddSeconds(3) && line.Time <= pending.Time.AddSeconds(60) && !actor.Equals(pending.PreviousPet, StringComparison.OrdinalIgnoreCase)) candidates[actor] = pending;
            var identities = profile.PetIdentities.Where(x => x.Name.Equals(actor, StringComparison.OrdinalIgnoreCase) && x.From <= line.Time.AddMinutes(10) && (!x.Until.HasValue || line.Time < x.Until)).ToList();
            if (identities.Count > 0 && candidates.TryGetValue(actor, out var summon))
                foreach (var identity in identities.Where(x => x.From >= summon.Time.AddSeconds(-2) && x.From <= summon.Time.AddMinutes(10) && (!x.Until.HasValue || x.Until > summon.Time))) changed |= Assign(identity, summon.Element, "Correlated summon", $"{summon.Spell} at {summon.Time:MMM dd HH:mm:ss}; subsequent {actor} activity and ownership", summon.Time);
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
    private static bool Assign(PetIdentity identity, string element, string confidence, string evidence, DateTime time)
    {
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
