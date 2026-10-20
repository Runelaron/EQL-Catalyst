using EqlTracker.Core;

internal static class UsabilityTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool value, string text) { if (!value) throw new Exception(text); }
        test("Full stat comparison reconciles Tank points and unscored caster losses", () =>
        {
            var robe = new ItemDefinition { Key = "apothic robe", Name = "Apothic Robe", MetadataComplete = true, Slots = ["CHEST"], Classes = ["MAG"], Stats = new() { ["AC"] = 11, ["STA"] = 12, ["INT"] = 14, ["Mana"] = 40, ["DEX"] = 5 } };
            var armor = new ItemDefinition { Key = "red dragonscale armor", Name = "Red Dragonscale Armor", MetadataComplete = true, Slots = ["CHEST"], Classes = ["SHD"], Stats = new() { ["AC"] = 21, ["STR"] = 20, ["ResistFire"] = 10, ["ResistMagic"] = 10 } };
            var catalog = new Catalog { Items = [robe, armor] }; var loadout = new LoadoutProfile { Classes = ["MAG", "SHD"], Level = 50, Equipment = [new() { Name = "Apothic Robe +5", Location = "Chest", Count = 1 }] };
            var comparison = GearScorer.Compare(armor, 0, "CHEST", loadout, false, catalog, GearScorer.Defaults["Tank"]);
            Check(comparison.CanCompare && comparison.BaselineScore == 57 && comparison.Score == 63 && comparison.Delta == 6, "57 → 63 must remain +6 preference points");
            Check(comparison.Breakdown.Sum(s => s.Points) == comparison.Delta, "Contribution arithmetic must reconcile");
            Check(comparison.Breakdown.Single(s => s.Stat == "INT") is { Change: -21, Weight: 0 } && comparison.Breakdown.Single(s => s.Stat == "Mana").Change == -60, "Unweighted stat losses must remain visible");
            Check(comparison.Breakdown.Single(s => s.Stat == "STA").Points == -9 && comparison.Breakdown.Single(s => s.Stat == "AC").Points == 15, "Both positive and negative point contributions must be retained");
        });
        test("Healing chart respects filters, direction, timestamps and earliest tied peak", () =>
        {
            var time = new DateTime(2026, 10, 15, 12, 0, 0); var fight = new Encounter { Start = time, End = time.AddSeconds(10), HealingCoverage = "Complete" };
            fight.Heals = [new() { Id = "a", Time = time.AddSeconds(1), Healer = "Self", HealerOwner = "Player", Recipient = "Pet", RecipientOwner = "Pet", Effective = 100, Ability = "Tap" }, new() { Id = "b", Time = time.AddSeconds(3), Healer = "Self", HealerOwner = "Player", Recipient = "Pet", RecipientOwner = "Pet", Effective = 100, Ability = "Tap" }, new() { Id = "c", Time = time.AddSeconds(4), Healer = "Pet", HealerOwner = "Pet", Recipient = "Self", RecipientOwner = "Player", Effective = 50, Ability = "Regen", OverTime = true }];
            var done = HealingMetrics.Build([fight], [], new(Ability: "Tap"));
            Check(done.Chart.Unit == "HPS" && done.Chart.Self.Sum() * done.Chart.BinSeconds == 200 && done.Chart.Pet.Sum() == 0, "Only selected spells contribute to chart");
            Check(done.Chart.Peaks.Single().Time == time.AddSeconds(1), "Ties mark earliest sample");
            var received = HealingMetrics.Build([fight], [], new(Direction: "Received", Owner: "Pet"));
            Check(received.Chart.Pet.Sum() * received.Chart.BinSeconds == received.Effective && received.Chart.Self.Sum() == 0, "Received chart attributes by recipient ownership");
            var clipped = HealingMetrics.Build([fight], [], new(Ability: "Tap", From: time.AddSeconds(2), Until: time.AddSeconds(6)));
            Check(clipped.Effective == 100 && clipped.Chart.Duration == 4 && clipped.Hps == 25 && fight.Duration == 10, "Chart filters do not change encounter timing");
        });
        test("Legacy pet migration preserves ranks, effects and timestamp without inferring new stock", () =>
        {
            var time = DateTime.Now; var loadout = new LoadoutProfile { PetEquipmentConfirmedAt = time, PetEquipment = [new() { Name = "Helmet +4", Location = "Head", Count = 1, Augments = [new() { Name = "Source (Exaltation)", EffectText = "Focus", EffectIdentity = "fx" }] }] };
            var inventory = PetInventoryEditing.Confirmed(loadout, new());
            Check(inventory.Items.Single() is { Position: 1, Rank: 4 } && inventory.Items[0].ConfirmedAt == time && inventory.Items[0].Exaltations.Single().EffectId == "fx", "Migration preserves exact user evidence");
            Check(ReferenceEquals(inventory, PetInventoryEditing.Confirmed(loadout, new())), "Migration is idempotent");
        });
        test("Pet position collisions require explicit replacement and preserve planned separation", () =>
        {
            var build = new PetBuild { Items = [new() { ItemKey = "old", Name = "Old", Position = 1, Rank = 3 }] }; var value = new PetInventoryItem { ItemKey = "new", Name = "New", Position = 1 };
            Check(!PetInventoryEditing.Put(build, value, null, false, out _) && build.Items.Single().ItemKey == "old", "Occupied position cannot be lost silently");
            Check(PetInventoryEditing.Put(build, value, null, true, out _) && build.Items.Single().ItemKey == "new", "Explicit replacement succeeds once");
            Check(PetInventoryEditing.NextPosition(build) == 2, "New additions choose a free position");
        });
        test("Confirmed pet inventory retains inactive storage and projects ranked equipment only", () =>
        {
            var loadout = new LoadoutProfile { Classes = ["MAG"] }; var build = new PetBuild { Items = [new() { Name = "Sword", ItemKey = "sword", Position = 1, Slot = "Primary", Rank = 4 }, new() { Name = "Spare", ItemKey = "spare", Position = 2, Slot = "Storage" }, new() { Name = "Inactive", ItemKey = "inactive", Position = 20, Slot = "Head" }] };
            PetInventoryEditing.Confirm(loadout, build, DateTime.Now);
            Check(loadout.ConfirmedPetInventory!.Items.Count == 3 && loadout.PetEquipment.Single().Rank == 4, "Storage capacity and equipment are separate");
            Check(PetInventoryEditing.Has(loadout.ConfirmedPetInventory, new() { Key = "spare" }), "Confirmed stored items remain ownership evidence");
            build.Items[0].Rank = 9; Check(loadout.ConfirmedPetInventory.Items[0].Rank == 4 && loadout.PetEquipment[0].Rank == 4, "Confirming a plan creates an independent inventory snapshot");
        });
        test("Empty and filtered healing charts do not invent missing historical totals", () =>
        {
            var time = DateTime.Now; var fight = new Encounter { Start = time, End = time.AddSeconds(10), HealingDetailAvailable = false, HealingCoverage = "Unavailable" };
            var view = HealingMetrics.Build([fight], [], new());
            Check(!view.TotalsAvailable && view.Chart.Peaks.Count == 0 && view.Chart.Pet.All(x => x == 0), "Unavailable healing remains unavailable");
        });
    }
}
