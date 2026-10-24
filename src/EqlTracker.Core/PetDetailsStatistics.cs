namespace EqlTracker.Core;

public sealed record PetDetailsStatistics(EffectivePetEquipment Equipment,IReadOnlyDictionary<string,double?> Observed,string Capture,bool MatchingEquipment,IReadOnlyList<string> Related)
{
    internal string CaptureName { get; init; } = "";
    internal DateTimeOffset? CapturedAt { get; init; }
    internal string Conditions { get; init; } = "";
    internal string CaptureId { get; init; } = "";
    internal bool Unbuffed { get; init; }
    internal IReadOnlyDictionary<string,StatLimit> Limits {get;init;}=new Dictionary<string,StatLimit>();
    internal IReadOnlyDictionary<string,double?> Uncapped {get;init;}=new Dictionary<string,double?>();
    internal int? ObservedLevel { get; init; }
    internal string Effects { get; init; } = "";
    internal string WeaponEvidence { get; init; } = "";
    internal PetSheetLoadout Loadout { get; init; } = PetSheetLoadout.Empty;
    public static PetDetailsStatistics Project(PetBuild build,LoadoutProfile owner,Catalog catalog,PetResearchState? research=null)
    {
        var effective=PetEquipmentInference.Build(PetEquipmentProjection.Prepare(build,owner,catalog),owner,catalog);
        var loadout=PetSheetLoadout.Build(build,owner,catalog,effective);
        string fingerprint=EquipmentPositions.Fingerprint(effective.Positions);
        var context=new LoadoutProfile{PetType=build.PetType,PetLevel=build.PetLevel,PetSummonSpell=build.SummonSpell,PetSummonRank=build.SummonRank,PetFocus=build.Focus};
        var profiles=build.StatProfiles.Concat(owner.PetStatProfiles).Where(p=>p.ConfirmedAt>=OctoberContent.PatchDate&&p.PetSignature==LoadoutEvaluationSession.PetSignature(context)&&p.Classes.Order().SequenceEqual(owner.Classes.Order())).OrderByDescending(p=>p.ConfirmedAt).ToList();
        var exact=profiles.Where(p=>p.EquipmentFingerprint==fingerprint).OrderBy(p=>!IsUnbuffed(p.Buffs)).FirstOrDefault();
        if(exact!=null&&build.SummonRankKnown)
            return new(effective,exact.Stats.Where(p=>double.IsFinite(p.Value.Value)&&(!(p.Key is "HP" or "Mana" or "Endurance" or "HPCurrent" or "ManaCurrent" or "EnduranceCurrent")||p.Value.Value>=0)).ToDictionary(p=>p.Key,p=>(double?)p.Value.Value),$"Observed capture: {exact.Name} · {exact.ConfirmedAt:MMM dd HH:mm} · {exact.Buffs} · {exact.Source}. Buffs/AA are capture conditions, not a detected current roster.",true,[])
            { Loadout=loadout,CaptureName=exact.Name,CapturedAt=new DateTimeOffset(exact.ConfirmedAt!.Value),Conditions=exact.Buffs,Unbuffed=IsUnbuffed(exact.Buffs),ObservedLevel=exact.Level>0?exact.Level:null,Limits=exact.Limits.Where(l=>l.Confirmed).GroupBy(l=>l.Stat).ToDictionary(g=>g.Key,g=>g.First()),Uncapped=exact.Stats.ToDictionary(p=>p.Key,p=>p.Value.UncappedTotalKnown?p.Value.Value+(p.Value.Overflow??0):p.Value.Overflow.HasValue?(double?)(p.Value.Value+p.Value.Overflow.Value):null) };
        var candidates=(research?.Sheets??[]).Where(s=>s.Reviewed&&s.Context.Pet==build.PetType&&s.Context.Patch==PetDefinition.Version).OrderByDescending(s=>s.CapturedAt).ToList();
        bool SameSpell(PetStatSheet s)=>s.Context.Summon.Equals(build.SummonSpell,StringComparison.OrdinalIgnoreCase);
        bool FocusCompatible(PetStatSheet s)=>!s.Context.Complete||s.Context.FocusAndAA==build.Focus||build.Focus.Length==0&&s.Context.FocusAndAA=="None";
        var matching=candidates.Where(s=>SameSpell(s)&&FocusCompatible(s)&&build.SummonRankKnown&&s.Context.Rank==build.SummonRank&&build.PetLevel.HasValue&&s.Context.Level==build.PetLevel).OrderBy(s=>!IsUnbuffed(s.Context.Buffs)).ToList();
        // Legacy narrative equipment descriptions remain related evidence, never exact fingerprints.
        var sheet=matching.FirstOrDefault(s=>s.Context.Equipment==fingerprint)??matching.FirstOrDefault(s=>!IsFingerprint(s.Context.Equipment));
        var related=candidates.Where(s=>s!=sheet).Select(s=>$"{s.Label} · rank {s.Context.Rank?.ToString()??"Unknown"} · actual level {s.Context.Level?.ToString()??"Unknown"} · {s.CapturedAt:MMM dd HH:mm} · {s.Context.Buffs}").Concat(profiles.Where(p=>p!=exact).Select(p=>p.Name+" · equipment differs from this build")).ToArray();
        if(sheet==null)return new(effective,new Dictionary<string,double?>(),"No applicable observed capture. Unknown totals remain unknown.",false,related){Loadout=loadout};
        var observed=sheet.Values.ToDictionary(p=>p.Key,p=>p.Value);
        foreach(string key in observed.Keys.ToArray())if(observed[key] is {} value&&!double.IsFinite(value))observed[key]=null;
        foreach(string key in new[]{"HP","HPCurrent","Mana","ManaCurrent","Endurance","EnduranceCurrent"})if(observed.GetValueOrDefault(key)<0)observed[key]=null;
        bool matches=sheet.Context.Equipment==fingerprint;
        return new(effective,observed,(matches?"Observed equipment capture: ":"Related capture · equipment/buff context incomplete: ")+sheet.Label+$" · rank {sheet.Context.Rank} · actual level {sheet.Context.Level} · {sheet.CapturedAt:MMM dd HH:mm} · "+sheet.Context.Buffs+" · focus/AA: "+(sheet.Context.FocusAndAA.Length>0?sheet.Context.FocusAndAA:"Unknown")+". Snapshot values are not recalculated totals for this build.",matches,related)
        { Loadout=loadout,CaptureName=sheet.Label,CaptureId=sheet.Id,CapturedAt=sheet.CapturedAt==default?null:sheet.CapturedAt,Conditions=sheet.Context.Buffs,Unbuffed=IsUnbuffed(sheet.Context.Buffs),ObservedLevel=sheet.Context.Level,Effects=sheet.Effects,WeaponEvidence=sheet.Weapons };
    }
    private static bool IsUnbuffed(string value)=>value.StartsWith("Unbuffed",StringComparison.OrdinalIgnoreCase)||value.Equals("No buffs",StringComparison.OrdinalIgnoreCase);
    private static bool IsFingerprint(string value)=>value.Length==64&&value.All(Uri.IsHexDigit);
}
