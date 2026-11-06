using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    internal void SeedIntegratedReview()
    {
        if (!Demo) throw new InvalidOperationException("Synthetic review only");
        var at = DateTime.Now.AddHours(-1); var session = new PlaySession { Id = "review-session", Start = at, LastObserved = DateTime.Now, Evidence = "Synthetic verified login marker" };
        SetPreviewSessions(new(1, [session], [new(session.Id, "Today · synthetic session")])); Profile.Meter.SessionId = session.Id;
        var item = Catalog.Find("Cloak of Flames") ?? Catalog.Items.First();
        Profile.GoalQueue.Add(new() { ItemKey = item.Key, Name = item.Name, Kind = "Item" });
        lock (Sync)
        {
            Journal(ActivityJournal.Receipt("review:1", new(at.AddMinutes(10), item.Name, "Looted", 1, "a named camp guardian"), "Solusek's Eye (Group)", Catalog, Profile));
            Journal(ActivityJournal.Receipt("review:2", new(at.AddMinutes(10), item.Name, "Sold", 1, "a named camp guardian"), "Solusek's Eye (Group)", Catalog, Profile));
            Journal(new() { Id = "review-plan", At = at.AddMinutes(20), Kind = "Planned setup", Name = "Gem 8: duplicate haste → missing resistance coverage", Source = "Saved in Catalyst only" });
            Journal(new() { Id = "review-progress", At = at.AddMinutes(25), Kind = "Progress", Name = "Player checked a completed quest step" });
            integrated.Bard = new() { ConfirmedAuraRank = 3, AuraEvidence = "Synthetic user confirmation", Songs = [new() { Gem = 8, Identity = new("fixture", "Verses of Victory", 4), Mana = 0, CooldownMs = 0, NonTargetedArea = true, Keep = true }] };
            otherHoldings = [new() { Character = "OtherCharacter", Server = Profile.Server, CapturedAt = at.AddDays(-3), Items = [new(item.Key, item.Name, 1, "Bank 1", 4, false)] }];
        }
    }
}
