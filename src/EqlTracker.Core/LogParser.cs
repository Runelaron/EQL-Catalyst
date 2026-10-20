using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public sealed partial class LogParser(string character)
{
    [GeneratedRegex(@"^(?<actor>.+?) healed (?<target>.+?)(?<hot> over time)? for (?<amount>[\d,]+)(?: \((?<raw>[\d,]+)\))? hit points?(?: by (?<spell>.+?))?\.(?: (?<flags>.*))?$")] private static partial Regex HealPattern();
    [GeneratedRegex(@"^\[(?<time>[^]]+)\] (?<message>.*)$")] private static partial Regex LinePattern();
    [GeneratedRegex(@"^(?<actor>.+?) (?:hit|hits) (?<target>.+?) for (?<amount>\d+) points of (?<type>.+?) damage by (?<spell>.+?)\.(?: (?<flags>.*))?$")] private static partial Regex SpellPattern();
    [GeneratedRegex(@"^(?<actor>.+?) (?<verb>slashes|pierces|crushes|strikes|hits|kicks|bashes|backstabs|cleaves|punches|shoots|bites|claws|mauls|frenzies|round kicks|flying kicks|tiger claws|eagle strikes|dragon punches|tail rakes|reaves|reave|slash|pierce|crush|strike|hit|kick|bash|backstab|cleave|punch|shoot|bite|claw|maul|frenzy|round kick|flying kick|tiger claw|eagle strike|dragon punch|tail rake) (?<target>.+?) for (?<amount>\d+) points of damage\.(?: (?<flags>.*))?$")] private static partial Regex MeleePattern();
    [GeneratedRegex(@"^(?<target>.+?) has taken (?<amount>\d+) damage from (?<actor>.+?) by (?<spell>.+?)\.(?: (?<flags>.*))?$")] private static partial Regex DotPattern();
    [GeneratedRegex(@"^(?<actor>.+?) (?:tries to|try to) (?<verb>flying kick|round kick|tiger claw|eagle strike|dragon punch|tail rake|reave|slash|pierce|crush|strike|hit|kick|bash|backstab|cleave|punch|shoot|bite|claw|maul|frenzy) (?<target>.+?), but (?<outcome>misses|miss|.*?dodges|.*?parries|.*?blocks|.*?ripostes)!?\.?$")] private static partial Regex MissPattern();
    [GeneratedRegex(@"^(?<name>.+?) (?:told you|tells you|says), '(?:Attacking .+? Master\.|My leader is .+?|Sorry, Master\.\.\..*)'$", RegexOptions.IgnoreCase)] private static partial Regex PetPattern();
    [GeneratedRegex(@"^(?:--)?You (?:have )?looted (?:(?<count>\d+) |an? |some )?(?<name>.+?) from (?<source>.+?)'s corpse(?<suffix>.*)$")] private static partial Regex LootPattern();
    [GeneratedRegex(@"^You successfully destroyed (?<count>\d+) (?<name>.+?)\.$")] private static partial Regex DestroyPattern();
    [GeneratedRegex(@"^You have fashioned the items together to create something new: (?<name>.+?)\.$")] private static partial Regex CraftPattern();
    [GeneratedRegex(@"^\[(?<level>\d+) (?<classes>[A-Z]+(?:/[A-Z]+){0,2})\] (?<name>\S+)\b")] private static partial Regex WhoPattern();

    public ParsedLine? Parse(string line, string id = "")
    {
        var match = LinePattern().Match(line);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value, ["ddd MMM dd HH:mm:ss yyyy", "ddd MMM d HH:mm:ss yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time)) return null;
        time = DateTime.SpecifyKind(time, DateTimeKind.Local);
        var message = match.Groups["message"].Value; var result = new ParsedLine { Time = time };
        result.AbilityOutcome = AbilityOutcomeParser.Parse(message, time, character, id);
        if (message.StartsWith("Outputfile Complete: ", StringComparison.OrdinalIgnoreCase)) result.InventoryExport = message[21..].Trim();
        result.LoggingEnabled = message.StartsWith("Logging to ", StringComparison.Ordinal) && message.Contains("*ON*", StringComparison.Ordinal);
        if (message.StartsWith("You begin casting ", StringComparison.Ordinal)) result.PlayerCast = message[18..].TrimEnd('.');
        result.CastFailed = message.StartsWith("Your ", StringComparison.Ordinal) && (message.Contains("spell is interrupted", StringComparison.Ordinal) || message.Contains("spell fizzles", StringComparison.Ordinal)) || message.StartsWith("Insufficient Mana", StringComparison.Ordinal) || message.Contains("already have a pet", StringComparison.OrdinalIgnoreCase) || message.StartsWith("You cannot summon", StringComparison.OrdinalIgnoreCase);
        var failedSpell = Regex.Match(message, @"^Your (.+?) spell (?:is interrupted|fizzles)");
        if (failedSpell.Success) result.FailedSpell = failedSpell.Groups[1].Value;
        if (message == "You have joined the group.") result.JoinedGroup = true;
        if (message is "Welcome to EverQuest Legends!" or "Welcome to EverQuest!") result.GroupRosterReset = true;
        if (message is "You have left the group." or "You are no longer in a group." or "Your group has been disbanded." or "You have been removed from the group." or "You have disbanded the group.") result.LeftGroup = true;
        var accepted = Regex.Match(message, @"^You notify (\S+) that you agree to join the group\.$");
        if (accepted.Success) result.AcceptedInviter = accepted.Groups[1].Value;
        var joined = Regex.Match(message, @"^(\S+) has joined the group\.$"); if (joined.Success) result.GroupJoined = joined.Groups[1].Value;
        var left = Regex.Match(message, @"^(\S+) has left the group\.$"); if (left.Success) result.GroupLeft = left.Groups[1].Value;
        var activity = Regex.Match(message, @"^(\S+) begins casting (.+)\.$"); if (activity.Success) { result.PetActivity = activity.Groups[1].Value; result.ActivitySpell = activity.Groups[2].Value; }
        if (message.StartsWith("You have entered ", StringComparison.Ordinal) && !message.StartsWith("You have entered an area where ", StringComparison.OrdinalIgnoreCase)) { result.Zone = Identity.Zone(message[17..].TrimEnd('.')); result.Boundary = true; return result; }
        if (message == "LOADING, PLEASE WAIT..." || message.StartsWith("You have been slain", StringComparison.Ordinal)) { result.Boundary = true; return result; }
        if (message.StartsWith("Your pet has been slain", StringComparison.Ordinal) || message.StartsWith("Your pet disperses", StringComparison.Ordinal)) { result.PetLost = true; return result; }
        var slain = Regex.Match(message, @"^(.+?) has been slain by ");
        if (slain.Success) { result.SlainActor = slain.Groups[1].Value; return result; }
        var who = WhoPattern().Match(message);
        if (who.Success && who.Groups["name"].Value.Equals(character, StringComparison.OrdinalIgnoreCase))
        { result.Classes = who.Groups["classes"].Value.Split('/').ToList(); result.Level = int.Parse(who.Groups["level"].Value); result.RosterObserved = true; return result; }
        if (message.StartsWith("You have gained a level! Welcome to level ", StringComparison.Ordinal) && int.TryParse(message.Split(' ').Last().TrimEnd('!'), out var level)) { result.Level = level; return result; }
        var pet = PetPattern().Match(message);
        if (pet.Success) { result.PetName = pet.Groups["name"].Value; return result; }
        var loot = LootPattern().Match(message);
        if (loot.Success)
        {
            var suffix = loot.Groups["suffix"].Value; var action = suffix.Contains("sold it for", StringComparison.Ordinal) ? "Sold" : suffix.Contains("to create", StringComparison.Ordinal) ? "Merged" : "Looted";
            var created = Regex.Replace(Regex.Match(suffix, @"to create (.+?)(?:\.|$)").Groups[1].Value, @"^an? ", "");
            result.Item = new(time, loot.Groups["name"].Value, action, int.TryParse(loot.Groups["count"].Value, out var n) ? n : 1, loot.Groups["source"].Value, created); return result;
        }
        var heal = HealPattern().Match(message);
        if (heal.Success)
        {
            if (!long.TryParse(heal.Groups["amount"].Value.Replace(",", ""), out long amount) || heal.Groups["raw"].Success && !long.TryParse(heal.Groups["raw"].Value.Replace(",", ""), out _))
            { result.ParseWarning = "Healing outside supported numeric range"; return result; }
            string actor = heal.Groups["actor"].Value;
            if (actor.Equals("You", StringComparison.OrdinalIgnoreCase)) actor = character;
            string target = heal.Groups["target"].Value;
            if (target.ToLowerInvariant() is "himself" or "herself" or "itself" or "themselves" or "yourself") target = actor;
            if (target.Equals("You", StringComparison.OrdinalIgnoreCase)) target = character;
            result.Healing = new() { Id = id, Time = time, Healer = actor, Recipient = target,
                Effective = amount, Raw = heal.Groups["raw"].Success ? long.Parse(heal.Groups["raw"].Value.Replace(",", "")) : null,
                Ability = heal.Groups["spell"].Success ? heal.Groups["spell"].Value : "Unknown heal",
                OverTime = heal.Groups["hot"].Success, Critical = heal.Groups["flags"].Value.Contains("Critical", StringComparison.OrdinalIgnoreCase) };
            result.Healing.Lifetap = HealingIdentity.IsLifetap(result.Healing.Ability);
            return result;
        }
        var destroyed = DestroyPattern().Match(message);
        if (destroyed.Success) { result.Item = new(time, destroyed.Groups["name"].Value, "Destroyed", int.Parse(destroyed.Groups["count"].Value), ""); return result; }
        var craft = CraftPattern().Match(message);
        if (craft.Success) { result.Item = new(time, craft.Groups["name"].Value, "Created", 1, "Crafting"); return result; }
        foreach (var (pattern, kind) in new[] { (SpellPattern(), "Spell"), (DotPattern(), "DoT"), (MeleePattern(), "Melee"), (MissPattern(), "Melee") })
        {
            var hit = pattern.Match(message); if (!hit.Success) continue;
            if (pattern != MissPattern() && !long.TryParse(hit.Groups["amount"].Value, out _)) { result.ParseWarning = "Damage outside supported numeric range"; return result; }
            var actor = hit.Groups["actor"].Value;
            result.Combat = new CombatEvent { Id = id, Time = time, Actor = actor.Equals("You", StringComparison.OrdinalIgnoreCase) ? character : actor, Owner = actor.Equals("You", StringComparison.OrdinalIgnoreCase) || actor.Equals(character, StringComparison.OrdinalIgnoreCase) ? "Player" : "Unknown", Target = hit.Groups["target"].Value, Kind = kind, Ability = kind == "Melee" ? Strike(hit.Groups["verb"].Value) : hit.Groups["spell"].Value, DamageType = hit.Groups["type"].Value, Damage = long.TryParse(hit.Groups["amount"].Value, out var amount) ? amount : 0, Critical = hit.Groups["flags"].Value.Contains("Critical", StringComparison.OrdinalIgnoreCase), Outcome = pattern == MissPattern() ? hit.Groups["outcome"].Value : "Hit" }; return result;
        }
        // Unattributed non-melee text is retained separately and never credited to the pet.
        var unknown = Regex.Match(message, @"^(?<target>.+?) (?:was|is) hit by non-melee for (?<damage>\d+) (?:points of )?damage\.");
        if (unknown.Success && !unknown.Groups["target"].Value.Equals("You", StringComparison.OrdinalIgnoreCase))
            result.Combat = new CombatEvent { Id = id, Time = time, Actor = "Unattributed", Target = unknown.Groups["target"].Value, Kind = "Unknown", Ability = "Unknown spell", Damage = long.Parse(unknown.Groups["damage"].Value) };
        return result;
    }
    private static string Strike(string verb) => verb switch { "slash" => "slashes", "crush" => "crushes", "bash" => "bashes", "punch" => "punches", "dragon punch" => "dragon punches", "frenzy" => "frenzies", "reave" or "pierce" or "strike" or "hit" or "kick" or "backstab" or "cleave" or "shoot" or "bite" or "claw" or "maul" or "round kick" or "flying kick" or "tiger claw" or "eagle strike" or "tail rake" => verb + "s", _ => verb };
}
