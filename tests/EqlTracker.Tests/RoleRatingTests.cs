using EqlTracker.Core;

internal static class RoleRatingTests
{
    internal static void Register(Action<string, Action> test)
    {
        void Check(bool value, string message = "Role rating assertion failed") { if (!value) throw new Exception(message); }
        var codes = new[] { "BRD", "ENC", "MAG", "NEC", "SHD", "WIZ", "BST", "CLR", "DRU", "PAL", "RNG", "SHM", "BER", "MNK", "ROG", "WAR" };
        for (int a = 0; a < codes.Length; a++) for (int b = a + 1; b < codes.Length; b++) for (int c = b + 1; c < codes.Length; c++)
        {
            string[] classes = [codes[a], codes[b], codes[c]];
            test("Class priority formula: " + string.Join('/', classes), () =>
            {
                var priority = CastingPriority.Resolve(classes); Check(priority.Complete);
                int i = classes.Count(x => CastingPriority.Family(x) == CastingFamily.Intelligence), w = classes.Count(x => CastingPriority.Family(x) == CastingFamily.Wisdom);
                Check(priority.IntelligenceShare == (i >= 2 ? 1 : w >= 2 ? 0 : i == 1 && w == 1 ? .5 : i == 1 ? 1 : 0));
                Check(priority.WisdomShare == (w >= 2 ? 1 : i >= 2 ? 0 : i == 1 && w == 1 ? .5 : w == 1 ? 1 : 0));
                Check(priority.IntelligenceShare + priority.WisdomShare == (i + w > 0 ? 1 : 0));
            });
        }
        test("Role aliases and malformed class sets never add votes", () =>
        {
            Check(CastingPriority.Resolve(["Shadowknight", "Shaman", "Bard"]) is { Complete: true, IntelligenceShare: 1 });
            foreach (var classes in new string[][] { [], ["MAG"], ["MAG", "CLR"], ["MAG", "MAG", "CLR"], ["MAG", "CLR", "Unknown"], ["MAG", "CLR", "WAR", "SHM"] }) Check(!CastingPriority.Resolve(classes).Complete);
        });
        test("Hybrid goals retain casting resources and explicit majority rationale", () =>
        {
            var p = new CharacterProfile(); var hybrid = new LoadoutProfile { Classes = ["SHD", "SHM", "BRD"] }; var basic = new LoadoutProfile { Classes = ["SHD", "MNK", "WAR"] };
            foreach (string goal in SelfRoleRatings.Goals)
            {
                var full = SelfRoleRatings.Resolve(p, hybrid, goal); var one = SelfRoleRatings.Resolve(p, basic, goal);
                Check(full.Complete && full.Weights["Mana"] > one.Weights["Mana"] && full.Weights["INT"] > one.Weights["INT"] && full.Weights["WIS"] == 0);
                Check(full.Weights["Endurance"] > 0 && full.Weights["Ratio"] > 0 && full.Weights["STR"] > 0 && !full.Weights.ContainsKey("Damage"));
                Check(full.Reasons.Any(r => r.Contains("not measured game multipliers")));
            }
            var mixed = SelfRoleRatings.Resolve(p, new() { Classes = ["MAG", "MNK", "CLR"] }, "Balanced"); Check(mixed.Weights["INT"] == mixed.Weights["WIS"] && mixed.Weights["INT"] > 0);
            var martial = SelfRoleRatings.Resolve(p, new() { Classes = ["WAR", "MNK", "ROG"] }, "Damage"); Check(martial.Weights["Mana"] == 0 && martial.Weights["INT"] == 0);
        });
        test("Self role migration preserves previous definitions, custom and all Pet weights", () =>
        {
            var p = new CharacterProfile { GearRatingVersion = 1, Weights = GearScorer.Defaults.ToDictionary(x => x.Key, x => new Dictionary<string, double>(x.Value)) };
            p.ActiveLoadout.Preset = "Balanced Tank"; string before = System.Text.Json.JsonSerializer.Serialize(p.Weights); GearRatingMigration.Apply(p);
            Check(p.ActiveLoadout.Preset == "Balanced" && p.ActiveLoadout.PreviousSelfRole == "Balanced Tank" && before == System.Text.Json.JsonSerializer.Serialize(p.Weights));
            p.ActiveLoadout.Preset = "Melee"; GearRatingMigration.Apply(p); Check(p.ActiveLoadout.Preset == "Damage");
            var custom = new CharacterProfile { GearRatingVersion = 1, Weights = new() { ["Caster"] = new() { ["WIS"] = 17 } } }; custom.ActiveLoadout.Preset = "Caster"; GearRatingMigration.Apply(custom); Check(custom.ActiveLoadout.Preset == "Custom Damage" && SelfRoleRatings.Resolve(custom, custom.ActiveLoadout).Weights["WIS"] == 17);
            Check(!SelfRoleRatings.Names(custom).Any(SelfRoleRatings.IsRetired));
            p.SelfRoleOverrides["Balanced"] = new() { ["STR"] = 7 }; var resolved = SelfRoleRatings.Resolve(p, p.ActiveLoadout, "Balanced"); Check(!resolved.Automatic && resolved.Complete && resolved.Weights["STR"] == 7);
            p.SelfRoleOverrides["My hybrid"] = new() { ["Mana"] = .4 }; Check(SelfRoleRatings.Names(p).Contains("My hybrid"));
            var read = System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>(System.Text.Json.JsonSerializer.Serialize(p, Json.Options), Json.Options)!; Check(read.SelfRoleOverrides["Balanced"]["STR"] == 7 && read.SelfRoleVersion == SelfRoleRatings.Version);
        });
        test("Named example reconciles 32.5, ordered gains and losses without usable wording", () =>
        {
            var old = new ItemDefinition { Key = "old", Name = "Gauntlets of Fiery Might", Classes = ["ALL"], Slots = ["HANDS"], MetadataComplete = true, Stats = new() { ["AC"] = 10, ["STR"] = 7, ["STA"] = 7, ["DEX"] = 7, ["ResistDisease"] = 7, ["ResistPoison"] = 7 } };
            var next = new ItemDefinition { Key = "new", Name = "Slime Blood of Cazic-Thule", Classes = ["ALL"], Slots = ["HANDS"], MetadataComplete = true, Stats = new() { ["AC"] = 16, ["HP"] = 20, ["Mana"] = 20, ["STR"] = 2, ["STA"] = 2, ["DEX"] = 2, ["AGI"] = 2, ["INT"] = 2, ["WIS"] = 2, ["ResistDisease"] = 2, ["ResistPoison"] = 2, ["ResistMagic"] = 2, ["ResistFire"] = 2, ["ResistCold"] = 2 } };
            var slot = new EquipmentPosition { Id = "HANDS:1", Slot = "Hands", Knowledge = EquipmentKnowledge.Occupied, Item = new() { Name = old.Name + " +4", ConfirmedItemKey = old.Key } };
            var owner = new LoadoutProfile { Classes = ["SHD", "SHM", "BRD"], Level = 50 };
            var catalog = new Catalog { Items = [old, next] }; var result = new LoadoutEvaluationSession(catalog, owner, false, new(GearScorer.Defaults["Balanced Tank"]), [slot], [slot]).Present(next, 5, slot.Id);
            var context = result.Evaluation!.Unbuffed; Check(context.Delta == 32.5 && Math.Abs(context.Changes.Sum(c => c.Points) - context.Delta.Value) < .0000001);
            Check(result.Candidate == next.Name + " +5" && result.Replaces.Contains(old.Name + " +4"));
            Check(RatingExplanation.Equation(context).EndsWith("= +32.5 points")); var str = context.Changes.Single(c => c.Stat == "STR"); Check(RatingExplanation.Change(str).StartsWith("Lose 4 STR") && !RatingExplanation.Change(str).Contains("usable"));
            var categories = RatingExplanation.Ordered(context.Changes).Select(c => ItemStatGroups.Category(c.Stat)).Distinct(); Check(categories.SequenceEqual(ItemStatGroups.Categories.Where(c=>c!="Weapons")));
            var role = SelfRoleRatings.Resolve(new(), owner, "Balanced"); var revised = new LoadoutEvaluationSession(catalog, owner, false, role.Weights, [slot], [slot], role: role).Compare(next, 5, slot.Id); Check(Math.Abs(revised.Unbuffed.Changes.Sum(c => c.Points) - revised.Unbuffed.Delta!.Value) < .0000001 && slot.Item.Rank == 4);
            var unresolved = SelfRoleRatings.Resolve(new(), new(), "Balanced"); Check(new LoadoutEvaluationSession(catalog, owner, false, unresolved.Weights, [slot], [slot], role: unresolved).Compare(next, 5, slot.Id).Unbuffed.Delta == null);
        });
        test("Raw Endurance parses only sourced fields and ranks with ordinary stats", () =>
        {
            var item = WikiClient.ParseItem("Documented gloves", "{{Itempage\n|itemname=Documented gloves\n|statsblock=Slot: HANDS<br>AC: 16<br>HP: +20 MANA: +20 END: +20<br>Class: ALL<br>Race: ALL}}", 123);
            Check(item.Stats["Endurance"] == 20 && GearScorer.EffectiveStats(item, 5)["Endurance"] == 30 && item.MetadataVersion == PetItemRules.MetadataVersion);
            var unknown = WikiClient.ParseItem("Unspecified gloves", "{{Itempage\n|itemname=Unspecified gloves\n|statsblock=Slot: HANDS<br>STR: +20<br>Class: ALL<br>Race: ALL}}", 124);
            Check(!unknown.Stats.ContainsKey("Endurance"));
        });
        test("Tooltip preferences are additive, finite presets and persist per character", () =>
        {
            var fresh = new CharacterProfile(); Check(fresh.Tooltips == TooltipLevel.Essential && fresh.TooltipDelayMilliseconds == 750 && TooltipPreferences.Delay(-1) == 750);
            foreach (int delay in TooltipPreferences.Delays) foreach (TooltipLevel level in Enum.GetValues<TooltipLevel>()) { fresh.Tooltips = level; fresh.TooltipDelayMilliseconds = delay; var read = System.Text.Json.JsonSerializer.Deserialize<CharacterProfile>(System.Text.Json.JsonSerializer.Serialize(fresh, Json.Options), Json.Options)!; Check(read.Tooltips == level && read.TooltipDelayMilliseconds == delay); }
        });
    }
}
