using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class BuildChangesPanel : Expander
{
    private string signature = "";
    public BuildChangesPanel() { Header = "Build Changes"; IsExpanded = true; MaxHeight = 170; ToolTip = "Local equipment preview, separate from acquisition Queue. Confirmed ownership stays unchanged. Remove reverses one replacement; Undo reverses the last edit."; }
    public sealed record Row(string Id, string Text, string Tooltip);
    public sealed record Snapshot(string Key, LoadoutComparison Total, IReadOnlyList<Row> Rows);
    public static Snapshot Prepare(List<EquipmentPosition> baseline, List<EquipmentPosition> planned, Catalog catalog, LoadoutEvaluationSession session)
    {
        var total = session.CompareBuild(planned); var rows = new List<Row>();
        string key = EquipmentPositions.Fingerprint(planned) + EquipmentPositions.Fingerprint(baseline) + System.Text.Json.JsonSerializer.Serialize(total);
        foreach (var change in planned.Where(p => Signature(p) != Signature(baseline.FirstOrDefault(b => b.Id == p.Id))))
        {
            var old = baseline.FirstOrDefault(p => p.Id == change.Id);
            var single = change.Item == null ? baseline.Select(p => p.Id == change.Id ? change : p).ToList() : EquipmentPositions.Replace(baseline, change.Id, change.Item, catalog);
            var evaluation = session.CompareBuild(single, change.Id);
            string text = change.Label + ": " + (old?.Item?.Name ?? "Empty") + " → " + (change.Item?.Name ?? "Empty") + " · " + (evaluation.Pet ? PetRoleRatings.Text(evaluation) : evaluation.Unbuffed.Delta?.ToString("+0.0;-0.0;0") ?? (evaluation.Status == "Conditional" ? "Conditional" : "—")) + " pts";
            string tooltip = "Current effects: " + string.Join("; ", old?.Item?.Augments.Select(a => a.EffectText) ?? []) + "\nPlanned effects: " + string.Join("; ", change.Item?.Augments.Select(a => a.EffectText) ?? []) + "\n" + string.Join('\n', evaluation.Unbuffed.Changes.Select(c => c.Explanation));
            rows.Add(new(change.Id, text, tooltip));
        }
        return new(key, total, rows);
    }
    public void Show(Snapshot snapshot, bool visible, Action undo, Action<string> remove)
    {
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed; if (!visible || signature == snapshot.Key) return; signature = snapshot.Key;
        var stack = new StackPanel(); var total = snapshot.Total;
        stack.Children.Add(UI.Text("Cumulative: " + (total.Pet ? PetRoleRatings.Text(total) : total.Unbuffed.Delta?.ToString("+0.0;-0.0;0") ?? total.Unbuffed.Quality) + " preference points" + (!total.Pet ? " · Buffed " + (total.Buffed.Delta?.ToString("+0.0;-0.0;0") ?? "—") : ""), 11, "Mint"));
        foreach (var row in snapshot.Rows)
        {
            var text = UI.Text(row.Text, 11); text.ToolTip = row.Tooltip;
            var removeButton = UI.Button("Remove", () => { signature = ""; remove(row.Id); }); removeButton.ToolTip = "Restore this position from confirmed equipment. No acquisition goal is removed."; stack.Children.Add(UI.Row(text, removeButton));
        }
        if (snapshot.Rows.Count == 0) stack.Children.Add(UI.Text("No equipment replacements yet. Add to build previews a replacement; Add to queue tracks obtaining it.", 11, "Muted"));
        stack.Children.Add(UI.Text("Each replacement is compared independently; cumulative rating accounts for shared slot and stacking changes.", 10, "Muted"));
        stack.Children.Add(UI.Button("Undo last change", () => { signature = ""; undo(); })); Content = UI.Scroll(stack);
    }
    private static string Signature(EquipmentPosition? p) => p?.Knowledge + "|" + (p?.Item == null ? "" : System.Text.Json.JsonSerializer.Serialize(p.Item));
}

public sealed partial class QuestWindow
{
    private void RemoveGearBuildChange(string id)
    {
        var loadout = Controller.Profile.ActiveLoadout; if (SelectedGearBuild is not { } draft) return;
        var baseline = EquipmentPositions.For(loadout, false); var original = baseline.FirstOrDefault(p => p.Id == id); if (original == null) return;
        draft.Undo.Add(EquipmentPositions.Copy(draft.Positions));
        draft.Positions = draft.Positions.Select(p => p.Id == id ? EquipmentPositions.Copy(original) : p).ToList();
        // Restoring either hand also restores the other if this was a two-handed swap.
        if (original.Slot is "Primary" or "Secondary") foreach (var hand in baseline.Where(p => p.Slot is "Primary" or "Secondary")) draft.Positions[draft.Positions.FindIndex(p => p.Id == hand.Id)] = EquipmentPositions.Copy(hand);
        GearEdited();
    }
    private void RemovePetBuildChange(string id)
    {
        if (CurrentBuild is not { } plan) return;
        var owner = Controller.Profile.ActiveLoadout; var confirmed = PetInventoryEditing.Confirmed(owner, Controller.Catalog);
        var current = PetEquipmentInference.Build(plan, owner, Controller.Catalog); var reference = PetEquipmentInference.Build(confirmed, owner, Controller.Catalog);
        var changed = current.Selections.FirstOrDefault(s => s.PositionId == id); var original = reference.Selections.FirstOrDefault(s => s.PositionId == id);
        PetBuildEditing.Remember(plan, "Remove planned change");
        int? position = plan.Items.FirstOrDefault(i => i.Id == changed?.EntryId)?.Position;
        plan.Items.RemoveAll(i => i.Id == changed?.EntryId);
        if (confirmed.Items.FirstOrDefault(i => i.Id == original?.EntryId) is { } source && !plan.Items.Any(i => i.Id == source.Id))
        { var item = EquipmentPositions.Copy(source); item.Position = position ?? PetInventoryEditing.NextPosition(plan); plan.Items.Add(item); }
        PetEdited();
    }
}
