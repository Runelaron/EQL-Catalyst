namespace EqlTracker.Core;

public static class CombatSnapshots
{
    public static Encounter Copy(Encounter fight) => new()
    {
        Id = fight.Id, Start = fight.Start, End = fight.End, Zone = fight.Zone, Loadout = fight.Loadout,
        PetType = fight.PetType, PetName = fight.PetName, PetElementEvidence = fight.PetElementEvidence,
        Weapons = fight.Weapons, BuffNotes = fight.BuffNotes, Finished = fight.Finished, DetailAvailable = fight.DetailAvailable,
        IdleResumable = fight.IdleResumable, ResumeCount = fight.ResumeCount, EndReason = fight.EndReason,
        ResolvedTargets = new(fight.ResolvedTargets, StringComparer.OrdinalIgnoreCase),
        Heals = fight.Heals.Select(e => e.Copy()).ToList(), HealingSummaries = fight.HealingSummaries.ToList(),
        HealingCoverage = fight.HealingCoverage, HealingDetailAvailable = fight.HealingDetailAvailable,
        PlayerDamage = fight.PlayerDamage, PetDamage = fight.PetDamage, UnknownDamage = fight.UnknownDamage, Peaks = fight.Peaks.ToList(),
        Events = fight.Events.Select(e => new CombatEvent { Id = e.Id, Time = e.Time, Actor = e.Actor, Owner = e.Owner,
            Target = e.Target, Kind = e.Kind, Ability = e.Ability, DamageType = e.DamageType, Damage = e.Damage,
            Critical = e.Critical, Outcome = e.Outcome, Zone = e.Zone, ActivityOnly = e.ActivityOnly, Relevance = e.Relevance }).ToList()
    };
}
