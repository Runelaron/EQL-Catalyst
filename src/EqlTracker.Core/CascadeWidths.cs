using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EQLTracker")]
[assembly: InternalsVisibleTo("EqlTracker.Tests")]
[assembly: InternalsVisibleTo("EQLUpdater")]

namespace EqlTracker.Core;

internal readonly record struct WidthTrack(double Width, double Minimum, double Maximum = double.PositiveInfinity);

/// <summary>Pure boundary transfer. Inputs are an immutable drag-start snapshot in displayed order.</summary>
internal static class CascadeWidths
{
    internal static double Resize(ReadOnlySpan<WidthTrack> tracks, int boundary, double displacement, Span<double> result)
    {
        if (result.Length != tracks.Length) throw new ArgumentException("Width buffer does not match tracks.");
        for (int i = 0; i < tracks.Length; i++) result[i] = tracks[i].Width;
        if (boundary < 0 || boundary >= tracks.Length - 1 || !double.IsFinite(displacement)) return 0;
        int recipient = displacement >= 0 ? boundary : boundary + 1;
        double remaining = Math.Min(Math.Abs(displacement), Math.Max(0, tracks[recipient].Maximum - tracks[recipient].Width));
        double transferred = 0;
        int step = displacement >= 0 ? 1 : -1;
        for (int i = displacement >= 0 ? boundary + 1 : boundary; i >= 0 && i < tracks.Length && remaining > 0; i += step)
        {
            double amount = Math.Min(remaining, Math.Max(0, tracks[i].Width - tracks[i].Minimum));
            result[i] -= amount; transferred += amount; remaining -= amount;
        }
        result[recipient] += transferred;
        return displacement >= 0 ? transferred : -transferred;
    }

    // Bounded weighted fit for window changes. Compact columns can be fixed by setting min=max.
    internal static void Fit(ReadOnlySpan<WidthTrack> tracks, double available, Span<double> result)
    {
        if (result.Length != tracks.Length) throw new ArgumentException("Width buffer does not match tracks.");
        double minimum = 0, maximum = 0;
        for (int i = 0; i < tracks.Length; i++) { minimum += tracks[i].Minimum; maximum += tracks[i].Maximum; }
        double target = Math.Clamp(double.IsFinite(available) ? available : minimum, minimum, maximum);
        if (target <= minimum) { for (int i = 0; i < tracks.Length; i++) result[i] = tracks[i].Minimum; return; }
        double low = 0, high = 1;
        for (int pass = 0; pass < 64; pass++)
        {
            double sum = 0;
            for (int i = 0; i < tracks.Length; i++) sum += Math.Clamp(Math.Max(.001, tracks[i].Width) * high, tracks[i].Minimum, tracks[i].Maximum);
            if (sum >= target - .000001) break;
            high *= 2;
        }
        for (int pass = 0; pass < 56; pass++)
        {
            double factor = (low + high) / 2, sum = 0;
            for (int i = 0; i < tracks.Length; i++) sum += Math.Clamp(Math.Max(.001, tracks[i].Width) * factor, tracks[i].Minimum, tracks[i].Maximum);
            if (sum < target) low = factor; else high = factor;
        }
        for (int i = 0; i < tracks.Length; i++) result[i] = Math.Clamp(Math.Max(.001, tracks[i].Width) * ((low + high) / 2), tracks[i].Minimum, tracks[i].Maximum);
    }
}
