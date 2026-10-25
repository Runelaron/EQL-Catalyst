namespace EqlTracker.Core;

public sealed class LoadoutMechanics
{
    public string Revision { get; set; } = "";
    public string Server { get; set; } = "";
    public List<EffectRule> Rules { get; set; } = [];
    public List<EffectInput> PetUsualBuffs { get; set; } = [];
    public List<EffectInput> SelfUsualBuffs { get; set; } = [];
    public List<MechanicsAnchor> Anchors { get; set; } = [];
}
public sealed record MechanicsAnchor(bool Pet, string PetSignature, string EquipmentFingerprint, EvaluationContext Context);

public sealed partial class LoadoutEvaluationSession
{
    private readonly string mechanicsFingerprint;
    private readonly Dictionary<string,(EvaluationContext Context,EffectInput[] Inputs,EffectRule[] Rules,CompatibilityResult Result)> mechanicsBaselines=[];
    private readonly Dictionary<(string Position,Dictionary<string,double> Stats),EffectInput[]> mechanicsParts=[];
    private CompatibilityComparison? Mechanics(List<EquipmentPosition> after, string scenario)
    {
        var evidence = loadout.Mechanics; if (evidence == null) return null;
        string fingerprint = mechanicsFingerprint;
        string scenarioKey=evidence.Revision+"|"+scenario;
        if(mechanicsBaselines.TryGetValue(scenarioKey,out var cached))return BuildCompatibility.ComparePrepared(cached.Context,cached.Inputs,Inputs(after,scenario=="Usual buffs"?pet?evidence.PetUsualBuffs:evidence.SelfUsualBuffs:[]),cached.Rules,cached.Result);
        var anchor = evidence.Anchors.FirstOrDefault(a => a.Pet == pet && a.EquipmentFingerprint == fingerprint && a.Context.Scenario == scenario && (!pet || a.PetSignature == PetSignature(loadout)));
        var context = anchor == null ? new EvaluationContext { Scenario = scenario, PetLevel = pet ? loadout.PetLevel : null, SummonedPet = pet && PetContextCatalog.Bundled.Find(loadout.PetType,loadout.PetSummonSpell)!=null, PetType = pet ? loadout.PetType : "", CharmedPet = pet && loadout.PetType.Contains("Charm", StringComparison.OrdinalIgnoreCase), Classes = loadout.Classes } : EquipmentPositions.Copy(anchor.Context);
        context.Fingerprint = fingerprint;
        context.Server = evidence.Server;
        var buffs = scenario == "Usual buffs" ? pet ? evidence.PetUsualBuffs : evidence.SelfUsualBuffs : [];
        EffectInput[] Inputs(List<EquipmentPosition> positions,IEnumerable<EffectInput> selectedBuffs)
        {
            var result = new List<EffectInput>();
            foreach (var position in positions.Where(p => p.Item != null && p.Knowledge == EquipmentKnowledge.Occupied))
            {
                var entry = position.Item!; var definition = Definition(entry); var content = Item(entry, position.Slot);
                if(mechanicsParts.TryGetValue((position.Id,content.Stats),out var saved)){result.AddRange(saved);continue;}int start=result.Count;
                var ranked = definition == null ? null : RankedItemStats.Resolve(definition, entry.Rank);
                foreach (var stat in content.Stats)
                    result.Add(new() { Instance = position.Id, ItemRank = entry.Rank, Identity = new(definition?.Key ?? entry.Name, definition?.Name ?? entry.Name, null), Family = stat.Key, Channel = stat.Key == "Haste" ? EffectChannel.WornHaste : EffectChannel.Attribute, Amount = ranked?.UncertainStats.Contains(stat.Key) == true ? null : stat.Value, Equipment = true, Quality = ranked?.UncertainStats.Contains(stat.Key) == true ? EvidenceQuality.Unknown : EvidenceQuality.Estimated, Source = "Ranked equipment metadata · " + RankedItemStats.Version });
                foreach (string effect in content.Effects) result.Add(new() { Instance = position.Id, ItemRank = entry.Rank, Identity = new(effect, effect, null), Family = "Unresolved item effect", Quality = EvidenceQuality.Unknown, Source = "Selected effective item effect; an independent rule is required" });
                if (!content.Complete) result.Add(new() { Instance = position.Id, Identity = new(entry.Name, entry.Name, null), Family = "Incomplete item metadata", Quality = EvidenceQuality.Unknown });
                mechanicsParts[(position.Id,content.Stats)]=result.Skip(start).ToArray();
            }
            result.AddRange(selectedBuffs); return result.ToArray();
        }
        var inputs=Inputs(before,buffs);var rules=evidence.Rules.ToArray();var evaluated=BuildCompatibility.Evaluate(context,inputs,rules);mechanicsBaselines[scenarioKey]=(context,inputs,rules,evaluated);
        return BuildCompatibility.ComparePrepared(context,inputs,Inputs(after,buffs),rules,evaluated);
    }
    private static ContextRating ApplyMechanics(ContextRating rating, CompatibilityComparison? mechanical)
    {
        if (mechanical == null) return rating;
        var notes=rating.Notes.Concat(mechanical.Conclusion.Length>0?[mechanical.Conclusion]:Array.Empty<string>()).Append("Equipment preference points are separate from verified mechanical contributions and measured DPS.").Distinct().ToList();
        return new(){Name=rating.Name,CurrentScore=rating.CurrentScore,CandidateScore=rating.CandidateScore,MarginalEstimate=rating.MarginalEstimate,Changes=rating.Changes,Quality=rating.Quality,Notes=notes};
    }
}
