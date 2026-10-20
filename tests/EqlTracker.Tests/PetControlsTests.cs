using EqlTracker.Core;

static class PetControlsTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new Exception("Pet control assertion failed"); }
        ItemDefinition Weapon(string key, params string[] slots) => new() { Key = key, Name = key, MetadataComplete = true, Classes = ["WAR"], Slots = slots.ToList(), TwoHanded = true, Stats = new() { ["Damage"] = 25, ["Delay"] = 36 } };
        test("Earthshaker on Sunderfury +4 allows Storage and normalized Primary WAR intersection", () =>
        {
            var source = Weapon("Earthshaker", "PRIMARY"); source.Effects = ["Earthquake (Combat) at Level 45"];
            var destination = Weapon("Sunderfury", "Primary"); var effect = new ExaltationOption("earthquake", source, "Proc", source.Effects[0], 4, false, false); var owner = new LoadoutProfile { Classes = ["MAG"], PetType = "Earth", PetLevel = null };
            foreach (string slot in new[] { "Storage", "PRIMARY", "Primary" }) Check(ExaltationPlanner.Validate(effect, destination, 4, slot, owner, true, new()) == "Compatible");
            Check(ExaltationPlanner.Validate(effect, destination, 3, "Storage", owner, true, new()).Contains("+4")); Check(ExaltationPlanner.Validate(effect, destination, 4, "Secondary", owner, true, new()) != "Compatible");
            var item = new PetInventoryItem { Slot = "Storage", Rank = 4 }; Check(ExaltationPlanner.Assign(item, effect, destination, 4, "Storage", owner, new(), "Socketed", false, out _));
            var next = effect with { Id = "other", Effect = "Other (Combat)" };
            Check(!ExaltationPlanner.Assign(item, next, destination, 4, "Storage", owner, new(), "Socketed", false, out string reason) && reason.Contains("occupied") && item.Exaltations.Single().EffectId == "earthquake");
            Check(ExaltationPlanner.Assign(item, next, destination, 4, "Storage", owner, new(), "Planned", true, out _) && item.Exaltations.Single().EffectId == "other");
            var restored = EquipmentPositions.Copy(item); Check(restored.Exaltations.Single().State == "Planned");
        });
        test("Pet ratings sort numerically with Conditional and unavailable last in both directions", () =>
        {
            var rows = new (double? Score, bool Conditional)[] { (-5, false), (0, false), (17, false), (2, false), (null, false), (100, true) };
            foreach (bool descending in new[] { true, false })
            { var sorted = rows.OrderBy(r => r, Comparer<(double? Score, bool Conditional)>.Create((a,b) => PetBuildEditing.CompareRatings(a.Score,a.Conditional,b.Score,b.Conditional,descending))).ToArray(); Check(sorted.Take(4).Select(r => r.Score).SequenceEqual(descending ? new double?[] { 17,2,0,-5 } : [-5,0,2,17])); Check(sorted.Skip(4).All(r => r.Score == null || r.Conditional)); }
        });
        test("Build context saves valid custom plans, rejects Current hypothetical data and supports interleaved Undo", () =>
        {
            var build = new PetBuild { Name = "Original", PetType = "Earth", Items = [new() { Id = "a", ItemKey = "a", Rank = 7, Slot = "Storage", Position = 1 }] };
            Check(PetBuildEditing.ApplyContext(build, "Air", 60, "Custom summon", 11, "Custom focus", false, out _));
            Check(PetBuildEditing.Hypothetical(build.PetLevel,build.SummonRank)); Check(!PetBuildEditing.ApplyContext(build,"Fire",60,"Other",11,"",true,out _) && build.PetType == "Air");
            Check(PetInventoryEditing.SetRank(build,"a",9,new(),out _)); Check(PetBuildEditing.Undo(build,out _) && build.Items.Single().Rank == 7 && build.PetLevel == 60);
            Check(PetBuildEditing.Undo(build,out _) && build.PetLevel == null && build.PetType == "Earth" && build.Items.Single().Id == "a");
            for (int i=0;i<25;i++) Check(PetBuildEditing.ApplyContext(build,"Earth",20,"Summon",i,"",false,out _)); Check(build.EditHistory.Count == 20); Check(PetBuildEditing.Undo(EquipmentPositions.Copy(build),out _));
        });
        test("Planned rating presets stay separate from Current and Undo restores the selection", () =>
        {
            var owner = new LoadoutProfile { PetPreset = "Pet DPS" }; var plan = new PetBuild { Preset = "Melee" };
            Check(GuidedPetBuilds.Context(owner,plan).PetPreset == "Melee" && owner.PetPreset == "Pet DPS"); PetBuildEditing.Remember(plan,"Rating preset"); plan.Preset="Balanced Tank"; Check(PetBuildEditing.Undo(plan,out _) && plan.Preset=="Melee");
        });
        test("Current build activation is reviewed separately and Undo restores context, effects and inactive entries", () =>
        {
            var current = new PetBuild { Id = "current-id", PetType = "Earth", Items = [new() { Id = "retained", ItemKey = "old", Name = "Old", Position = 15, Rank = 12, Exaltations = [new() { Effect = "Proc: retained", State = "Socketed" }] }] };
            var owner = new LoadoutProfile { ConfirmedPetInventory = current }; var plan = new PetBuild { Id = "plan-id", Name = "Plan", PetType = "Water", PetLevel = 50, Items = [new() { Id = "new", ItemKey = "new", Position = 1, EquipmentVerified = true }] };
            Check(PetBuildEditing.Activate(owner,plan,new(),out _)); var active = owner.ConfirmedPetInventory!; Check(active.Id == "current-id" && active.PetType == "Water" && active.Items.Single().EquipmentVerified == false && plan.Items.Single().EquipmentVerified);
            Check(PetBuildEditing.Undo(active,out _) && active.PetType == "Earth" && active.Items.Single().Position == 15 && active.Items.Single().Exaltations.Count == 1);
            plan.PetLevel = 60; Check(!PetBuildEditing.Activate(owner,plan,new(),out _));
        });
        test("Raw item pairs retain ranked differences and effects when incompatible or metadata is missing", () =>
        {
            var a = Weapon("Current", "PRIMARY"); a.Stats["STR"] = 5; var b = Weapon("Candidate", "CHEST"); b.Classes = ["CLR"]; b.Stats["DEX"] = 8;
            var pair = ItemPairComparison.Create(a,new() { Name="Current +4" },b,2,null,new() { Classes=["MAG"],PetType="Earth" },new());
            Check(pair.Current.Rank == 4 && pair.Candidate.Rank == 2 && pair.Warning.Length > 0 && pair.Differences.Any(s => s.Stat == "STR" && s.Change < 0));
            b.MetadataComplete = false; var missing = ItemPairComparison.Create(null,null,b,0,null,new(),new()); Check(missing.Differences.All(s => s.Current == null));
        });
        test("Sourced summons cover every supported type, sort by caster level and preserve focus conflicts", () =>
        {
            var catalog = PetContextCatalog.Load(Path.Combine(AppContext.BaseDirectory,"data","pet-context.json"));
            foreach (string type in new[] {"Earth","Water","Air","Fire","Undead","Animation","Warder","Spirit"}) { var spells = catalog.Summons(type); Check(spells.Count > 0 && spells.All(s => s.Revision > 0 && s.RequiredLevel <= 50 && s.Url.StartsWith("https://eqlwiki.com/"))); Check(spells.Select(s => s.RequiredLevel).SequenceEqual(spells.Select(s => s.RequiredLevel).OrderDescending())); }
            var earth = catalog.Summons("Earth").First(); Check(earth.RequiredLevel == 46 && earth.Name == "Greater Conjuration: Earth" && earth.Aliases.Contains("Greater Conjuration: Earth Summon"));
            var build = new PetBuild { PetType="Earth",PetLevel=50,SummonSpell=earth.Name,Focus="Shovel of Ponz" }; Check(catalog.FocusEvidence(build).Contains("Matching lesser-focus")); build.Focus="Staff of Elemental Mastery: Earth"; Check(catalog.FocusEvidence(build).Contains("conflicts"));
        });
    }
}
