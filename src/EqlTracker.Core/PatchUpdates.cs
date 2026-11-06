namespace EqlTracker.Core;

internal sealed record PatchFact(string Date, string Subject, string Change, string Source, bool IndependentlyVerified = true);
internal static class PatchUpdates
{
    internal const string Version = "EQL-2026-11-04/content-v18";
    internal const string October27 = "https://www.everquestlegends.com/patch-notes/eql-update-notes-10-27-2026";
    internal const string October29 = "https://www.everquestlegends.com/patch-notes/eql-update-notes-10-29-2026";
    internal const string November3 = "https://www.everquestlegends.com/patch-notes/eql-hotfix-notes-11-3-2026";
    internal const string November4 = "https://www.everquestlegends.com/patch-notes/eql-hotfix-notes-11-4-2026";
    internal static readonly PatchFact[] Facts = [
        new("2026-10-27", "Hate, Betrayal, and Trust", "The High Priestess blessing supports solo/multiplayer non-raid personal instances. Raid instances remain excluded.", October27),
        new("2026-10-27", "Host of Nektropos", "Kills improve Everling Dynasty faction; castle quest dialogue no longer rejects visitors for the corrected faction issue.", October27),
        new("2026-10-27", "Cryptguard", "The missing ornamentation socket was added.", October27),
        new("2026-10-27", "Low-level loadouts", "Hide, Sneak and Foraging retention was repaired when swapping to level-one loadouts.", October27),
        new("2026-10-29", "Castle quest items", "Quest checks can find required items in Equipment Storage. Dialogue/progression was repaired; no new universal quest-completion rule is inferred.", October29),
        new("2026-10-29", "Everling encounters", "Plimptos and Lady Sileniu respawn sooner, with no published duration. Alexa's encounter debuffs are removed on her death.", October29),
        new("2026-11-03", "Alternate abilities", "Activation requirements after deleveling were corrected. The note does not establish new ranks, ownership or numerical potency.", November3),
        new("2026-11-04", "Lay on Hands / Harm Touch", "The cooldown-without-activation defect was fixed. A cooldown or cast attempt still does not prove a successful effect in logs.", November4),
        new("2026-11-04", "Castle tower access", "Restarting House Everling through Ollix retains earned tower access. The repair is not retroactive; previously lost access must be regained once.", November4),
        new("2026-11-04", "Castle visibility", "Undead detect normal invisibility; living creatures detect invisibility versus undead. Shroud of Stealth generally works, with some rare/named exceptions to all invisibility.", November4)
    ];
    internal static void Apply(Catalog catalog)
    {
        foreach (var fact in Facts.DistinctBy(f => f.Source))
            if (!catalog.Documents.Any(d => d.Url == fact.Source)) catalog.Documents.Add(new() { Title = "Patch changes " + fact.Date, Url = fact.Source, Timestamp = fact.Date });
    }
}
