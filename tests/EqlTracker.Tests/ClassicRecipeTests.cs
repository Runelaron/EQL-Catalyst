using EqlTracker.Core;

public static class ClassicRecipeTests
{
    public static void Register(Action<string, Action> test)
    {
        var c = Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data", "catalog.json"));
        void Check(bool condition, string message = "Classic recipe assertion failed") { if (!condition) throw new Exception(message); }
        InventorySnapshot Stock(params (string Name, int Count)[] entries) => new() { Entries = entries.Select(x => new InventoryEntry { Name = x.Name, Count = x.Count, Location = "Bank1" }).ToList() };
        CharacterProfile Profile(QuestDefinition q, int copies = 1) { var p = new CharacterProfile { CurrentClasses = [], OptionalClasses = [], PlanThrough = "Classic", CurrentExpansion = "Classic" }; QuestTracking.Track(p, q, true, copies); return p; }
        foreach (var quest in c.Quests.Where(q => q.Kind == "Standard"))
            test("Curated Classic graph: " + quest.Name, () => {
                Check(quest.RecipeVersion == 1 && quest.ChecklistCoverage == "Complete graph");
                Check(quest.Steps.Any(s => s.Id == quest.Id + ":turn-in"));
                Check(!quest.Steps.Any(s => s.Inputs.Any(i => i.ItemKey == quest.RewardKey)));
                Check(quest.Steps.All(s => s.Inputs.Concat(s.Outputs).All(i => i.Count > 0 && c.FindKey(i.ItemKey) != null)));
                var p = Profile(quest); var rows = QuestPlanner.Plan(c, p, new());
                Check(rows.Count > 0, "No leaf requirements: " + quest.Name);
                Check(rows.All(r => r.Needed > 0 && r.Sources.Count > 0));
                Check(QuestHierarchy.Build(quest, c, p, new()).Count > 0);
            });
        test("Partial Ronium ownership expands only missing copies", () => {
            var q = c.Quests.Single(q => q.Name == "Ivy Etched Boots"); var p = Profile(q, 2);
            var rows = QuestPlanner.Plan(c, p, Stock(("A Bar of Ronium", 1)));
            Check(rows.Single(r => r.Key == "a bar of ronium").Owned == 1);
            Check(rows.Single(r => r.Key == "platinum bar").Remaining == 2);
            Check(rows.Single(r => r.Key == "mistmoore granite").Remaining == 3);
        });
        test("Owned Ro mold covers only its own branch", () => {
            var q = c.Quests.Single(q => q.Name == "Helm of Ro"); var rows = QuestPlanner.Plan(c, Profile(q), Stock(("Mold of Ro Helm", 1)));
            Check(!rows.Any(r => r.Key == "cyclops skull")); Check(rows.Any(r => r.Key == "platinum bar"));
        });
        test("Shared stock is allocated once across curated quests", () => {
            var a = c.Quests.Single(q => q.Name == "Helm of Ro"); var b = c.Quests.Single(q => q.Name == "Ivy Etched Boots"); var p = Profile(a); QuestTracking.Track(p, b, true);
            var rows = QuestPlanner.Plan(c, p, Stock(("Enchanted Platinum Bar", 2)));
            Check(rows.Where(r => r.Key == "enchanted platinum bar").Sum(r => r.Owned) == 2);
            Check(rows.Where(r => r.Key == "platinum bar").Sum(r => r.Remaining) == 2);
        });
        test("Lynuga remains incomplete after one failed attempt", () => {
            var q = c.Quests.Single(q => q.Name == "Brutechopper"); var p = Profile(q);
            Check(q.Steps.Single().RandomReward); Check(!QuestTracking.Fulfilled(q, p, Stock(("Ruby", 1)), c));
            Check(!QuestTracking.Fulfilled(q, p, new(), c)); Check(QuestTracking.Fulfilled(q, p, Stock((q.Name, 1)), c));
        });
        test("Paw routes do not require both a kill and pie", () => {
            var q = c.Quests.Single(q => q.Name == "Paw of Opolla"); var p = Profile(q);
            Check(!QuestPlanner.Plan(c, p, new()).Any(r => r.Key == "rat ears")); p.Routes[q.Id] = "Alternative";
            var rows = QuestPlanner.Plan(c, p, new()); Check(rows.Any(r => r.Key == "rat ears")); Check(!rows.Any(r => r.Key == "severed paw"));
        });
        test("Unqualified fragments cannot satisfy all four clay runes", () => {
            var q = c.Quests.Single(q => q.Name == "Clay Bracelet"); var p = Profile(q); var inventory = Stock(("Rune of Clay", 4));
            Check(QuestPlanner.Plan(c, p, inventory).Sum(r => r.Owned) == 0);
            inventory.Entries[0].Count = 1; p.ItemIdentityConfirmations[QuestRecipes.InventoryIdentity(inventory.Entries[0])] = "rune of clay:top";
            QuestRecipes.ApplyIdentities(p, inventory); var rows = QuestPlanner.Plan(c, p, inventory); Check(rows.Sum(r => r.Owned) == 1 && rows.Sum(r => r.Remaining) == 3);
            p.ItemIdentityConfirmations.Clear(); QuestRecipes.ApplyIdentities(p, inventory); Check(QuestPlanner.Plan(c, p, inventory).Sum(r => r.Owned) == 0);
        });
        test("Returned files and multi-output puzzle are not duplicated", () => {
            var q = c.Quests.Single(q => q.Name == "Regent Symbol of Innoruuk"); var rows = QuestPlanner.Plan(c, Profile(q), new());
            Check(q.Steps.Single(s => s.Outputs.Any(o => o.ItemKey == "steel boning")).Inputs.Single(i => i.ItemKey == "file").Consumed == false);
            Check(rows.Single(r => r.Key == "file mold").Needed == 1);
            var badge = c.Quests.Single(q => q.Name == "Researcher's Badge"); rows = QuestPlanner.Plan(c, Profile(badge), new());
            Check(rows.Single(r => r.Key == "live enchanted rat: jar 1").Needed == 1);
            Check(rows.Single(r => r.Key == "confession document").Needed == 4);
        });
        test("Retained seal avoids repeating the entire badge chain", () => {
            var q = c.Quests.Single(q => q.Name == "Qeynos Badge of Honor"); var rows = QuestPlanner.Plan(c, Profile(q), new());
            Check(rows.Single(r => r.Key == "live enchanted rat: jar 1").Needed == 1);
        });
        test("Nested recipe yields preserve exact required materials", () => {
            var q = c.Quests.Single(q => q.Name == "Regent Symbol of Innoruuk"); var row = QuestPlanner.Plan(c, Profile(q), new()).Single(r => r.Key == "small piece of ore");
            Check(row.Needed == 2, "One file uses one of two produced metal bits, not two separate combines.");
        });
        test("Manual completion and undo preserve stable final step IDs", () => {
            var q = c.Quests.Single(q => q.Name == "Helm of Ro"); var p = Profile(q); p.CompletedSteps.Add(q.Id + ":turn-in");
            Check(QuestPlanner.Plan(c,p,new()).Count == 0);p.CompletedSteps.Clear();Check(QuestPlanner.Plan(c,p,new()).Count > 0);
        });
        test("Curated source replacement retains observed evidence", () => {
            var saved = new Catalog { SchemaVersion = 5, Items = [new() { Key = "x", Sources = [new() { Zone = "Wrong", Mob = "Old" }, new() { Zone = "Befallen", Mob = "Seen", Availability = "Observed drop" }] }] };
            var bundled = new Catalog { SchemaVersion = 6, Items = [new() { Key = "x", Sources = [new() { Zone = "Rathe Mountains", Mob = "Verified", EvidenceId = "classic-v1:x" }] }] };
            var result = CatalogMigration.Merge(saved,bundled).Items.Single();Check(result.Sources.Count == 2 && !result.Sources.Any(s => s.Zone == "Wrong"));
        });
        test("Icon preference is additive per profile and manual", () => {
            var a = System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>("{}")!; var b = new CharacterProfile();Check(a.AppIconId == "blue");a.AppIconId = "class-mag";a.CurrentClasses = ["WAR"];Check(a.AppIconId == "class-mag" && b.AppIconId == "blue");
        });
        test("Later monk reward covers earlier quest without creating physical stock", () => {
            var q = c.Quests.Single(q => q.Name == "White Headband"); var p = Profile(q);var inventory=Stock(("Purple Headband",1));
            Check(QuestTracking.Fulfilled(q,p,inventory,c));Check(QuestPlanner.Plan(c,p,inventory).Count==0);Check(inventory.Count(c.FindKey(q.RewardKey)!)==0);
            p.QuestQuantities[q.Id]=2;Check(!QuestTracking.Fulfilled(q,p,inventory,c));Check(QuestPlanner.Plan(c,p,inventory).Single(r=>r.Key=="gnoll pup scalp").Needed==4);
        });
        test("Recipe alternatives are independent of the quest acquisition route", () => {
            var q=c.Quests.Single(q=>q.Name=="Paw of Opolla");var p=Profile(q);p.Routes[q.Id]="Alternative";p.Routes[q.Id+"|uncooked-rat-ear-pie"]="Giant rat ear";
            var rows=QuestPlanner.Plan(c,p,new());Check(rows.Any(r=>r.Key=="giant rat ear") && !rows.Any(r=>r.Key=="rat ears"));
        });
        test("Same-source lore fragments require explicit identification", () => {
            var item=c.FindKey("tanned split paw skin:volume-1")!;var inventory=Stock(("Tanned Split Paw Skin",1));inventory.Entries[0].AcquiredFrom="Splitpaw gnolls";
            Check(inventory.Count(item)==0);inventory.Entries[0].ConfirmedItemKey=item.Key;inventory.Invalidate();Check(inventory.Count(item)==1);
        });
        test("Classic source aliases match logged zones", () => {
            foreach(var (a,b) in new[]{("Northern Karana","North Karana"),("Western Karana","Western Plains of Karana"),("Cazic-Thule","Cazic Thule"),("Southern Desert of Ro","South Ro"),("Mistmoore Castle","Castle Mistmoore")})Check(Identity.ZoneKey(a)==Identity.ZoneKey(b));
        });
    }
}
