using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    private string mechanicsInput = "";
    private void RefreshMechanicalSetup()
    {
        string key = standardPetBuffs.Revision + "|" + PetComparisonEngine.Key(integrated.Rules) + "|" + research.Revision + "|" + Profile.SelectedLoadout;
        if (key == mechanicsInput) return; mechanicsInput = key;
        var evidence = new LoadoutMechanics { Revision = key, Server = Profile.Server, Rules = EquipmentPositions.Copy(integrated.Rules) };
        foreach (var buff in standardPetBuffs.Buffs.Where(b => !b.Excluded && !StandardPetBuffs.Burst(b.Spell)))
        {
            // Historical tooltip inspections lack a full caster/context key. Retain them for
            // review; only scoped fits and exact paired setup captures enter this evaluation.
            var magnitude = StandardPetBuffs.Magnitude(buff.Spell, buff.EffectiveRank, null);
            foreach (var stat in magnitude.Values) evidence.PetUsualBuffs.Add(new() { Identity = new(buff.Spell, buff.Spell, buff.EffectiveRank), Caster = buff.Caster, Recipient = buff.Recipient, Family = stat.Key, Channel = stat.Key == "HastePercent" ? EffectChannel.OrdinaryHaste : EffectChannel.Attribute, Amount = stat.Value, Quality = magnitude.Estimated ? EvidenceQuality.Estimated : EvidenceQuality.Observed, Source = magnitude.Evidence });
            if (magnitude.Values.Count == 0) evidence.PetUsualBuffs.Add(new() { Identity = new(buff.Spell, buff.Spell, buff.EffectiveRank), Family = "Unresolved buff", Source = magnitude.Evidence });
        }
        foreach (var owner in Profile.Loadouts)
        {
            var own = EquipmentPositions.Copy(evidence);
            foreach (var build in owner.PetBuilds.Concat(owner.ConfirmedPetInventory == null ? [] : new[] { owner.ConfirmedPetInventory }))
            {
                var contextOwner = GuidedPetBuilds.Context(owner, build);
                string fingerprint = EquipmentPositions.Fingerprint(GuidedPetBuilds.Positions(build, owner, Catalog));
                foreach (var sheet in (researchUnavailable?new PetResearchState():research).Sheets.Where(s => s.Reviewed && s.Context.Complete && s.Context.Pet == build.PetType && s.Context.Summon == build.SummonSpell && build.SummonRankKnown && s.Context.Rank == build.SummonRank && s.Context.Level == build.PetLevel && s.Context.Equipment == fingerprint && s.Context.Patch == PetDefinition.Version && (s.Context.FocusAndAA == build.Focus || build.Focus.Length == 0 && s.Context.FocusAndAA == "None")))
                {
                    bool unbuffed = sheet.Context.Buffs.StartsWith("Unbuffed", StringComparison.OrdinalIgnoreCase) || sheet.Context.Buffs == "No buffs";
                    var selected = standardPetBuffs.Buffs.Where(b => !b.Excluded && !StandardPetBuffs.Burst(b.Spell)).ToArray();
                    bool paired = selected.Length == 1 && selected[0].Spell is "Burnout III" or "Verses of Victory" && standardPetBuffs.Pairs.Any(p => p.Buffed == sheet.Id && p.Spell == selected[0].Spell && p.Rank == selected[0].EffectiveRank);
                    if (!unbuffed && !paired) continue;
                    var context = new EvaluationContext { Character = Profile.Character, Recipient = "Pet", PetType = build.PetType, PetLevel = sheet.Context.Level, SummonedPet = PetContextCatalog.Bundled.Find(build.PetType,build.SummonSpell)!=null, CharmedPet = build.PetType.Contains("Charm", StringComparison.OrdinalIgnoreCase), Scenario = unbuffed ? "Unbuffed" : "Usual buffs", Fingerprint = fingerprint, Patch = sheet.Context.Patch, ObservedOrdinaryAttackSpeed = sheet.Values.GetValueOrDefault("AttackSpeedPercent"), SpeedEvidence = EvidenceQuality.Observed, SpeedSource = "Reviewed exact equipment capture · " + sheet.Label };
                    var reviewedCap=standardPetBuffs.Pairs.FirstOrDefault(p=>(p.Baseline==sheet.Id||p.Buffed==sheet.Id)&&p.SpeedCeiling==171);
                    if(reviewedCap!=null){context.ReviewedOrdinaryCeiling=reviewedCap.SpeedCeiling;context.CeilingFingerprint=fingerprint;}
                    own.Anchors.Add(new(true, LoadoutEvaluationSession.PetSignature(contextOwner), fingerprint, context));
                }
            }
            owner.Mechanics = own;
        }
        equipmentRevision++; petRevision++; statProfileRevision++;
    }
}
