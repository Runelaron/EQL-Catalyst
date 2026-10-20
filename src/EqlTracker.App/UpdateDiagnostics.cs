using System.Collections.Concurrent;
using System.Diagnostics;

namespace EqlTracker.App;

// Wall time intentionally includes scheduling/GC pauses. Never discard slow samples.
public static class UpdateDiagnostics
{
    public sealed record Sample(string Stage, double Milliseconds, long Bytes, int Gen0, int Gen2);
    private static readonly ConcurrentQueue<Sample> samples = new();
    public static IDisposable Measure(string stage) => new Scope(stage);
    public static void Record(string stage, double milliseconds) => samples.Enqueue(new(stage, milliseconds, 0, 0, 0));
    public static IDisposable Lock(object target) => new LockScope(target);
    private sealed class LockScope : IDisposable
    {
        private readonly object target;
        public LockScope(object target) { this.target = target; long at = Stopwatch.GetTimestamp(); Monitor.Enter(target); Record("Ingestion lock wait", Stopwatch.GetElapsedTime(at).TotalMilliseconds); }
        public void Dispose() => Monitor.Exit(target);
    }
    private sealed class Scope(string stage) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
        private readonly int gen0 = GC.CollectionCount(0), gen2 = GC.CollectionCount(2);
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; samples.Enqueue(new(stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes, GC.CollectionCount(0) - gen0, GC.CollectionCount(2) - gen2)); }
    }
    public static object Drain()
    {
        var result = new List<Sample>(); while (samples.TryDequeue(out var sample)) result.Add(sample);
        return new { Stages = result.GroupBy(s => s.Stage).Select(g => { var ordered = g.Select(s => s.Milliseconds).Order().ToArray(); return new { Stage = g.Key, Samples = ordered.Length, P99 = ordered[(int)((ordered.Length - 1) * .99)], Max = ordered[^1], AllocatedBytes = g.Sum(s => s.Bytes) }; }).ToArray(), Outliers = result.Where(s => s.Milliseconds > 16).ToArray() };
    }
}
