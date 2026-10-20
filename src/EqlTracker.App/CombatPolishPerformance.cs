using System.Diagnostics;
using System.Windows;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class CombatPolishPerformance
{
    internal static async Task Run(TrackerController controller, DpsWindow lab, string folder)
    {
        var reports = new List<object>();
        foreach (var mode in new[] { CombatLabMode.Damage, CombatLabMode.Healing })
        {
            lab.ShowView(mode, CombatLabTab.Group);
            for (int i = 0; i < 100; i++) { await Task.Delay(50); var ready = controller.InspectionView(mode == CombatLabMode.Healing, true); if (ready?.Key.Contains(":" + (mode == CombatLabMode.Healing) + ":True:", StringComparison.Ordinal) == true) break; }
            var readings = new List<double>(); var outliers = new List<object>(); long allocations = GC.GetTotalAllocatedBytes();
            for (int i = 0; i < 56; i++)
            {
                var watch = Stopwatch.StartNew(); lab.Refresh(); lab.UpdateLayout(); double ms = watch.Elapsed.TotalMilliseconds;
                if (i >= 8) readings.Add(ms); if (ms > 16) outliers.Add(new { Warmup = i < 8, Milliseconds = ms }); await Task.Delay(125);
            }
            var values = readings.Order().ToArray();
            reports.Add(new { Mode = mode.ToString(), Samples = values.Length, P99 = values[(int)((values.Length - 1) * .99)], Maximum = values.Last(), AllocatedMiB = (GC.GetTotalAllocatedBytes() - allocations) / 1048576d, AllOutliers = outliers });
        }
        Json.Write(Path.Combine(folder, "group-performance.json"), new { Modes = reports, Diagnostics = UpdateDiagnostics.Drain(), LiveAcceptance = "Offscreen fixture timing only; no game input" });
    }
}
