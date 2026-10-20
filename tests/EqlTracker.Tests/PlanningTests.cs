using EqlTracker.Core;

public static class PlanningTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new Exception("Planning assertion failed"); }
        var catalog = Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data", "catalog.json"));
        InventorySnapshot Stock(params string[] names) => new() { Entries = names.Select(n => new InventoryEntry { Name = n, Location = "Bank1", Count = 1 }).ToList() };
        test("Classic library is opt-in and epic defaults remain class-relevant", () => {
            var p = new CharacterProfile { CurrentClasses = ["MAG"] }; Check(catalog.Quests.Count(q => q.Kind == "Standard") >= 100);
            Check(!catalog.Quests.Where(q => q.Kind == "Standard").Any(q => QuestTracking.Selected(q, p)));
            Check(QuestTracking.Selected(catalog.Quests.Single(q => q.Class == "MAG" && q.Kind == "Epic"), p));
            Check(!QuestTracking.Selected(catalog.Quests.Single(q => q.Class == "WIZ" && q.Kind == "Epic"), p));
        });
        test("Explicit epic exclusion survives serialization and catalog migration", () => {
            var p = new CharacterProfile(); var q = catalog.Quests.Single(q => q.Class == "MAG" && q.Kind == "Epic"); QuestTracking.Track(p, q, false);
            var saved = System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
            Check(!QuestTracking.Selected(q, saved)); Check(!QuestPlanner.Plan(catalog, saved, new()).Any(r => r.QuestId == q.Id));
        });
        test("White cloak checklist uses Hide and magical crafted jewelry", () => {
            var q = catalog.Quests.Single(q => q.Name == "White Dragonscale Cloak"); Check(q.Expansion == "Classic" && q.RecommendedLevel == 45);
            Check(q.Steps.Single(s => s.Id == q.Id + ":turn-in").Inputs.Select(i => i.ItemKey).ToHashSet().SetEquals(["white dragon hide", "platinum diamond wedding ring", "black sapphire platinum necklace"]));
            Check(!q.Requirements.Any(i => i.ItemKey.Contains("scales"))); Check(q.Steps.Any(s => s.Outputs.Any(o => o.ItemKey == "platinum diamond wedding ring") && s.Inputs.Any(i => i.ItemKey == "enchanted platinum bar")));
        });
        test("Darkforge piece quantities and shared multi-class rewards stay singular", () => {
            var q = catalog.Quests.Single(q => q.Name == "Darkforge Breastplate"); Check(q.Requirements.Single(i => i.ItemKey == "enchanted platinum bar").Count == 2);
            var p = new CharacterProfile { CurrentExpansion = "Classic", PlanThrough = "Classic" }; QuestTracking.Track(p, q, true, 2);
            Check(QuestPlanner.Plan(catalog, p, new()).Single(r => r.QuestId == q.Id && r.Key == "platinum bar").Needed == 4);
            Check(catalog.Quests.Count(q => q.Name == "White Dragonscale Cloak") == 1);
        });
        test("Quest cards retain every pending ingredient and pause on untrack", () => {
            var q = catalog.Quests.Single(q => q.Name == "Darkforge Breastplate"); var p = new CharacterProfile { PlanThrough = "Classic" }; QuestTracking.Track(p, q, true);
            p.GoalQueue.Add(new() { Kind = "Quest", QuestId = q.Id, ItemKey = q.RewardKey, Name = q.Name });
            var rows = QuestPlanner.Plan(catalog, p, new()); Check(ObjectiveQueue.Build(p, catalog, rows, new()).Single().Ingredients.Count == 3);
            Check(ObjectiveQueue.Fallback(p, catalog, rows).Count == 3); QuestTracking.Track(p, q, false);
            Check(ObjectiveQueue.Build(p, catalog, rows).Single().Status == "Paused"); Check(ObjectiveQueue.Fallback(p, catalog, rows).Count == 0);
        });
        test("Owned quest reward satisfies card while explicit tracking remains reversible", () => {
            var q = catalog.Quests.Single(q => q.Name == "Darkforge Breastplate"); var p = new CharacterProfile(); QuestTracking.Track(p, q, true);
            p.GoalQueue.Add(new() { Kind = "Quest", QuestId = q.Id, ItemKey = q.RewardKey }); var stock = Stock(q.Name);
            Check(QuestTracking.Fulfilled(q, p, stock, catalog)); Check(!QuestPlanner.Plan(catalog, p, stock).Any(r => r.QuestId == q.Id));
            Check(ObjectiveQueue.Build(p, catalog, [], stock).Single().Status == "Satisfied / covered");
        });
        test("Exact introduction expansion differs from source availability", () => {
            var item = WikiClient.ParseItem("Future", "{{Kunark Era}}\n{{Itempage\n|itemname=Future\n|statsblock=Slot: BACK<br>Class: ALL<br>Race: ALL<br>\n}}", 7);
            Check(item.IntroducedExpansion == "Kunark" && item.ExpansionEvidence.Contains("7"));
            Check(new ItemDefinition().IntroducedExpansion == "Unknown");
        });
        test("Pet storage capacity and paired wearable slots are separate", () => {
            var loadout = new LoadoutProfile { Classes = ["MAG", "SHD", "BRD"] }; Check(PetEquipmentPlanner.Capacity(loadout.Classes) == 7);
            var build = new PetBuild { Items = [new() { Position = 8, Slot = "Chest" }] };
            Check(PetEquipmentPlanner.Validate(build, build.Items[0], loadout, catalog).StartsWith("Inactive"));
            build.Items = [new() { Position = 1, Slot = "Chest" }, new() { Position = 2, Slot = "Chest" }]; Check(PetEquipmentPlanner.Validate(build, build.Items[0], loadout, catalog) == "Too many items in slot");
        });
        test("Pet level is independent of owner and rank", () => {
            var item = new ItemDefinition { MetadataComplete = true, Classes = ["ALL"], Slots = ["PRIMARY"], RequiredLevel = 45 };
            var loadout = new LoadoutProfile { Level = 50, PetLevel = 30 }; Check(GearScorer.Eligibility(item, loadout, true, [], new()) == "Requires level 45");
            loadout.PetLevel = null; Check(GearScorer.Eligibility(item, loadout, true, [], new()).StartsWith("Confirm pet level"));
        });
        test("Planned pet Exaltations never become confirmed sockets implicitly", () => {
            var item = new PetInventoryItem { Name = "Blade", Rank = 4, Exaltations = [new() { State = "Planned", Effect = "Fire" }] }; Check(item.Entry().Augments.Count == 0);
            item.Exaltations[0].State = "Socketed"; Check(item.Entry().Augments.Count == 1 && item.Entry().Rank == 4);
        });
        test("Exaltation unlocks enforce destination rank and class intersection", () => {
            Check(new[] { "Ornamentation", "Focus", "Click", "Worn", "Proc" }.Select(ExaltationPlanner.Unlock).SequenceEqual([0, 1, 2, 3, 4]));
            var source = new ItemDefinition { Key = "s", Name = "Source", Classes = ["SHD"], Slots = ["PRIMARY"], MetadataComplete = true, Effects = ["Proc: Fire"] };
            var target = new ItemDefinition { Classes = ["MAG"], Slots = ["PRIMARY"], MetadataComplete = true };
            var effect = new ExaltationOption("s:0", source, "Proc", "Fire", 4, false, true); var l = new LoadoutProfile { Classes = ["SHD", "MAG"], Level = 50 };
            Check(ExaltationPlanner.Validate(effect, target, 3, "Primary", l, true, new()) == "Destination requires +4");
            Check(ExaltationPlanner.Validate(effect, target, 4, "Primary", l, true, new()) == "No shared class"); target.Classes = ["ALL"]; Check(ExaltationPlanner.Validate(effect, target, 4, "Primary", l, true, new()) == "Compatible");
        });
        test("Source ownership does not claim an extracted Exaltation or finish queue", () => {
            var source = new ItemDefinition { Key = "blade", Name = "Blade", Effects = ["Proc: Fire"], Sources = [new() { Zone = "Permafrost" }] }; var c = new Catalog { Items = [source] }; var inventory = Stock("Blade +4");
            var effect = ExaltationPlanner.Build(c, inventory).Single(); Check(!effect.Owned && effect.SourceOwned && effect.OwnershipEvidence.SourceReady && effect.State == "Source ready to extract");
            var p = new CharacterProfile { GoalQueue = [new() { Kind = "Exaltation", QuestId = "exaltation", ItemKey = source.Key, EffectId = effect.Id }] };
            Check(ObjectiveQueue.Fallback(p, c, [], inventory).Single().Remaining == 1);
            inventory.Entries.Add(new() { Name = "Blade (Exaltation)", Location = "Augmentation", Count = null }); inventory.Invalidate(); Check(ObjectiveQueue.Fallback(p, c, [], inventory).Count == 0);
        });
        test("Ambiguous multiple-effect Exaltation ownership stays unclassified", () => {
            var source = new ItemDefinition { Key = "x", Name = "X", Effects = ["Focus: A", "Proc: B"] }; var stock = new InventorySnapshot { Entries = [new() { Name = "X (Exaltation)", Location = "Augmentation", Count = null }] };
            var rows = ExaltationPlanner.Build(new() { Items = [source] }, stock); Check(rows.Count(r => r.Owned) == 1 && rows.Single(r => r.Owned).Type == "Unknown");
        });
        test("Older queue IDs and ownership survive additive planner fields", () => {
            var p = System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>("{\"GoalQueue\":[{\"QuestId\":\"war\",\"ItemKey\":\"scale\"}],\"CompletedSteps\":[\"war:step\"]}")!;
            Check(p.GoalQueue.Single().Id == "war|scale" && p.CompletedSteps.Contains("war:step") && p.QuestTracking.Count == 0 && p.GearExpansions.Count == 4);
        });
        test("Lambent gem intermediates suppress only their own ingredients", () => {
            var quest = catalog.Quests.Single(q => q.Name == "Lambent Breastplate"); var p = new CharacterProfile { CurrentExpansion = "Classic", PlanThrough = "Classic" }; QuestTracking.Track(p, quest, true);
            var empty = QuestPlanner.Plan(catalog, p, new()).Where(r => r.QuestId == quest.Id).ToList(); Check(empty.Any(r => r.Key == "lambent stone") && empty.Any(r => r.Key == "ruby" && r.Needed == 2));
            var owned = QuestPlanner.Plan(catalog, p, Stock("Lambent Ruby")).Where(r => r.QuestId == quest.Id).ToList(); Check(!owned.Any(r => r.Key is "lambent stone" or "ruby") && owned.Any(r => r.Key == "gypsy lute"));
        });
        test("Only the selected socketed effect appears in comparison effects", () => {
            var item = new ItemDefinition { Key = "item", Name = "Item", Slots = ["PRIMARY"], Classes = ["ALL"], MetadataComplete = true };
            var source = new ItemDefinition { Key = "source", Name = "Source", Slots = ["PRIMARY"], Classes = ["ALL"], MetadataComplete = true, Effects = ["Focus: A", "Proc: B"] };
            var c = new Catalog { Items = [item, source] }; var loadout = new LoadoutProfile { Equipment = [new() { Name = "Item", Location = "Primary" }] };
            var result = GearScorer.Compare(item, 0, "PRIMARY", loadout, false, c, [], [new() { Name = "Source (Exaltation)", EffectText = "Focus: A" }]);
            Check(result.Effects.Contains("Focus: A") && !result.Effects.Contains("Proc: B"));
        });
        test("Item proc activation level is not an equipment requirement", () => {
            var item = WikiClient.ParseItem("Proc blade", "{{Itempage\n|itemname=Proc blade\n|statsblock=Slot: PRIMARY<br>Class: ALL<br>Race: ALL<br>Effect: Fire (Combat) at Level 45<br>\n}}", 1);
            Check(item.RequiredLevel == 0 && item.EffectActivationLevel == 45);
        });
        test("Owned epic is not automatically selected in the discovery library", () => {
            var q = catalog.Quests.Single(q => q.Class == "MAG" && q.Kind == "Epic"); var p = new CharacterProfile(); var stock = Stock(catalog.FindKey(q.RewardKey)!.Name);
            Check(!QuestTracking.Selected(q, p, stock, catalog)); QuestTracking.Track(p, q, true); Check(QuestTracking.Selected(q, p, stock, catalog));
        });
        test("Exaltation goal identities survive effect order and label variations", () => {
            Check(ExaltationPlanner.EffectId("bloodmoon", "Focus: Improved Vampirism III") == ExaltationPlanner.EffectId("bloodmoon", "focus effect: Improved Vampirism III"));
            var item = new ItemDefinition { Key = "x", Effects = ["Focus: A", "Proc: B"] }; var c = new Catalog { Items = [item] }; var before = ExaltationPlanner.Build(c, new()).Select(e => e.Id).ToHashSet(); item.Effects.Reverse(); Check(before.SetEquals(ExaltationPlanner.Build(c, new()).Select(e => e.Id)));
        });
    }
}
