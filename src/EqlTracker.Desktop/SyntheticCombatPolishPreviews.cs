using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    internal void SeedPolishOutcomes(Encounter fight)
    {
        if (!Demo) throw new InvalidOperationException("Diagnostic fixtures require demo mode.");
        for (int i = 0; i < 3; i++) abilityOutcomes["preview-resist-" + i] = new("preview-resist-" + i, fight.Start.AddSeconds(3 + i * 4), Profile.Character, "an ice giant", "Spirit Tap", "Full resist", "Illustrative verified-format fixture"); outcomeRevision++; outcomesCaughtUp = true;
    }
}
