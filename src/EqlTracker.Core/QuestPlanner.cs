namespace EqlTracker.Core;

public sealed class ObjectiveRow
{
    public string Key { get; init; } = "";
    public string Item { get; init; } = "";
    public string Class { get; init; } = "";
    public string QuestId { get; init; } = "";
    public string Quest { get; init; } = "";
    public int Needed { get; init; }
    public int Owned { get; init; }
    public int TotalOwned { get; init; }
    public int Remaining => Math.Max(0, Needed - Owned);
    public string Quantity => $"{Owned} / {Needed}";
    public string Status => Remaining == 0 ? "Owned" : Availability == "Unavailable" ? "Unavailable" : "Needed";
    public string Zone { get; init; } = "";
    public string Mob { get; init; } = "";
    public string Availability { get; init; } = "";
    public string Location { get; init; } = "";
    public string Instructions { get; init; } = "";
    public string WikiUrl { get; init; } = "";
    public string Eligibility { get; init; } = "";
    public List<ItemSource> Sources { get; init; } = [];
    public string StepId { get; init; } = "";
    public string ParentReward { get; init; } = "";
    public string Group => Class + " / " + Quest + (ParentReward.Length > 0 ? " / " + ParentReward : " / Collection");
    public bool Estimated { get; init; }
    public string Evidence => Estimated ? "Log estimate" : Location.Contains("Equipment", StringComparison.Ordinal) ? "Collection presence; quantity unknown" : "Inventory snapshot";
}

public static class QuestPlanner
{
    public static List<ObjectiveRow> Plan(Catalog catalog, CharacterProfile profile, InventorySnapshot inventory)
    {
        var rows = new List<ObjectiveRow>();
        var knownZones = catalog.Zones.Select(Identity.ZoneKey).ToHashSet();
        var relevant = catalog.Quests.SelectMany(q => q.Requirements.Concat(q.Steps.SelectMany(s => s.Outputs))).Select(x => x.ItemKey).Concat(profile.Wishlist.Select(x => catalog.Find(x.Name)?.Key ?? "")).ToHashSet();
        var stock = catalog.Items.Where(i => relevant.Contains(i.Key)).ToDictionary(x => x.Key, inventory.Count);
        var ordered = catalog.Quests.Where(x => QuestTracking.Selected(x, profile) && ExpansionRules.Planned(x, profile)).OrderBy(x => Priority(x.Class, profile)).ThenBy(x => x.Class);
        foreach (var quest in ordered)
        {
            if (QuestTracking.Fulfilled(quest, profile, inventory, catalog)) continue;
            if (quest.RecipeVersion > 0)
            {
                foreach (var demand in QuestRecipes.Expand(quest, profile, catalog, stock, inventory))
                {
                    var item = catalog.FindKey(demand.Key); if (item == null) continue;
                    var available = item.Sources.Where(s => ExpansionRules.Available(s.Era, ExpansionRules.Current(profile))).ToList();
                    var step = demand.Parent;
                    string availability = available.Any(x => x.Availability == "Observed drop") ? "Observed drop" : available.Count == 0 ? "Future / source unknown" : available.All(x => x.Availability == "Unavailable") ? "Unavailable" : available.Any(x => x.Availability == "Unverified") ? "Unverified" : "Wiki documented";
                    rows.Add(new ObjectiveRow { Key = item.Key, Item = item.Name, Class = quest.Class, QuestId = quest.Id, Quest = quest.Name, Needed = demand.Needed, Owned = demand.Owned, TotalOwned = inventory.Count(item),
                        Zone = string.Join(", ", available.Where(s => knownZones.Contains(Identity.ZoneKey(s.Zone))).Select(s => s.Zone).DistinctBy(Identity.ZoneKey)), Mob = string.Join(", ", available.Select(s => s.Mob).Distinct()), Availability = availability,
                        Location = inventory.Locations(item), Sources = item.Sources, WikiUrl = item.WikiUrl, StepId = step?.Id ?? "", ParentReward = step == null ? "" : string.Join(" + ", step.Outputs.Select(o => catalog.FindKey(o.ItemKey)?.Name ?? o.ItemKey)),
                        Instructions = (step?.Instructions ?? "Collect this ingredient.") + (step?.RandomReward == true ? "\nIngredients per attempt; desired reward is random." : "") + (step?.Conditions.Count > 0 ? "\n" + string.Join("\n", step.Conditions) : ""),
                        Estimated = inventory.Entries.Any(e => e.Estimated && inventory.Matches(e, item)), Eligibility = profile.CurrentClasses.Contains(quest.Class) ? "Current class goal" : profile.Level50Classes.Contains(quest.Class) ? "Level 50 class goal" : "Selected quest" });
                }
                continue;
            }
            int copies = Math.Max(1, profile.QuestQuantities.GetValueOrDefault(quest.Id, 1));
            if (catalog.FindKey(quest.RewardKey) is { } ownedReward) copies = Math.Max(1, copies - inventory.Count(ownedReward));
            var completed = new HashSet<string>(profile.CompletedSteps);
            var activeRoute = profile.Routes.GetValueOrDefault(quest.Id, "Default");
            var steps = quest.Steps.Where(x => !x.Optional || x.Route == activeRoute).ToList();
            var inactiveItems = quest.Steps.Where(x => x.Optional && x.Route != activeRoute).SelectMany(x => x.Inputs).Select(x => x.ItemKey).Except(steps.SelectMany(x => x.Inputs).Select(x => x.ItemKey)).ToHashSet();
            var rewards = quest.RewardAlternatives.Count > 0 ? quest.RewardAlternatives : [[quest.RewardKey]];
            // Owned intermediate outputs prove their prerequisites have been satisfied.
            foreach (var step in steps.Where(s => s.Outputs.Count > 0 && s.Outputs.All(o => stock.GetValueOrDefault(o.ItemKey) >= o.Count * copies)))
                CompleteAncestors(step, steps, completed, []);
            foreach (var id in profile.CompletedSteps.ToArray())
                if (steps.FirstOrDefault(x => x.Id == id) is { } step) CompleteAncestors(step, steps, completed, []);
            var satisfiedInputs = new HashSet<string>(steps.Where(s => completed.Contains(s.Id)).SelectMany(x => x.Inputs).Select(x => x.ItemKey));
            var produced = steps.Where(s => !completed.Contains(s.Id) && s.Inputs.Count > 0).SelectMany(s => s.Outputs).Select(x => x.ItemKey).ToHashSet();
            foreach (var requirement in quest.Requirements)
            {
                var item = catalog.FindKey(requirement.ItemKey); if (item == null) continue;
                if (inactiveItems.Contains(item.Key)) continue;
                var consumers = steps.Where(s => s.Outputs.Count > 0 && s.Inputs.Any(i => i.ItemKey == item.Key)).ToList();
                int needed = consumers.Count > 0 ? consumers.Where(s => !completed.Contains(s.Id)).Sum(s => s.Inputs.Where(i => i.ItemKey == item.Key).Sum(i => i.Count)) : satisfiedInputs.Contains(item.Key) ? 0 : requirement.Count;
                if (steps.Any(s => s.Outputs.Count == 0 && completed.Contains(s.Id) && s.Inputs.Any(i => i.ItemKey == item.Key))) needed = 0;
                needed *= copies;
                if (needed == 0) continue;
                // The objective list shows the ingredients of unavailable intermediates,
                // not both the product and its ingredients as independent farm goals.
                if (produced.Contains(item.Key) && stock.GetValueOrDefault(item.Key) == 0) continue;
                var assigned = Math.Min(needed, stock.GetValueOrDefault(item.Key)); stock[item.Key] -= assigned;
                var available = item.Sources.Where(s => ExpansionRules.Available(s.Era, ExpansionRules.Current(profile))).ToList();
                var step = steps.FirstOrDefault(s => s.Inputs.Any(i => i.ItemKey == item.Key) && !completed.Contains(s.Id));
                var status = available.Any(x => x.Availability == "Observed drop") ? "Observed drop" : available.Count == 0 ? "Future / source unknown" : available.All(x => x.Availability == "Unavailable") ? "Unavailable" : available.Any(x => x.Availability == "Unverified") ? "Unverified" : "Wiki documented";
                rows.Add(new ObjectiveRow { Key = item.Key, Item = item.Name, Class = quest.Class, QuestId = quest.Id, Quest = quest.Name, Needed = needed, Owned = assigned, TotalOwned = inventory.Count(item), Zone = string.Join(", ", available.Where(s => knownZones.Count == 0 || knownZones.Contains(Identity.ZoneKey(s.Zone))).Select(x => x.Zone).DistinctBy(Identity.ZoneKey)), Mob = string.Join(", ", available.Select(x => x.Mob).Distinct()), Availability = status, Location = inventory.Locations(item), Sources = item.Sources, WikiUrl = item.WikiUrl, StepId = step?.Id ?? "", ParentReward = step == null ? "" : string.Join(" + ", step.Outputs.Select(o => catalog.FindKey(o.ItemKey)?.Name ?? o.ItemKey)), Instructions = step?.Instructions ?? "Collect for a later quest step. See the source walkthrough for prerequisites.", Estimated = inventory.Entries.Any(e => e.Estimated && inventory.Matches(e, item)), Eligibility = profile.CurrentClasses.Contains(quest.Class) ? "Current class goal" : profile.Level50Classes.Contains(quest.Class) ? "Level 50 class goal" : "Optional future goal" });
            }
        }
        foreach (var wish in profile.Wishlist.Where(x => !x.Completed))
        {
            var item = catalog.Find(wish.Name); int owned = item == null ? 0 : Math.Min(wish.Quantity, stock.GetValueOrDefault(item.Key));
            if (item != null) stock[item.Key] -= owned;
            rows.Add(new ObjectiveRow { Key = item?.Key ?? Identity.Key(wish.Name), Item = wish.Name, QuestId = wish.Id, Class = "GEAR", Quest = wish.Loadout + " wishlist", Needed = wish.Quantity, Owned = owned, TotalOwned = item == null ? 0 : inventory.Count(item), Zone = wish.Zone, Mob = wish.Source, Instructions = wish.Notes, WikiUrl = item?.WikiUrl ?? "", Sources = item?.Sources ?? [new() { Zone = wish.Zone, Mob = wish.Source }], Location = item == null ? "Unknown" : inventory.Locations(item), Availability = item?.Sources.Count > 0 ? "Wiki documented" : "User supplied" });
        }
        if (profile.GoalQueue.Any(g => g.Kind == "Exaltation"))
        {
            var keys = profile.GoalQueue.Where(g => g.Kind == "Exaltation").Select(g => g.ItemKey).ToHashSet();
            var effects = ExaltationPlanner.Build(new Catalog { Items = catalog.Items.Where(i => keys.Contains(i.Key)).ToList() }, inventory);
            foreach (var goal in profile.GoalQueue.Where(g => g.Kind == "Exaltation")) if (ExaltationPlanner.Objective(goal, effects.FirstOrDefault(e => e.MatchesId(goal.EffectId))) is { } row) rows.Add(row);
        }
        return rows;
    }
    public static bool InZone(ObjectiveRow row, string zone, string era) => row.Sources.Any(s => Identity.ZoneKey(s.Zone) == Identity.ZoneKey(zone) && ExpansionRules.Available(s.Era, era) && s.Availability != "Unavailable") || row.Class == "GEAR" && Identity.ZoneKey(row.Zone) == Identity.ZoneKey(zone);
    private static int Priority(string className, CharacterProfile profile)
    {
        int custom = profile.GoalPriority.IndexOf(className);
        return custom >= 0 ? custom : 100 + (profile.CurrentClasses.Contains(className) ? 0 : profile.Level50Classes.Contains(className) ? 20 : 40);
    }
    private static void CompleteAncestors(QuestStep step, List<QuestStep> steps, HashSet<string> completed, HashSet<string> visiting)
    {
        if (!visiting.Add(step.Id)) return;
        completed.Add(step.Id);
        foreach (var id in step.Prerequisites) if (steps.FirstOrDefault(x => x.Id == id) is { } parent) CompleteAncestors(parent, steps, completed, visiting);
    }
}
