using EqlTracker.Core;

static class PetUpgradeTests
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value, string message = "Pet upgrade assertion failed") { if (!value) throw new Exception(message); }
        static ItemDefinition Item(string name, string slot, Dictionary<string, double> stats) => new() { Key = Identity.Key(name), Name = name, Slots = [slot], Classes = ["ALL"], Races = ["ALL"], Stats = stats, MetadataComplete = true };
        static PetInventoryItem Entry(ItemDefinition item, int position, int rank = 0) => new() { Id = Guid.NewGuid().ToString("N"), ItemKey = item.Key, Name = item.Name, Position = position, Rank = rank, Slot = "Storage" };
        test("Seven actual pet inventory entries infer ranked equipment without profiles or manual slots", () => {
            var snapshot = Json.Read<EquipmentSnapshot>(Path.Combine(AppContext.BaseDirectory, "data/equipment.json")); var c = new Catalog { Items = snapshot.Items };
            var owner = new LoadoutProfile { Classes = ["SHD", "BRD", "MAG"], PetType = "Earth", PetLevel = null };
            var build = new PetBuild { PetType = "Earth", Items = new[] { ("Sunderfury", 4), ("Indicolite Breastplate", 5), ("Indicolite Bracer", 7), ("Indicolite Greaves", 7), ("Cloak of Flames", 4), ("Icebelt", 3), ("Indicolite Vambraces", 7) }.Select((x, i) => Entry(c.Find(x.Item1) ?? throw new Exception("Missing " + x.Item1), i + 1, x.Item2)).ToList() };
            var before = System.Text.Json.JsonSerializer.Serialize(build); var inferred = PetEquipmentInference.Build(build, owner, c);
            Check(inferred.Selections.Count == 7, string.Join("; ", inferred.Totals.Notes)); Check(inferred.Totals.Stats["AC"] > 0 && inferred.Totals.Stats["Haste"] > 0 && inferred.Totals.Weapons.Count == 1);
            Check(before == System.Text.Json.JsonSerializer.Serialize(build) && inferred.Selections.All(s => !s.Verified));
            var chest = c.Find("Indicolite Breastplate")!; var session = new LoadoutEvaluationSession(c, GuidedPetBuilds.Context(owner, build), true, GearScorer.Defaults["Pet DPS"], inferred.Positions, inferred.Positions);
            var compare = PetEquipmentInference.Best(chest, 6, null, session, inferred.Positions)!; Check(compare.CanCompare && compare.Evaluation!.Unbuffed.Delta > 0, compare.Confidence);
        });
        test("Pet armor inference uses ranked AC, paired limits, corrections and active capacity", () => {
            var a = Item("a", "CHEST", new() { ["AC"] = 10 }); var b = Item("b", "CHEST", new() { ["AC"] = 12 }); var wrist = Item("wrist", "WRIST", new() { ["DEX"] = 5 }); var c = new Catalog { Items = [a,b,wrist] }; var owner = new LoadoutProfile { Classes = ["MAG"] }; var build = new PetBuild { Items = [Entry(a,1,5),Entry(b,2),Entry(wrist,3),Entry(wrist,4),Entry(wrist,5),Entry(b,8,20)] };
            var result = PetEquipmentInference.Build(build, owner, c); Check(result.Positions.Single(p => p.Slot == "Chest").Item!.Rank == 5); Check(result.Selections.Count(s => s.PositionId.StartsWith("WRIST")) == 2 && result.Selections.All(s => s.EntryId != build.Items[^1].Id));
            build.Items[1].Slot = "Chest"; build.Items[1].EquipmentVerified = true; result = PetEquipmentInference.Build(build, owner, c); Check(result.Positions.Single(p => p.Slot == "Chest").Item!.ConfirmedItemKey == b.Key && result.Selections.Single(s => s.EntryId == build.Items[1].Id).Verified);
        });
        test("Pet ratios and two-hand dual-wield comparisons stay conditional; procs stay separate", () => {
            var a = Item("a", "PRIMARY", new() { ["Damage"] = 10, ["Delay"] = 20 }); var b = Item("b", "SECONDARY", new() { ["Damage"] = 8, ["Delay"] = 20 }); var two = Item("two", "PRIMARY", new() { ["Damage"] = 30, ["Delay"] = 25 }); two.TwoHanded = true; two.Effects = ["Proc: Flame Shock (Combat)"];
            var c = new Catalog { Items = [a,b,two] }; var owner = new LoadoutProfile(); var build = new PetBuild { Items = [Entry(a,1),Entry(b,2)] }; var result = PetEquipmentInference.Build(build, owner, c); var session = new LoadoutEvaluationSession(c, owner, true, GearScorer.Defaults["Pet DPS"], result.Positions, result.Positions); var change = session.Compare(two,4,"PRIMARY:1");
            Check(change.Status == "Conditional" && change.Unbuffed.Delta == null && change.Replaced.Count == 2 && change.WeaponNote.Contains("ratios")); Check(change.EffectsGained.Count == 1);
        });
        test("Rank-sensitive cache and change identity reconcile repeated item comparisons", () => {
            var item = Item("ranked", "CHEST", new() { ["STR"] = 10 }); var c = new Catalog { Items = [item] }; var owner = new LoadoutProfile(); var build = new PetBuild { Items = [Entry(item,1)] }; var result = PetEquipmentInference.Build(build, owner, c); var session = new LoadoutEvaluationSession(c,owner,true,new() { ["STR"] = 1 },result.Positions,result.Positions);
            Check(session.Compare(item,1,"CHEST:1").Unbuffed.Delta == 1); Check(session.Compare(item,5,"CHEST:1").Unbuffed.Delta == 5); Check(session.Compare(item,1,"CHEST:1").Unbuffed.Delta == 1);
        });
        test("Pet preset migration is idempotent and preserves edited weights", () => {
            var p = new CharacterProfile { Weights = new() { ["Melee"] = new(GearScorer.Defaults["Melee"]) } }; p.ActiveLoadout.PetPreset = "Melee"; GearRatingMigration.Apply(p); Check(p.ActiveLoadout.PetPreset == "Pet DPS" && p.Weights["Pet DPS"]["AC"] == .25); GearRatingMigration.Apply(p); Check(p.ActiveLoadout.PetPreset == "Pet DPS");
            var custom = new CharacterProfile { Weights = new() { ["Melee"] = new() { ["DEX"] = 9 } } }; custom.ActiveLoadout.PetPreset = "Melee"; GearRatingMigration.Apply(custom); Check(custom.ActiveLoadout.PetPreset == "Melee" && custom.Weights["Melee"]["DEX"] == 9);
        });
        test("AA rank editing distinguishes unknown, verified limits and manual numeric models", () => {
            var aa = new GearAaModifier { Kind = AaModifierKind.FlatStat, Value = 8, Confirmed = true }; Check(AaRankEditing.Set(aa,1,out _) && aa.Rank == 1 && !aa.Confirmed && aa.Value == 8); Check(AaRankEditing.Set(aa,0,out _) && aa.Rank == 0); Check(AaRankEditing.Set(aa,null,out _) && aa.Rank == null);
            aa.VerifiedMaxRank = 3; Check(!AaRankEditing.Set(aa,4,out _) && aa.Rank == null); aa.VerifiedRankBonuses = new() { [2] = 10 }; aa.RankRuleSource = "Fixture verified rule"; Check(AaRankEditing.Set(aa,2,out _) && aa.Value == 10 && aa.Confirmed);
            aa.ValueManuallyOverridden = true; Check(AaRankEditing.Set(aa,3,out _) && aa.Value == 10); Check(!AaRankEditing.Set(aa,-1,out _));
        });
    }
}
