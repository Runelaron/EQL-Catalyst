using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class TrackerController
{
    public Task<string> EditHistoryAsync(string id, string action, DateTime? splitAt = null) => Task.Run(() =>
    {
        lock (Sync)
        {
            List<Encounter> before, after;
            if (action == "Undo")
            {
                var undo = historyUndo ?? throw new InvalidOperationException("There is no history edit to undo.");
                var restored = undo.Before.Select(CombatSnapshots.Copy).ToList(); QueuePersistence(() => store.ReplaceEncounters(undo.AfterIds, restored, null)); historyUndo = null; Combat.Healing.Replace(undo.AfterIds, undo.Before);
                Combat.Encounters.RemoveAll(f => undo.AfterIds.Contains(f.Id)); Combat.Encounters.AddRange(undo.Before);
                Profile.Meter.EncounterId = undo.Before.MaxBy(f => f.Start)?.Id ?? ""; historyRevision++; Save(); return "Undid " + undo.Description;
            }
            var selected = Combat.Encounters.FirstOrDefault(f => f.Id == id) ?? throw new InvalidOperationException("Select a finished encounter.");
            if (action == "Merge")
            {
                var previous = Combat.Encounters.Where(f => f.End <= selected.Start && f.Id != id).OrderByDescending(f => f.End).FirstOrDefault()
                    ?? throw new InvalidOperationException("There is no earlier finished encounter in the same zone.");
                before = [CombatSnapshots.Copy(previous), CombatSnapshots.Copy(selected)]; after = [EncounterEdits.Merge(previous, selected)];
            }
            else { before = [CombatSnapshots.Copy(selected)]; after = EncounterEdits.Split(selected, splitAt ?? throw new InvalidOperationException("Choose a split time.")); }
            var ids = before.Select(f => f.Id).ToList();
            var replacement = after.Select(CombatSnapshots.Copy).ToList(); var undoRecord = new EncounterEditUndo(before, after.Select(f => f.Id).ToList(), action.ToLowerInvariant()); historyUndo = undoRecord; QueuePersistence(() => store.ReplaceEncounters(ids, replacement, undoRecord));
            Combat.Healing.Replace(ids, after); dirty.RemoveAll(f => ids.Contains(f.Id)); Combat.Encounters.RemoveAll(f => ids.Contains(f.Id)); Combat.Encounters.AddRange(after);
            Profile.Meter.EncounterId = after[0].Id; historyRevision++; Save();
            return action == "Merge" ? "Merged encounters; elapsed gap included. Undo is available." : "Split encounter. Undo is available.";
        }
    });
}
