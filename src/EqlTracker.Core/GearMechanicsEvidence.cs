namespace EqlTracker.Core;

public static class GearMechanicsEvidence
{
    public const string Version = "EQL-2026-10-13/preference-v2";
    public const string PetWeaponSource = "https://www.everquestlegends.com/patch-notes/eql-update-notes-10-13-2026";
    public const string AaSource = "https://eqlwiki.com/Alternate_Advancement";
    public const string StatsSource = "https://eqlwiki.com/Statistics";
    public static List<GearAaModifier> RelevantAAs(bool pet) => (pet
        ? new[] { "Companion's Fury", "Unbound Companion", "Unbound Ferocity", "Pet Affinity", "Companion's Discipline", "Hobble of Spirits" }
        : new[] { "Innate Eminence", "Combat Agility", "Combat Stability", "Physical Enhancement", "Natural Durability", "Combat Fury", "Ambidexterity" })
        .Select(name => new GearAaModifier { Name = name, Target = pet ? "Pet" : "Self", Source = AaSource, Kind = AaModifierKind.Information }).ToList();
}
