using System.Text.RegularExpressions;

namespace EqlTracker.Core;

public static class InventoryParser
{
    public static InventorySnapshot Parse(string text, DateTime capturedAt, string path = "")
    {
        var result = new InventorySnapshot { CapturedAt = capturedAt, FilePath = path };
        var lines = text.Replace("\r", "").Split('\n');
        if (lines.Length == 0 || !lines[0].TrimStart('\uFEFF').StartsWith("Location\tName\tID", StringComparison.Ordinal))
            throw new InvalidDataException("This is not an /outputfile inventory export.");
        bool keyring = false, sawStorage = false;
        var locations = new Dictionary<string, int>();
        var equipmentLocations = new Dictionary<string, int>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split('\t');
            if (fields.Length < 3) continue;
            if (fields[0] == "KeyRing") { keyring = true; sawStorage = true; continue; }
            EquipmentPosition? equipment = null;
            if (!keyring && EquipmentPositions.IsSlot(fields[0]) && fields.Length >= 5)
            {
                string slot = EquipmentPositions.Normalize(fields[0]); int index = equipmentLocations[slot] = equipmentLocations.GetValueOrDefault(slot) + 1;
                equipment = new() { Id = EquipmentPositions.Key(slot, index), Slot = slot, Index = index, Knowledge = fields[1] == "Empty" ? EquipmentKnowledge.Empty : EquipmentKnowledge.Occupied };
                result.EquipmentPositions.Add(equipment);
            }
            if (!int.TryParse(fields[2], out var id) || id == 0 || fields[1] == "Empty") continue;
            if (!keyring && fields.Length < 5) { result.Warning = "Incomplete physical inventory row; previous snapshot retained."; return result; }
            int? count = keyring ? null : int.TryParse(fields[3], out var n) ? n : null;
            if (!keyring && count is null) { result.Warning = "Invalid item quantity."; return result; }
            var entry = new InventoryEntry { Location = fields[0], Name = fields[1], GameId = id, Count = count };
            if (equipment != null) equipment.Item = entry;
            if (!keyring)
            {
                locations[entry.Location] = locations.GetValueOrDefault(entry.Location) + 1;
                bool paired = entry.Location is "Ear" or "Wrist" or "Finger" or "Fingers" or "Any Slot" || entry.IsAugment && Regex.IsMatch(entry.Location, @"^(Ear|Wrist|Fingers?|Any Slot)-Slot");
                if (locations[entry.Location] > (paired ? 2 : 1)) { result.Warning = "Duplicate physical location."; return result; }
            }
            if (entry.IsAugment && !keyring)
            {
                var parent = result.Entries.LastOrDefault(x => !x.IsAugment && !x.IsCollection && entry.Location.StartsWith(x.Location + "-Slot", StringComparison.OrdinalIgnoreCase));
                parent?.Augments.Add(entry);
            }
            result.Entries.Add(entry);
        }
        result.Complete = lines.Length > 2 && sawStorage;
        if (!result.Complete) result.Warning = "Storage section missing; keeping the previous complete export.";
        return result;
    }
    public static bool IsEquipped(InventoryEntry e) => !e.IsAugment && !e.IsCollection && EquipmentPositions.IsSlot(e.Location);

    public static void Apply(InventorySnapshot snapshot, ItemObservation item, string logId = "")
    {
        bool comparable = snapshot.ExportLogId.LastIndexOf(':') is var a && a > 0 && logId.StartsWith(snapshot.ExportLogId[..(a + 1)], StringComparison.Ordinal);
        if (comparable ? !LogPosition.After(logId, snapshot.ExportLogId) : item.Time <= snapshot.CapturedAt) return;
        snapshot.Invalidate();
        if (item.Action is "Looted" or "Created" or "Received" or "Destroyed" or "Merged" or "Turned in") snapshot.LastObservedChangeAt = snapshot.LastObservedChangeAt > item.Time ? snapshot.LastObservedChangeAt : item.Time;
        var canonical = Identity.Key(item.Name);
        var rows = snapshot.Entries.Where(e => !e.IsAugment && Identity.Key(e.Name) == canonical).ToList();
        if (item.Action == "Sold") return;
        if (item.Action == "Merged")
        {
            // A newly looted copy was consumed into an existing item, not added to stock.
            var existing = rows.OrderByDescending(x => x.Rank).FirstOrDefault();
            if (existing != null && item.Result.Length > 0) { existing.Name = item.Result; existing.Estimated = true; }
            return;
        }
        if (item.Action is "Destroyed" or "Turned in")
        {
            var remaining = item.Count;
            foreach (var e in rows.Where(x => x.Count.HasValue).OrderBy(x => x.Rank))
            {
                int take = Math.Min(remaining, e.Count ?? 0); e.Count -= take; remaining -= take; e.Estimated = true;
                if (remaining == 0) break;
            }
            // Collection presence is not a reliable quantity; do not guess deletion.
            return;
        }
        if (item.Action is "Looted" or "Created" or "Received")
        {
            var e = rows.FirstOrDefault(x => x.Estimated && x.Location == "Observed loot" && x.Name == item.Name && x.AcquiredFrom == item.Source);
            if (e != null) e.Count += item.Count;
            else snapshot.Entries.Add(new InventoryEntry { Location = "Observed loot", Name = item.Name, Count = item.Count, Estimated = true, AcquiredFrom = item.Source });
        }
    }
}
