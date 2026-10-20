namespace EqlTracker.Core;

public sealed record GearBuildSelection(string Id, string Name, bool IsTheorycraft = false)
{
    public override string ToString() => Name + (IsTheorycraft ? " · Theorycraft" : "");
}
public static class GearBuildEditing
{
    public static void Migrate(LoadoutProfile owner)
    {
        if (owner.GearBuildVersion >= 1) return;
        if (owner.EquipmentDraft is { } legacy && !owner.GearBuilds.Any(b => b.Id == legacy.Id))
        {
            var copy = EquipmentPositions.Copy(legacy); copy.Name = legacy.Name == "Equipment draft" ? "Planned build" : legacy.Name;
            owner.GearBuilds.Add(copy);
        }
        owner.GearBuildVersion = 1;
    }
    public static GearDraft Create(LoadoutProfile owner, string name, bool empty = false, GearDraft? source = null)
    {
        var positions = empty ? EquipmentPositions.FromEntries([]) : EquipmentPositions.Copy(source?.Positions ?? EquipmentPositions.For(owner, false));
        if (empty) foreach (var p in positions) p.Knowledge = EquipmentKnowledge.Empty;
        var result = new GearDraft { Name = name.Trim(), Positions = positions, IsTheorycraft = source?.IsTheorycraft == true, Assumptions = source?.Assumptions.ToList() ?? [] };
        owner.GearBuilds.Add(result); return result;
    }
    public static void Remember(LoadoutProfile owner, GearDraft? build)
    {
        var undo = build?.Undo ?? owner.CurrentGearUndo;
        undo.Add(EquipmentPositions.Copy(build?.Positions ?? EquipmentPositions.For(owner, false)));
        if (build != null) { build.EditHistory.Add(new() { Name = build.Name, Assumptions = build.Assumptions.ToList() }); if (build.EditHistory.Count > 30) build.EditHistory.RemoveAt(0); }
        if (undo.Count > 30) undo.RemoveAt(0);
    }
    public static void SetPositions(LoadoutProfile owner, GearDraft? build, List<EquipmentPosition> positions)
    {
        if (build != null) { build.Positions = EquipmentPositions.Copy(positions); return; }
        owner.EquipmentPositions = EquipmentPositions.Copy(positions);
        owner.Equipment = owner.EquipmentPositions.Where(p => p.Item != null).Select(p => p.Item!).ToList();
    }
    public static bool Undo(LoadoutProfile owner, GearDraft? build)
    {
        var undo = build?.Undo ?? owner.CurrentGearUndo; if (undo.Count == 0) return false;
        var previous = undo[^1]; undo.RemoveAt(undo.Count - 1);
        if (build is { EditHistory.Count: > 0 }) { var edit = build.EditHistory[^1]; build.EditHistory.RemoveAt(build.EditHistory.Count-1); build.Name = edit.Name; build.Assumptions = edit.Assumptions.ToList(); }
        SetPositions(owner, build, previous); return true;
    }
    public static bool UseCurrent(LoadoutProfile owner, GearDraft build, out string reason)
    {
        if (build.IsTheorycraft) { reason = "Theorycraft builds stay separate from Current."; return false; }
        Remember(owner, null); SetPositions(owner, null, build.Positions); owner.SelectedGearBuildId = "current"; reason = "Current build updated · Undo available."; return true;
    }
    public static bool ApplyExport(CharacterProfile profile, InventorySnapshot inventory)
    {
        if (!profile.UpdateCurrentBuildFromInventory || !inventory.Complete) return false;
        var owner = profile.ActiveLoadout;
        if (EquipmentPositions.Fingerprint(EquipmentPositions.For(owner, false)) == EquipmentPositions.Fingerprint(inventory.EquipmentPositions)) return false;
        Remember(owner, null); SetPositions(owner, null, inventory.EquipmentPositions); return true;
    }
}
/// <summary>Never added to profile collections until the explicit Save command.</summary>
public sealed class GearTheorycraftWorkspace
{
    public GearDraft Working { get; }
    public string SavedId { get; private set; }
    private string saved;
    public bool Dirty => saved != Signature();
    private string Signature() => System.Text.Json.JsonSerializer.Serialize(new { Working.Name, Working.Positions, Working.Assumptions });
    public GearTheorycraftWorkspace(GearDraft source, bool existing = false)
    {
        Working = EquipmentPositions.Copy(source); Working.IsTheorycraft = true;
        SavedId = existing ? source.Id : ""; if (!existing) { Working.Id = Guid.NewGuid().ToString("N"); Working.Name = source.Name + " · Theorycraft"; }
        saved = existing ? Signature() : "";
    }
    public GearDraft Save(string name)
    {
        Working.Name = name.Trim(); SavedId = Working.Id; saved = Signature(); return EquipmentPositions.Copy(Working);
    }
}
