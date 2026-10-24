namespace EqlTracker.Core;

internal sealed record PetEffectLine(string Kind, string Text, bool Active);

/// <summary>Presentation of the same effective snapshot used by equipment calculations.</summary>
internal static class PetInventoryPresentation
{
    private static int Order(string kind, bool weapon = false) => weapon && kind == "Proc" ? -1 : kind switch { "Focus" => 0, "Click" => 1, "Worn" => 2, "Proc" => 3, _ => 4 };
    private static string EffectText(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(),@"^(?:Focus|Click|Worn|Proc|Ornamentation)(?:\s+Effect)?\s*:\s*","",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    internal static IReadOnlyList<PetEffectLine> Effects(PetBuild build, PetInventoryItem item, ItemDefinition definition,
        LoadoutProfile owner, Catalog catalog, EffectivePetEquipment equipment)
    {
        string position = equipment.Selections.FirstOrDefault(s => s.EntryId == item.Id)?.PositionId ?? "";
        string slot = equipment.Positions.FirstOrDefault(p => p.Id == position)?.Slot ?? item.Slot;
        var notes = new List<string>();
        var active = PetSlotPlanning.ActiveEffects(build, item, definition, slot, owner, catalog, notes).Select(e => e.EffectId).ToHashSet();
        var lines = new List<PetEffectLine>();
        var configured = build.SlotConfigurations.FirstOrDefault(s => s.Position == item.Position)?.Effects ?? item.Exaltations;
        var conflicts = PetEffectPolicy.Conflicts(configured);
        foreach (var effect in configured.Where(e => EffectText(e.Effect).Length>0))
        {
            string kind = PetEffectPolicy.Kind(effect);
            bool enabled = position.Length > 0 && active.Contains(effect.EffectId) && !conflicts.Contains(kind);
            string? reason = notes.FirstOrDefault(n => n.Contains(effect.SourceName + ":", StringComparison.Ordinal));
            string? observed = build.SlotConfigurations.FirstOrDefault(s => s.Position == item.Position)?.ObservedItemId;
            string state = !enabled ? "inactive" + (reason == null ? "" : " · " + reason) : effect.State == "Socketed" &&
                (observed == null || observed == item.Id) ? "recorded" : "planned";
            if (item.Rank < ExaltationPlanner.Unlock(kind)) state = "locked · requires +" + ExaltationPlanner.Unlock(kind);
            if (conflicts.Contains(kind)) state = "inactive · conflicting " + kind + " assignments; choose one in Edit selected";
            lines.Add(new(kind, kind + ": " + EffectText(effect.Effect) + (effect.SourceName.Length > 0 ? " · " + effect.SourceName : "") + " · " + state, enabled));
        }
        bool weapon = ItemEquipmentIcons.IsWeapon(definition) || definition.Stats.GetValueOrDefault("Damage") > 0 && definition.Stats.GetValueOrDefault("Delay") > 0;
        return lines.OrderBy(e => Order(e.Kind, weapon)).DistinctBy(e => e.Text).ToArray();
    }
    internal static string EmptyEffects(PetBuild build, int position)
    {
        var effects = build.SlotConfigurations.FirstOrDefault(s => s.Position == position)?.Effects ?? [];
        var conflicts = PetEffectPolicy.Conflicts(effects);
        return string.Join("\n", effects.Where(e => EffectText(e.Effect).Length>0).OrderBy(e => Order(PetEffectPolicy.Kind(e)))
            .Select(e => PetEffectPolicy.Kind(e) + ": " + EffectText(e.Effect) + " · inactive (empty position)" + (conflicts.Contains(PetEffectPolicy.Kind(e)) ? " · conflicting type; choose one in Edit selected" : "")));
    }

    internal static string ItemTooltip(PetBuild build, PetInventoryItem item, Catalog catalog, EffectivePetEquipment equipment)
    {
        if (item.ItemKey.Length == 0) return "Empty position " + item.Position;
        var lines = new List<string> { item.Name + " +" + item.Rank, "Recorded inventory position: " + item.Position };
        var selected = equipment.Selections.FirstOrDefault(s => s.EntryId == item.Id);
        var position = equipment.Positions.FirstOrDefault(p => p.Id == selected?.PositionId);
        lines.Add(position == null ? "Not active in effective equipment" : "Effective slot: " + position.Slot + " · " + selected!.Evidence);
        if ((catalog.FindKey(item.ItemKey) ?? catalog.Find(item.Name)) is {} definition)
        {
            var projection = RankedItemStats.Resolve(definition, item.Rank);
            if (projection.Stats.TryGetValue("Ratio", out double ratio)) lines.Add("Weapon ratio: " + ratio.ToString("0.###") +
                (projection.WeaponUncertain ? " · Conditional (ranked damage rule unresolved)" : "") + "; damage / delay. Not predicted pet DPS.");
            lines.Add(projection.Evidence);
        }
        return string.Join("\n", lines);
    }
}
