using System.Text.Json;
using EqlTracker.Core;

internal static class PetDetailsTests
{
    internal static void Register(Action<string, Action> test)
    {
        void Check(bool value, string message = "Pet detail invariant failed") { if (!value) throw new Exception(message); }
        var inputs = Json.Read<List<PetItemRules.WikiFixture>>(Path.Combine(AppContext.BaseDirectory, "data/pet-item-fixtures.json"));
        ItemDefinition Fixture(string name) { var f = inputs.Single(i => Identity.Key(i.Title) == Identity.Key(name)); return WikiClient.ParseItem(f.Title, f.Text, f.Revision); }
        ItemDefinition Bow() => Fixture("Oakwynd");
        PetInventoryItem Entry(ItemDefinition d, int position = 1, int rank = 0) => new() { Id = "item-" + position, ItemKey = d.Key, Name = d.Name, Position = position, Slot = "Storage", Rank = rank };
        test("Oakwynd has sourced Archery type and classic display facts", () => {
            var i = Bow(); Check(PetItemRules.Type(i) == "Bow" && i.Display.Skill == "Archery"); Check(i.Display.Weight == .1 && i.Display.Range == 150 && i.Display.Size == "MEDIUM"); Check(i.Display.Flags.Contains("ARTIFACT") && i.Display.Source.Contains("eqlwiki.com") && i.Display.Revision > 0 && i.MetadataVersion == PetItemRules.MetadataVersion);
        });
        test("Classic metadata preserves single-sided hammer weapon facts and activation level", () => {
            var i = Fixture("Barbarian Spiritist's Hammer"); Check(i.Display.Skill == "2H Blunt" && i.TwoHanded && i.Display.Size == "LARGE" && i.Display.Weight == 10); Check(i.Stats["Damage"] == 22 && i.Stats["Delay"] == 43 && i.EffectActivationLevel == 30);
        });
        test("Ring classic metadata and supplied stats survive parsing", () => {
            var i = Fixture("Engineer's Ring"); Check(PetItemRules.Type(i) == "Ring" && i.Display.Size == "TINY" && i.Display.Weight == .1); Check(i.Stats.ContainsKey("INT") && i.Races.Count > 0 && i.Classes.Count > 0);
        });
        test("Types are not invented from an item name", () => {
            Check(PetItemRules.Type(new() { Name = "Amazing Bow", Slots = ["PRIMARY"] }) == "Unknown"); Check(PetItemRules.Type(new() { Slots = ["FINGERS"] }) == "Ring");
        });
        test("Normal Earth pet excludes bows despite Ranger class while Self remains eligible", () => {
            var i = Bow(); var owner = new LoadoutProfile { Classes = ["RNG"], Level = 50, PetType = "Earth", PetLevel = 50 }; var c = new Catalog { Items = [i] };
            Check(GearScorer.Eligibility(i, owner, true, null, c).Contains("bows")); Check(GearScorer.Eligibility(i, owner, false, null, c) == "Eligible"); Check(!PetItemRules.ShowCandidate(i, owner, c, false) && PetItemRules.ShowCandidate(i, owner, c, true));
        });
        test("Legacy ranged weapon cache is rejected without new display metadata", () => {
            var i = Bow(); i.Display = new(); Check(PetItemRules.Unsupported(i).Contains("Ranged")); i.Slots = ["PRIMARY"]; Check(PetItemRules.Unsupported(i) == "");
        });
        test("Normal pet inference excludes bows without removing inventory evidence", () => {
            var i = Bow(); var c = new Catalog { Items = [i] }; var owner = new LoadoutProfile { Classes = ["MAG", "RNG"] }; var b = new PetBuild { Items = [Entry(i)] }; string original = JsonSerializer.Serialize(b);
            var result = PetEquipmentInference.Build(b, owner, c); Check(result.Selections.Count == 0 && result.Totals.Weapons.Count == 0 && result.Totals.Procs.Count == 0 && JsonSerializer.Serialize(b) == original);
        });
        test("Direct normal evaluator cannot count legacy ranged ratios", () => {
            var i = Bow(); var c = new Catalog { Items = [i] }; var owner = new LoadoutProfile { Classes = ["RNG"], PetType = "Earth", PetLevel = 50, Level = 50 };
            var positions = EquipmentPositions.FromEntries([new() { Name = i.Name, ConfirmedItemKey = i.Key, Location = "Range", Count = 1 }]);
            var normal = new LoadoutEvaluationSession(c, owner, true, GearScorer.Defaults["Pet DPS"], positions, positions); Check(normal.Compare(i, 0, "RANGE:1").Unbuffed.Delta == null); Check(normal.CompareBuild(positions).Unbuffed.CurrentScore == 0);
            var self = new LoadoutEvaluationSession(c, owner, false, GearScorer.Defaults["Melee"], positions, positions); Check(self.Compare(i, 1, "RANGE:1").Unbuffed.Delta > 0);
        });
        test("Theorycraft bow evaluation is explicitly hypothetical with assumptions", () => {
            var i = Bow(); var c = new Catalog { Items = [i] }; var owner = new LoadoutProfile { Classes = ["MAG"], PetType = "Earth", PetLevel = 50 }; var positions = PetEquipmentInference.Build(new(), owner, c).Positions;
            var evaluation = new LoadoutEvaluationSession(c, owner, true, GearScorer.Defaults["Pet DPS"], positions, positions, true).Compare(i, 0, "RANGE:1");
            Check(evaluation.IsHypothetical && evaluation.Status == "Hypothetical" && evaluation.Assumptions.Any(s => s.Contains("bows")) && evaluation.Unbuffed.Delta > 0);
        });
        test("Unknown restrictions cannot become hypothetical numeric recommendations", () => {
            var i = new ItemDefinition { Key = "unknown", Name = "Unknown", Slots = ["CHEST"], Stats = new() { ["AC"] = 9999 } }; var c = new Catalog { Items = [i] }; var owner = new LoadoutProfile(); var p = PetEquipmentInference.Build(new(), owner, c).Positions;
            var result = new LoadoutEvaluationSession(c, owner, true, GearScorer.Defaults["Pet DPS"], p, p, true).Compare(i, 0, "CHEST:1"); Check(result.Unbuffed.Delta == null);
        });
        test("Theorycraft retains structural slot and locked Exaltation restrictions", () => {
            var item = new ItemDefinition { Key = "blade", Name = "Blade", MetadataComplete = true, Slots = ["PRIMARY"], Classes = ["WAR"], Stats = new() { ["Damage"] = 10, ["Delay"] = 20 } };
            var proc = new ItemDefinition { Key = "source", Name = "Source", MetadataComplete = true, Slots = ["PRIMARY"], Classes = ["WAR"], Effects = ["Proc: Earthquake (Combat)"] };
            var c = new Catalog { Items = [item, proc] }; var owner = new LoadoutProfile(); var positions = PetEquipmentInference.Build(new(), owner, c).Positions; var session = new LoadoutEvaluationSession(c, owner, true, GearScorer.Defaults["Pet DPS"], positions, positions, true);
            var instance = new InventoryEntry { Name = item.Name, Count = 1, Augments = [new() { Name = "Source (Exaltation)", EffectText = "Proc: Earthquake (Combat)" }] };
            var locked = session.Compare(item, 0, "PRIMARY:1", instance); Check(locked.Eligibility.Contains("+4") && locked.Unbuffed.Delta == null); Check(session.Compare(item, 4, "CHEST:1").Unbuffed.Delta == null);
            Check(ItemPairComparison.Create(item, null, item, 0, instance, owner, c).Warning.Contains("+4"));
        });
        test("Theorycraft workspace is detached and cannot leak through profile saves", () => {
            var source = new PetBuild { Name = "Current", Items = [Entry(Bow(), 1, 4)] }; var owner = new LoadoutProfile { ConfirmedPetInventory = source }; string before = JsonSerializer.Serialize(owner);
            var workspace = new PetTheorycraftWorkspace(source); workspace.Working.Items[0].Rank = 10; workspace.Changed(); Check(workspace.Dirty && workspace.Working.IsTheorycraft && workspace.Working.Id != source.Id && JsonSerializer.Serialize(owner) == before);
        });
        test("Theorycraft explicit Save snapshots and subsequent edits remain unsaved", () => {
            var workspace = new PetTheorycraftWorkspace(new() { Name = "Current", Items = [Entry(Bow())] }); var saved = workspace.Save("Future bow build"); Check(!workspace.Dirty && saved.IsTheorycraft);
            workspace.Working.Items[0].Rank = 7; Check(saved.Items[0].Rank == 0 && workspace.Dirty); var update = workspace.Save("Future bow build"); Check(update.Id == saved.Id && update.Items[0].Rank == 7 && !workspace.Dirty);
            var reopened = new PetTheorycraftWorkspace(update); Check(!reopened.Dirty && reopened.SavedId == update.Id);
        });
        test("Theorycraft Undo changes only the isolated working copy", () => {
            var source = new PetBuild { Items = [Entry(Bow(), 1, 3)] }; var workspace = new PetTheorycraftWorkspace(source); PetBuildEditing.Remember(workspace.Working); workspace.Working.Items[0].Rank = 8; Check(PetBuildEditing.Undo(workspace.Working, out _) && workspace.Working.Items[0].Rank == 3 && source.Items[0].Rank == 3);
        });
        test("Theorycraft activation is blocked even with normal level and rank", () => {
            var owner = new LoadoutProfile(); string before = JsonSerializer.Serialize(owner); Check(!PetBuildEditing.Activate(owner, new() { IsTheorycraft = true, PetLevel = 50 }, new(), out string issue) && issue.Contains("Theorycraft") && JsonSerializer.Serialize(owner) == before);
        });
        test("Theorycraft flag migrates additively and preserves IDs/ranks", () => {
            Check(!JsonSerializer.Deserialize<PetBuild>("{\"Name\":\"Legacy\"}")!.IsTheorycraft); var b = new PetBuild { IsTheorycraft = true, Items = [Entry(Bow(), 1, 20)] }; var copy = EquipmentPositions.Copy(b); Check(copy.Id == b.Id && copy.Items[0].Rank == 20 && copy.IsTheorycraft);
        });
        test("Metadata refresh reparses unchanged wiki revisions after parser version change", () => {
            string path = Path.Combine(Path.GetTempPath(), "eql-pet-metadata-" + Guid.NewGuid().ToString("N"), "cache.db");
            using var cache = new EquipmentCatalog(path); var old = Bow(); old.MetadataVersion = 3; cache.Index([old.Name]); cache.Store([old], [old.Name], 3); Check(cache.Pending(DateTime.UtcNow.AddDays(-1)).Contains(old.Name));
            var updated = Bow(); cache.Store([updated], [updated.Name]); Check(!cache.Pending(DateTime.UtcNow.AddDays(-1)).Contains(old.Name) && cache.Items().Single().Display.Skill == "Archery");
        });
        test("Display evidence enrichment leaves existing stats and identity intact", () => {
            var i = Bow(); i.Display = new(); i.GameId = 123; i.Stats["Damage"] = 999; var c = new Catalog { Items = [i] }; PetItemRules.ApplyDisplayEvidence(c, Path.Combine(AppContext.BaseDirectory, "data/pet-item-fixtures.json")); Check(i.Display.Type == "Bow" && i.GameId == 123 && i.Stats["Damage"] == 999);
        });
        test("Metadata version refresh is checkpointed even when an old page is removed", () => {
            string path = Path.Combine(Path.GetTempPath(), "eql-pet-reparse-" + Guid.NewGuid().ToString("N"), "cache.db"); var old = Bow(); old.MetadataVersion = 3;
            using (var cache = new EquipmentCatalog(path)) { cache.Index([old.Name]); cache.Store([old], [old.Name], 3); Check(cache.Pending(DateTime.UtcNow.AddDays(-1)).Contains(old.Name)); cache.Store([], [old.Name]); Check(cache.Pending(DateTime.UtcNow.AddDays(-1)).Count == 0); }
            using (var cache = new EquipmentCatalog(path)) { Check(cache.Items().Single().Key == old.Key && cache.Pending(DateTime.UtcNow.AddDays(-1)).Count == 0); }
        });
    }
}
