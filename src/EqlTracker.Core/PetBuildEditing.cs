namespace EqlTracker.Core;

public sealed record PetBuildSelection(string Id, string Name, PetInventoryDestination Destination) { public override string ToString() => Name; }
public sealed class PetBuildEdit
{
    public bool FollowDetectedSummon { get; set; } = true;
    public bool SummonRankKnown { get; set; } = true;
    public int SlotConfigurationVersion { get; set; }
    public bool DualWield { get; set; }
    public List<PetSlotConfiguration> SlotConfigurations { get; set; } = [];
    public PetInventoryItem? DormantSecondary { get; set; }
    public PetSlotConfiguration? DormantSecondaryConfiguration { get; set; }
    public PetComparisonScenario? ComparisonScenario { get; set; }
    public string Reason { get; set; } = "Inventory change";
    public string Preset { get; set; } = "";
    public string PetType { get; set; } = "Unknown";
    public int? PetLevel { get; set; }
    public PetLevelMode LevelMode { get; set; }
    public string LevelEvidence { get; set; } = "";
    public string SummonSpell { get; set; } = "";
    public string SummonClass { get; set; } = "";
    public int SummonRank { get; set; }
    public string Focus { get; set; } = "";
    public List<PetInventoryItem> Items { get; set; } = [];
    public List<GearStatProfile> StatProfiles { get; set; } = [];
}
public static class PetBuildEditing
{
    public static PetBuildEdit Capture(PetBuild build, string reason) => new() { FollowDetectedSummon=build.FollowDetectedSummon, SummonRankKnown=build.SummonRankKnown, SlotConfigurationVersion = build.SlotConfigurationVersion, DualWield = build.DualWield, SlotConfigurations = EquipmentPositions.Copy(build.SlotConfigurations), DormantSecondary = build.DormantSecondary == null ? null : EquipmentPositions.Copy(build.DormantSecondary), DormantSecondaryConfiguration = build.DormantSecondaryConfiguration == null ? null : EquipmentPositions.Copy(build.DormantSecondaryConfiguration), ComparisonScenario = build.ComparisonScenario == null ? null : EquipmentPositions.Copy(build.ComparisonScenario), Reason = reason, Preset = build.Preset, PetType = build.PetType, PetLevel = build.PetLevel, LevelMode = build.LevelMode, LevelEvidence = build.LevelEvidence, SummonSpell = build.SummonSpell, SummonClass = build.SummonClass, SummonRank = build.SummonRank, Focus = build.Focus, Items = EquipmentPositions.Copy(build.Items), StatProfiles = EquipmentPositions.Copy(build.StatProfiles) };
    public static void Remember(PetBuild build, string reason = "Inventory change")
    {
        // Import legacy inventory-only Undo once; new edits retain their context too.
        if (build.Undo.Count > 0) { foreach (var items in build.Undo) { var old = Capture(build, "Inventory change"); old.Items = items; build.EditHistory.Add(old); } build.Undo.Clear(); }
        build.EditHistory.Add(Capture(build, reason)); if (build.EditHistory.Count > 20) build.EditHistory.RemoveRange(0, build.EditHistory.Count - 20);
    }
    public static bool Undo(PetBuild build, out string reason)
    {
        reason = "";
        if (build.EditHistory.Count == 0) { if (build.Undo.Count == 0) return false; build.Items = build.Undo[^1]; build.Undo.RemoveAt(build.Undo.Count - 1); reason = "Inventory change"; return true; }
        var previous = build.EditHistory[^1]; build.EditHistory.RemoveAt(build.EditHistory.Count - 1);
        build.FollowDetectedSummon=previous.FollowDetectedSummon;build.SummonRankKnown=previous.SummonRankKnown;build.SlotConfigurationVersion=previous.SlotConfigurationVersion; build.DualWield=previous.DualWield; build.SlotConfigurations=previous.SlotConfigurations; build.DormantSecondary=previous.DormantSecondary; build.DormantSecondaryConfiguration=previous.DormantSecondaryConfiguration; build.ComparisonScenario=previous.ComparisonScenario; build.Preset = previous.Preset; build.PetType = previous.PetType; build.PetLevel = previous.PetLevel; build.LevelMode = previous.LevelMode; build.LevelEvidence = previous.LevelEvidence; build.SummonSpell = previous.SummonSpell; build.SummonClass = previous.SummonClass; build.SummonRank = previous.SummonRank; build.Focus = previous.Focus; build.Items = previous.Items; build.StatProfiles = previous.StatProfiles; reason = previous.Reason; return true;
    }
    public static bool Hypothetical(int? level, int rank) => level is > 50 || rank > 10;
    public static string? ContextError(int? level, int rank, bool current) => level is < 1 or > 1000 || rank is < 0 or > 1000 ? "Use a positive level and a non-negative spell rank (up to 1000 for a planned scenario)." : current && Hypothetical(level, rank) ? "Values above level 50 or spell rank 10 are hypothetical. Choose a planned build to use them." : null;
    public static bool ApplyContext(PetBuild build, string type, int? level, string spell, int rank, string focus, bool current, out string error, string summonClass = "", PetLevelMode? mode = null, string evidence = "")
    {
        error = (ContextError(level,rank,false) ?? (current && (level>50 || rank>10 && rank!=build.SummonRank) ? "New hypothetical levels/ranks require a planned build." : null)) ?? ""; if (error.Length > 0) return false;
        if (build.PetType == type && build.PetLevel == level && build.SummonSpell == spell && build.SummonRank == rank && build.Focus == focus && build.SummonClass == summonClass && (!mode.HasValue || build.LevelMode == mode) && (evidence.Length == 0 || build.LevelEvidence == evidence) && build.SummonRankKnown) return true;
        Remember(build, "Pet context"); build.SummonRankKnown=true; build.PetType = type; build.PetLevel = level; build.SummonSpell = spell; build.SummonRank = rank; build.Focus = focus; build.SummonClass = summonClass; if (mode.HasValue) build.LevelMode = mode.Value; if (evidence.Length > 0) build.LevelEvidence = evidence; return true;
    }
    public static bool Activate(LoadoutProfile owner, PetBuild plan, Catalog catalog, out string error)
    {
        if (plan.IsTheorycraft) { error = "Theorycraft builds cannot become Current build."; return false; }
        error = ContextError(plan.PetLevel, plan.SummonRank, true) ?? ""; if (error.Length > 0) return false;
        var previous = PetInventoryEditing.Confirmed(owner, catalog); if (previous.Preset.Length == 0) previous.Preset = owner.PetPreset; var copy = EquipmentPositions.Copy(plan);
        Remember(previous, "Use as current build");
        copy.Id = previous.Id; copy.Name = "Current build"; copy.Undo = []; copy.EditHistory = EquipmentPositions.Copy(previous.EditHistory);
        copy.FollowDetectedSummon=false;
        foreach (var entry in copy.Items) entry.EquipmentVerified = false;
        PetInventoryEditing.Confirm(owner, copy, DateTime.Now); return true;
    }
    public static int CompareRatings(double? a, bool aConditional, double? b, bool bConditional, bool descending)
    {
        bool missingA = aConditional || !a.HasValue, missingB = bConditional || !b.HasValue;
        if (missingA != missingB) return missingA ? 1 : -1;
        if (missingA) return 0;
        return descending ? b!.Value.CompareTo(a!.Value) : a!.Value.CompareTo(b!.Value);
    }
}
