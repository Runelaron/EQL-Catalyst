namespace EqlTracker.Core;

public sealed record SelectedItemPreviewState(string Identity, int? Rank, long Revision);

public static class PhysicalItemChoices
{
    public static string Identity(InventoryEntry? item) => item == null ? "catalog" : item.PhysicalId + "|" + item.Location + "|" + item.GameId + "|" + item.Name + "|" + string.Join(';', item.Augments.Select(a => a.EffectIdentity + a.EffectText));
    public static List<InventoryEntry> Ordered(IEnumerable<InventoryEntry> items) => items.OrderByDescending(i => i.Rank).ThenBy(i => i.Location, StringComparer.OrdinalIgnoreCase).ThenBy(Identity, StringComparer.Ordinal).ToList();
    public static IEnumerable<InventoryEntry?> Rows(IEnumerable<InventoryEntry> items, bool inventoryOnly)
    {
        var ordered = Ordered(items);
        return ordered.Count == 0 ? new InventoryEntry?[] { null } : inventoryOnly ? ordered.Cast<InventoryEntry?>() : [ordered[0]];
    }
}
