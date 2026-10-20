using System.Diagnostics;

namespace EqlTracker.App;

public sealed class DisplayCadence
{
    public const string DpsHelp = "Fight DPS = selected damage / first-to-last relevant combat time (minimum 1 second). Resumed interruptions count; the closing timeout does not. Session DPS uses the sum of selected fight durations. 5s is a separate rolling value. DPS readings refresh every 2 seconds.";
    private long next;
    private string context = "";
    private readonly Func<long> clock;
    public DisplayCadence(Func<long>? clock = null) => this.clock = clock ?? Stopwatch.GetTimestamp;
    public static string Context(EqlTracker.Core.DpsViewState view, string visualContext)
    {
        var latest = view.Fights.MaxBy(f => f.Start);
        return visualContext + "|" + latest?.Id + "|" + (latest?.PetDamage > 0) + "|" + (latest?.PlayerDamage > 0) + "|" +
            latest?.ResolvedTargets.Count + "|" + view.Targets.Count + "|" + view.InCombat;
    }
    public bool Due(string key)
    {
        long now = clock();
        if (key == context && now < next) return false;
        context = key; next = now + Stopwatch.Frequency * 2; return true;
    }
}
