using System.Diagnostics;
using System.Globalization;
using EqlTracker.Core;

namespace EqlTracker.App;

public static class PipelineChecks
{
    public static async Task Run(string folder)
    {
        string root = Path.Combine(folder, "pipeline-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "game"); Directory.CreateDirectory(Path.Combine(game, "Logs"));
        string log = Path.Combine(game, "Logs", "eqlog_PipelineTest_test.txt");
        string Line(string text, DateTime at) => $"[{at.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture)}] {text}\r\n";
        DateTime start = DateTime.Now;
        File.WriteAllText(log, Line("You slash an orc for 71 points of damage.", start.AddSeconds(-40)));
        using var controller = new TrackerController(Path.Combine(root, "profile"), false, "PipelineTest", "test", game);
        controller.Profile.Meter.Actor = "Player"; controller.Profile.Meter.Target = "YOU"; controller.Start(true);
        var checks = new List<string>(); var timings = new Dictionary<string, double>();
        async Task Wait(string name, Func<bool> predicate)
        {
            var timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < 8) await Task.Delay(25);
            if (!predicate()) throw new InvalidOperationException("Pipeline validation failed: " + name + " · " + controller.Status);
            checks.Add(name); timings[name] = timer.Elapsed.TotalMilliseconds;
        }
        await Wait("Single-hit fight is published without waiting two seconds", () => controller.Published?.Combat.Damage == 71);
        string first = controller.Published!.Combat.Fights.Single().Id;
        File.AppendAllText(log, Line("You slash another orc for 93 points of damage.", start));
        await Wait("Next short fight publishes a new encounter immediately", () => controller.Published?.Combat is { Damage: 93 } v && v.Fights.Single().Id != first);
        if (controller.Profile.Meter.Target.Length != 0) throw new InvalidOperationException("Obsolete YOU target was not cleared globally.");
        checks.Add("Obsolete Self-target filter is repaired without Combat Lab");
        long equipmentRevision = controller.EquipmentRevision; var hierarchy = controller.Published!.Hierarchies;
        string partial = Line("You slash another orc for 10 points of damage.", start.AddSeconds(1)).TrimEnd('\r', '\n');
        File.AppendAllText(log, partial); await Task.Delay(700);
        if (controller.Published!.Combat.Damage != 93) throw new InvalidOperationException("Partial line was consumed early.");
        File.AppendAllText(log, "\r\n"); await Wait("Partial attack line is retained until complete", () => controller.Published?.Combat.Damage == 103);
        using (var locked = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) await Task.Delay(1100);
        File.AppendAllText(log, Line("You slash another orc for 11 points of damage.", start.AddSeconds(2)));
        await Wait("Reader resumes automatically after an exclusive file lock", () => controller.Published?.Combat.Damage == 114);
        File.AppendAllText(log, Line("You slash another orc for 999999999999999999999999 points of damage.", start.AddSeconds(3)) + Line("You slash another orc for 12 points of damage.", start.AddSeconds(3)));
        await Wait("Malformed attack does not stop subsequent valid combat", () => controller.Published?.Combat.Damage == 126);
        if (controller.ReaderErrors < 1) throw new InvalidOperationException("Invalid line was not diagnosed.");
        if (controller.EquipmentRevision != equipmentRevision || !ReferenceEquals(hierarchy, controller.Published!.Hierarchies)) throw new InvalidOperationException("Combat invalidated equipment or quest hierarchy.");
        checks.Add("Combat-only events preserve equipment revisions and quest hierarchy instances");
        controller.BeginSync(); controller.CheckSync();
        if (controller.SyncRequest.State == "Complete") throw new InvalidOperationException("A sent request incorrectly counted as successful sync.");
        checks.Add("Sync request without fresh evidence is not success");
        string inventory = Path.Combine(game, "PipelineTest_test-Inventory.txt"); DateTime ack = DateTime.Now;
        File.WriteAllText(inventory, "Location\tName\tID\tCount\tSlots\nBank1\tDiamond\t10037\t1\t0\nKeyRing\tName\tID\n");
        File.AppendAllText(log, Line("Logging to 'eqlog.txt' is now *ON*.", ack) + Line("[50 WAR] PipelineTest (Human)", ack) + Line("Outputfile Complete: PipelineTest_test-Inventory.txt", ack) + Line("You looted a Diamond from an orc's corpse.", ack));
        await Wait("Fresh acknowledgement imports inventory plus same-second later loot", () => { lock (controller.Sync) { controller.CheckSync(); return controller.SyncRequest.State == "Complete" && controller.Inventory.Entries.Where(e => e.Name == "Diamond").Sum(e => e.Count) == 2; } });
        if (!controller.Profile.NativeSync.Verified || !controller.Profile.NativeSync.LastSuccess.HasValue) throw new InvalidOperationException("Verified sync state was not retained.");
        checks.Add("Sync verification requires logging, own classes, export acknowledgement and complete import");
        long total = controller.Combat.Encounters.Sum(f => f.Damage) + (controller.Combat.Current?.Damage ?? 0);
        controller.Dispose();
        using (var restart = new TrackerController(Path.Combine(root, "profile"), false, "PipelineTest", "test", game))
        {
            long restored = restart.Combat.Encounters.Sum(f => f.Damage) + (restart.Combat.Current?.Damage ?? 0);
            if (total != restored || restart.Inventory.Entries.Where(e => e.Name == "Diamond").Sum(e => e.Count) != 2) throw new InvalidOperationException("Queued persistence did not reconcile after restart.");
        }
        checks.Add("Ordered persistence drains before restart with unchanged damage and same-second inventory evidence");
        Json.Write(Path.Combine(folder, "pipeline-report.json"), new { Passed = checks.Count, Checks = checks, DetectionMilliseconds = timings, controller.ReaderErrors, controller.Status });
    }
}
