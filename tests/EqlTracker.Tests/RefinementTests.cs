using EqlTracker.Core;

public static class RefinementTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        Catalog Load() => Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data/catalog.json"));
        CharacterProfile Mage() => new() { CurrentClasses = ["MAG"], OptionalClasses = [] };
        InventorySnapshot Stock(params string[] names) => new() { Complete = true, CapturedAt = DateTime.Now, Entries = names.Select(n => new InventoryEntry { Name = n, Count = 1, Location = "Bank13-Slot20" }).ToList() };
        test("Bank focus rewards suppress their own ingredients", () =>
        {
            var c = Load(); var rows = QuestPlanner.Plan(c, Mage(), Stock("Broom of Trilon", "Shovel of Ponz"));
            Check(!rows.Any(r => r.Item is "Cyclops Toes" or "Griffon Feathers" or "Star Ruby" or "Hill Giant Toes" or "Gargoyle Eye" or "Ruby" or "A Shovel" or "A Broom"));
            Check(rows.Any(r => r.Item == "Shovel of Ponz" && r.Owned == 1));
        });
        test("Broom and shovel satisfy separate recipe branches", () =>
        {
            var c = Load(); var rows = QuestPlanner.Plan(c, Mage(), Stock("Shovel of Ponz"));
            Check(rows.Any(r => r.Item == "Cyclops Toes" && r.ParentReward == "Broom of Trilon")); Check(!rows.Any(r => r.Item == "Hill Giant Toes"));
        });
        test("Verified shovel identity matches exported bank names", () =>
        {
            var c = Load(); var stock = Stock("Shovel"); stock.Entries[0].GameId = 16545;
            Check(stock.Count(c.Find("A Shovel")!) == 1); Check(stock.Ownership(c.Find("A Shovel")!).Contains("Bank13-Slot20"));
        });
        test("Shared bank and collection evidence stay distinct", () =>
        {
            var item = new ItemDefinition { Name = "Test", Key = "test", GameId = 123 }; var stock = Stock("Test"); stock.Entries[0].GameId = 123; stock.Entries[0].Location = "SharedBank1-Slot2";
            stock.Entries.Add(new() { Name = "Test (Exaltation)", GameId = 123, Count = 1, Location = "General1-Slot7" }); stock.Entries.Add(new() { Name = "Test", GameId = 123, Count = null, Location = "Equipment" });
            Check(stock.Count(item) == 1); Check(stock.Ownership(item).Contains("Shared bank: 1")); Check(stock.Ownership(item).Contains("quantity unknown"));
        });
        test("A covered branch does not erase a shared ingredient in another branch", () =>
        {
            var c = new Catalog { Items = [new() { Key = "ruby", Name = "Ruby" }, new() { Key = "a", Name = "A" }, new() { Key = "b", Name = "B" }], Quests = [new() { Id = "test", Class = "MAG", Requirements = [new() { ItemKey = "ruby" }], Steps = [new() { Id = "a", Inputs = [new() { ItemKey = "ruby" }], Outputs = [new() { ItemKey = "a" }] }, new() { Id = "b", Inputs = [new() { ItemKey = "ruby" }], Outputs = [new() { ItemKey = "b" }] }] }] };
            Check(QuestPlanner.Plan(c, Mage(), Stock()).Single().Needed == 2);
            Check(QuestPlanner.Plan(c, Mage(), Stock("A")).Single().Needed == 1);
        });
        test("Quest hierarchy exposes intermediate ownership and covered leaves", () =>
        {
            var c = Load(); var tree = QuestHierarchy.Build(c.Quests.Single(q => q.Class == "MAG" && q.Kind == "Epic"), c, Mage(), Stock("Broom of Trilon"));
            IEnumerable<QuestNode> Flatten(IEnumerable<QuestNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
            var nodes = Flatten(tree).ToList(); Check(nodes.Any(n => n.Name == "Broom of Trilon" && n.Status == "Owned")); Check(nodes.Any(n => n.Name.StartsWith("Cyclops Toes") && n.Status == "Covered by reward"));
        });
        test("Zone index excludes mobs and narrative placeholders", () =>
        {
            var c = Load(); Check(c.Zones.Contains("Permafrost")); Check(!c.Zones.Any(z => z is "A Cyclops" or "See walkthrough" or "Various"));
        });
        test("Bloodmoon is available offline with its focus effect", () =>
        {
            var item = Load().Find("Bloodmoon")!; Check(item.Slots.Contains("PRIMARY")); Check(item.Classes.Contains("SHD")); Check(item.Effects.Any(e => e.Contains("Improved Vampirism III")));
        });
        test("Metadata parser retains structured focus and proc fields", () =>
        {
            var item = WikiClient.ParseItem("Test", "{{Itempage\n|itemname=Test\n|statsblock=Slot: PRIMARY<br>\nClass: SHD<br>\nRace: ALL<br>\n|focus_effect=Improved Vampirism III\n|proc_effect=Flame Strike\n}}", 123);
            Check(item.Effects.Count == 2); Check(item.MetadataVersion == PetItemRules.MetadataVersion);
        });
        test("Comparison contributions reconcile and explain preference points", () =>
        {
            var item = new ItemDefinition { Key = "blade", Name = "Blade", MetadataComplete = true, Slots = ["PRIMARY"], Classes = ["ALL"], Stats = new() { ["DEX"] = 11 } };
            var baseline = new ItemDefinition { Key = "old", Name = "Old", MetadataComplete = true, Slots = ["PRIMARY"], Classes = ["ALL"] };
            var c = new Catalog { Items = [item, baseline] }; var p = new LoadoutProfile { Equipment = [new() { Name = "Old", Location = "Primary" }] };
            var comparison = GearScorer.Compare(item, 0, "PRIMARY", p, false, c, new() { ["DEX"] = .5 });
            Check(comparison.CanCompare); Check(comparison.Breakdown.Sum(x => x.Points) == comparison.Delta && comparison.Delta == 5.5); Check(comparison.Contributions.Contains("preference points"));
            p.Equipment.Clear(); Check(!GearScorer.Compare(item, 0, "PRIMARY", p, false, c, new() { ["DEX"] = .5 }).CanCompare);
        });
        test("Unconfirmed pet gear retains the item score without inventing an upgrade", () =>
        {
            var item = new ItemDefinition { Name = "Blade", MetadataComplete = true, Slots = ["PRIMARY"], Classes = ["ALL"], Stats = new() { ["DEX"] = 11 } };
            var comparison = GearScorer.Compare(item, 0, "PRIMARY", new(), true, new(), new() { ["DEX"] = .5 });
            Check(comparison.ItemScore == 5.5); Check(!comparison.CanCompare); Check(comparison.Verdict == "Confirm pet equipment");
        });
        test("Item scoring distinguishes missing metadata from restricted planning items", () =>
        {
            var item = new ItemDefinition { Name = "Blade", Slots = ["PRIMARY"], Classes = ["ROG"], Stats = new() { ["DEX"] = 11 } };
            var pending = GearScorer.Compare(item, 0, "PRIMARY", new() { Classes = ["SHD"] }, false, new(), new() { ["DEX"] = .5 });
            Check(pending.ItemScore == null && !pending.CanCompare);
            item.MetadataComplete = true;
            var restricted = GearScorer.Compare(item, 0, "PRIMARY", new() { Classes = ["SHD"] }, false, new(), new() { ["DEX"] = .5 });
            Check(restricted.ItemScore == 5.5 && !restricted.CanCompare && restricted.Verdict == "Requires another class / pet");
        });
        test("Catalog migration retains verified identities aliases and observed sources", () =>
        {
            var old = new Catalog { Items = [new() { Key = "same", Name = "Same", GameId = 42, Sources = [new() { Zone = "Permafrost", Availability = "Observed drop" }] }] };
            var merged = CatalogMigration.Merge(old, new() { SchemaVersion = 2, Zones = ["Permafrost"], Items = [new() { Key = "same", Name = "Same", Aliases = ["Alias"] }] });
            Check(merged.Find("Alias")?.GameId == 42); Check(merged.Items[0].Sources[0].Availability == "Observed drop");
        });
        test("Equipment catalog resumes unfinished batches across restart", () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "eql-catalog-" + Guid.NewGuid()); string path = Path.Combine(folder, "catalog.db");
            using (var cache = new EquipmentCatalog(path)) { cache.Index(["Bloodmoon", "Next"]); var current = Load().Find("Bloodmoon")!; current.MetadataVersion = PetItemRules.MetadataVersion; cache.Store([current], ["Bloodmoon"]); cache.SaveState(new() { Continuation = "cursor" }); }
            using (var cache = new EquipmentCatalog(path)) { Check(cache.State().Continuation == "cursor"); Check(cache.Pending(DateTime.UtcNow.AddDays(-1)).SequenceEqual(["Next"])); Check(cache.Items().Single().Name == "Bloodmoon"); }
            Directory.Delete(folder, true);
        });
        test("Zero-delay wiki weapons never generate non-finite cached stats", () =>
        {
            var item = WikiClient.ParseItem("Zero", "{{Itempage\n|itemname=Zero\n|statsblock=Slot: PRIMARY<br>\nDMG: 8 Delay: 0<br>\nClass: ALL<br>\n}}", 1);
            Check(item.Stats.Values.All(double.IsFinite)); System.Text.Json.JsonSerializer.Serialize(item);
        });
        test("Bundled full equipment snapshot supports offline lookup", () =>
        {
            var snapshot = Json.Read<EquipmentSnapshot>(Path.Combine(AppContext.BaseDirectory, "data/equipment.json"));
            Check(snapshot.Items.Count >= 6000 && snapshot.Titles.Count >= snapshot.Items.Count);
            Check(snapshot.Items.Any(i => i.Name == "Bloodmoon" && i.Effects.Any(e => e.Contains("Vampirism"))));
            Check(snapshot.Items.All(i => i.Stats.Values.All(double.IsFinite)));
        });
        test("Panel and tree state survive profile serialization", () =>
        {
            var profile = Mage(); profile.Panels["Pet damage"] = new() { Collapsed = true, Height = 160, Width = 240 }; profile.CollapsedQuestNodes.Add("mag/branch");
            var restored = System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!;
            Check(restored.Panels["Pet damage"].Height == 160 && restored.Panels["Pet damage"].Collapsed); Check(restored.CollapsedQuestNodes.Contains("mag/branch"));
        });
    }
}
