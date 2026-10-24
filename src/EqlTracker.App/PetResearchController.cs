using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
#if PRIVATE_PET_RESEARCH
    internal static bool PetResearchEnabled => true;
#else
    internal static bool PetResearchEnabled => false;
#endif
    private PetResearchState research = new();
    private long researchSavedRevision = -1;
    private bool researchUnavailable;
    private string researchSource = "";
    private string ResearchPath => Path.Combine(DataFolder, "pet-research.json");
    public long PetResearchRevision { get { lock (Sync) return research.Revision; } }
    public PetResearchState PetResearchSnapshot() { lock (Sync) return EquipmentPositions.Copy(research); }
    private void InitializePetResearch()
    {
        if (!PetResearchEnabled) return;
        try { if (File.Exists(ResearchPath)) research = Json.Read<PetResearchState>(ResearchPath); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { researchUnavailable = true; Status = "Pet research file unavailable; original retained for review"; return; }
        if(research.Version!=PetResearch.Version){researchUnavailable=true;Status="Research format needs review; original retained";return;}
        foreach (var sheet in research.Sheets) sheet.Validate();
        foreach (var buff in research.Buffs) PetResearch.NormalizeBuff(buff);
        ApplyReviewedPetCorrections();
        researchSavedRevision = research.Revision;
    }
    private void ApplyReviewedPetCorrections()
    {
        foreach (var correction in research.Corrections.Where(c => c.Evidence.Length > 0 && PetDefinition.Types.Contains(c.Context.Pet)))
        {
            foreach (var identity in Profile.PetIdentities.Where(p => p.From >= correction.From && p.From <= correction.Until && (correction.Actor.Length == 0 || p.Name == correction.Actor)))
            {
                bool metadata=correction.Context.Summon.Length>0&&(identity.CanonicalSummonSpell!=correction.Context.Summon||identity.SummonRank!=correction.Context.Rank);
                if (identity.Element == correction.Context.Pet && identity.ElementConfidence == "User reviewed"&&!metadata) continue;
                identity.Element = correction.Context.Pet; identity.ElementConfidence = "User reviewed"; identity.ElementEvidence = correction.Evidence; identity.ElementObservedAt = correction.From;
                if(correction.Context.Summon.Length>0){identity.CanonicalSummonSpell=correction.Context.Summon;identity.SummonRank=correction.Context.Rank;identity.RawSummonSpell=correction.Context.Summon+(correction.Context.Rank is {} rank?" +"+rank:"");identity.SummonEvidence=correction.Evidence;}
                inspectionIdentityRevision++; profileNeedsPersistence = saveRequested = true;
            }
            foreach (var fight in Combat.Encounters.Where(f => f.Start >= correction.From && f.End <= correction.Until && (correction.Actor.Length == 0 || f.PetName == correction.Actor)))
            {
                if (fight.PetType == correction.Context.Pet && fight.PetElementEvidence == correction.Evidence) continue;
                fight.PetType = correction.Context.Pet; fight.PetElementEvidence = correction.Evidence;
                if (!dirty.Contains(fight)) dirty.Add(fight); changedHistoricalSnapshots.Add(fight.Id); historicalSnapshotRevision++;
            }
        }
    }
    private void ObservePetResearch(ParsedLine line, string id)
    {
        if (!PetResearchEnabled) return;
        string? spell = line.PlayerCast ?? (line.CastFailed ? line.FailedSpell : null);
        if (spell == null || !PetResearch.BuffFamily(spell).Contains("Burnout")) return;
        var pet = PetIdentityResolver.At(Profile, line.Time);
        PetResearch.Observe(research, new() { Id = id + ":cast", Actor = pet?.Name ?? "", At = line.Time, Spell = spell, Transition = line.CastFailed ? "Interrupted" : "Cast", Evidence = "Cast attempt/interruption only; active buff not established or removed" });
    }
    private void ObserveResearchEffect(EffectEvidence effect)
    {
        if (!PetResearchEnabled) return;
        lock (Sync)
        {
            bool petOnly = effect.Spells.Count > 0 && effect.Spells.All(s => PetResearch.BuffFamily(s) is "Burnout" or "Frenzied Burnout");
            if (effect.Owner != "Pet" && !(petOnly && effect.State == "Fade")) return;
            string actor = effect.Owner == "Pet" ? effect.Actor : PetIdentityResolver.At(Profile,effect.At)?.Name ?? "";
            if (actor.Length == 0) return;
            string spell = PetResearch.ResolveBuffLanding(research,actor,effect.At,effect.Spells);
            double? duration = research.Buffs.LastOrDefault(b=>b.Transition=="Inspect" && b.Spell==spell)?.DurationSeconds;
            // Ambiguous messages are retained but never promoted to a known active buff.
            PetResearch.Observe(research, new() { Id = effect.LogId + ":effect", Actor = actor, At = effect.At, Spell = spell, SpellCandidates=effect.Spells.ToList(), DurationSeconds=duration, Transition = !spell.Contains("unresolved",StringComparison.OrdinalIgnoreCase) ? effect.State : "Unresolved", Evidence = effect.Evidence });
        }
    }
    private void RefreshPetResearch(IReadOnlyList<Encounter> history, string source)
    {
        if (!PetResearchEnabled || researchUnavailable) return;
        PetResearchState snapshot; CharacterProfile profile; long expected;
        lock (Sync)
        {
            string key = source + "|" + research.Revision;
            if (key == researchSource) return;
            expected = research.Revision; snapshot = EquipmentPositions.Copy(research);
            profile = new() { PetIdentities = Profile.PetIdentities.Select(EquipmentPositions.Copy).ToList() };
        }
        PetResearch.Collect(snapshot, history, profile);
        lock (Sync)
        {
            if (research.Revision != expected || cancel.IsCancellationRequested) return;
            research = snapshot;
            researchSource = source + "|" + research.Revision;
            SavePetResearch();
        }
    }
    private void SavePetResearch()
    {
        if (!PetResearchEnabled || researchUnavailable || researchSavedRevision == research.Revision) return;
        var copy = EquipmentPositions.Copy(research); researchSavedRevision = research.Revision;
        QueuePersistence(() => Json.Write(ResearchPath, copy));
    }
    internal void EditPetResearch(Action<PetResearchState> edit)
    {
        if (!PetResearchEnabled) throw new InvalidOperationException("Pet research is available only in an explicitly enabled local build.");
        if (researchUnavailable) throw new InvalidOperationException("Original research file needs review before saving. It has been retained unchanged.");
        lock (Sync)
        {
            // Validate a copy so failed imports cannot partially change the live research.
            var next = EquipmentPositions.Copy(research); edit(next);
            foreach (var sheet in next.Sheets) sheet.Validate();
            next.Revision = research.Revision + 1; research = next; researchSource = ""; ApplyReviewedPetCorrections(); SavePetResearch();
        }
    }
    internal void ImportPetResearch(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new ArgumentException("Measurement file exceeds 16 MB.");
        var imported = Json.Read<PetResearchState>(path);
        if (imported.Version != PetResearch.Version) throw new ArgumentException("Unsupported measurement format. Use the local research template.");
        EditPetResearch(next => {
            foreach (var sheet in imported.Sheets) { sheet.Reviewed = false; next.Sheets.RemoveAll(s => s.Id == sheet.Id); next.Sheets.Add(sheet); }
            foreach (var buff in imported.Buffs) PetResearch.Observe(next, buff);
            foreach (var model in imported.Models) { model.LocallyApproved = false; model.ReviewEvidence = ""; next.Models.RemoveAll(m => m.Id == model.Id); next.Models.Add(model); }
            // Imported files cannot approve identity corrections or overwrite combat totals.
        });
    }
}
