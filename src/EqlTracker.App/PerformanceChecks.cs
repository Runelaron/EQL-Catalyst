using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

// Run against a copied character DB: no game input, network refresh or live data writes.
public static class PerformanceChecks
{
    public static async Task Run(TrackerController controller, string folder)
    {
        await controller.PrepareIntegratedValidation();
        await PipelineChecks.Run(folder);
        await controller.ValidateHealingImportAsync(folder);
        await controller.ValidateStatEvidenceAsync(folder);
        await controller.ValidateAbilityOutcomesAsync(folder);
        await controller.ValidateSessionImportAsync(folder);
        await controller.DrainPersistenceAsync();
        controller.StartValidationViews();
        for (int i = 0; i < 200 && controller.Published == null; i++) await Task.Delay(50);
        if (controller.Published == null) throw new InvalidOperationException("View worker did not publish a snapshot.");
        var quest = new QuestWindow(controller); var combat = new DpsWindow(controller);
        var meter = new OverlayWindow(controller, true); var objectives = new OverlayWindow(controller, false);
        var windows = new FrostWindow[] { quest, combat, meter, objectives };
        foreach (var window in windows) { window.Left = -15000; window.Top = 0; window.Show(); }
        using var input = new WheelRouter();
        var warmupDiagnostics = UpdateDiagnostics.Drain();
        var slowUpdates = new List<object>(); var allSlowUpdates = new List<object>(); var dispatcherStalls = new List<double>(); var samples = new List<double>(); var healingSamples = new List<double>(); double worst = 0; int worstPhase = -1; var watch = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess(); double cpu = process.TotalProcessorTime.TotalSeconds;
        long allocations = GC.GetTotalAllocatedBytes(); int phase = -1, warm = 0, updates = 0;
        while (watch.Elapsed.TotalSeconds < 65)
        {
            int nextPhase = (int)(watch.Elapsed.TotalSeconds / 8);
            if (nextPhase != phase)
            {
                phase = nextPhase; warm = 0;
                if (phase % 3 == 0) { quest.ShowView(TrackerTab.Items); quest.SetZoneFilter(phase % 2 == 0 ? "Current zone" : "All zones"); }
                else if (phase % 3 == 1) quest.ShowView(TrackerTab.Gear, phase < 4 ? "Bloodmoon" : "");
                else quest.ShowView(TrackerTab.Progress, "Magician Epic Quest");
                if (phase >= 3) quest.ShowView(phase switch { 3 => TrackerTab.Queue, 4 => TrackerTab.Quests, 5 => TrackerTab.Pet, 6 => TrackerTab.Exaltations, _ => TrackerTab.Items });
                combat.ShowView(phase % 5); meter.SetMeterSize(phase % 2 == 0 ? 140 : 240, 320);
            }
            var elapsed = Stopwatch.StartNew();
            {
                using var updateMeasurement = UpdateDiagnostics.Measure("Benchmark application update");
                var stages = new Dictionary<string, double>(); var part = Stopwatch.StartNew(); int gen0 = GC.CollectionCount(0), gen2 = GC.CollectionCount(2);
                { quest.Refresh(); stages["Quest"] = part.Elapsed.TotalMilliseconds; part.Restart(); combat.Refresh(); stages["Combat"] = part.Elapsed.TotalMilliseconds; part.Restart(); meter.Refresh(); objectives.Refresh(); stages["Overlays"] = part.Elapsed.TotalMilliseconds; part.Restart(); foreach (var window in windows) window.UpdateLayout(); stages["Layout"] = part.Elapsed.TotalMilliseconds; }


                if (++warm > 8) { double ms = elapsed.Elapsed.TotalMilliseconds; samples.Add(ms); if (phase % 5 == 4) healingSamples.Add(ms); if (ms > worst) { worst = ms; worstPhase = phase; } }
                if (elapsed.Elapsed.TotalMilliseconds > 16) allSlowUpdates.Add(new { Phase = phase, Warmup = warm <= 8, Milliseconds = elapsed.Elapsed.TotalMilliseconds, Stages = stages, Gen0Collections = GC.CollectionCount(0) - gen0, Gen2Collections = GC.CollectionCount(2) - gen2 });
                if (warm > 8 && elapsed.Elapsed.TotalMilliseconds > 16) slowUpdates.Add(new { Phase = phase, Milliseconds = elapsed.Elapsed.TotalMilliseconds, Stages = stages, Gen0Collections = GC.CollectionCount(0) - gen0, Gen2Collections = GC.CollectionCount(2) - gen2 });
                updates++;
            }
            var delay = Stopwatch.StartNew(); await Task.Delay(125); double stall = Math.Max(0, delay.Elapsed.TotalMilliseconds - 125); UpdateDiagnostics.Record("Dispatcher wake delay", stall); if (stall > 16) dispatcherStalls.Add(stall);
        }
        var values = samples.Order().ToArray(); process.Refresh();
        Json.Write(Path.Combine(folder, "performance-report.json"), new
        {
            AppAssemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))).ToLowerInvariant(),
            CoreAssemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GearScorer).Assembly.Location))).ToLowerInvariant(),
            Mode = "Copied real character data; offscreen WPF panels; no game input", Seconds = watch.Elapsed.TotalSeconds,
            Encounters = controller.Combat.Encounters.Count, Events = controller.Combat.Encounters.Sum(f => f.Events.Count), CatalogItems = controller.Catalog.Items.Count, HealingEvents = controller.Combat.Healing.Events.Count, SnapshotMs = controller.LastSnapshotMilliseconds,
            Updates = updates, RoutineSamples = samples.Count, UiP99Ms = values[(int)((values.Length - 1) * .99)], UiMaxMs = values.Last(), WorstPhase = worstPhase, HealingUiP99Ms = healingSamples.Count == 0 ? 0 : healingSamples.Order().ElementAt((int)((healingSamples.Count - 1) * .99)), HealingUiMaxMs = healingSamples.DefaultIfEmpty(0).Max(),
            CpuPercentOfOneCore = 100 * (process.TotalProcessorTime.TotalSeconds - cpu) / watch.Elapsed.TotalSeconds,
            AllocatedMB = (GC.GetTotalAllocatedBytes() - allocations) / 1048576d, WorkingSetMB = process.WorkingSet64 / 1048576d,
            SuppressedHiddenGearBuilds = quest.SuppressedGearBuilds, SlowUpdates = slowUpdates, AllSlowUpdates = allSlowUpdates, WarmupDiagnostics = warmupDiagnostics, DispatcherStalls = dispatcherStalls, Diagnostics = UpdateDiagnostics.Drain(), Input = input.Diagnostics(), LiveAcceptance = "Pending next play session; no game input generated"
        });
        await CombatPolishPerformance.Run(controller, combat, folder);
        foreach (var window in windows) window.Close();
    }
}
