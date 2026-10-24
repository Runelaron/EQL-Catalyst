using System.Text.Json;
using EqlTracker.Core;

internal static class PlacementEffectTests
{
    private static void Check(bool ok) { if (!ok) throw new Exception("Placement/effect assertion failed"); }
    private static (Catalog Catalog, LoadoutProfile Owner, PetBuild Build, ItemDefinition Weapon) Fixture()
    {
        var weapon = new ItemDefinition { Key = "sample weapon", Name = "Sample weapon", Slots = ["PRIMARY", "SECONDARY"], Classes = ["ALL"], Stats = new() { ["Damage"] = 10, ["Delay"] = 20 }, Effects = ["Focus: Catalog default", "Proc: Catalog strike"], MetadataComplete = true };
        var sources = new[] { "Focus", "Click", "Worn", "Proc", "Ornamentation" }.Select(kind => new ItemDefinition { Key = kind.ToLowerInvariant(), Name = kind, Classes = ["ALL"], Slots = ["PRIMARY", "SECONDARY", "CHEST"], MetadataComplete = true, Effects = [kind + ": Selected " + kind] }).ToList();
        var build = new PetBuild { SlotConfigurationVersion = PetSlotPlanning.Version, PetType = "Earth", PetLevel = 50, DualWield = true, Items = [new() { Id = "copy-one", ItemKey = weapon.Key, Name = weapon.Name, Position = 1, Slot = "Primary", Rank = 4 }] };
        return (new() { Items = [weapon, .. sources] }, new() { Classes = ["MAG", "BRD", "SHD"], Level = 50 }, build, weapon);
    }
    private static ExaltationAssignment Effect(string kind, string suffix = "") => new() { EffectId = kind.ToLowerInvariant() + suffix, SourceName = kind, Effect = kind + ": Selected " + kind + suffix, State = "Socketed" };
    internal static void Register(Action<string, Action> test)
    {
        test("Pet effects: catalog defaults remain reference data without a selected assignment", () => {
            var (c, o, b, w) = Fixture(); var eq = PetEquipmentInference.Build(b, o, c);
            Check(PetInventoryPresentation.Effects(b, b.Items[0], w, o, c, eq).Count == 0 && eq.Totals.Procs.Count == 0);
            Check(w.Effects.Count == 2 && b.Items[0].TransferredOutEffects.Count == 0 && b.Items[0].Exaltations.Count == 0);
        });
        test("Pet effects: weapons lead with Proc and armor keeps regular ordering", () => {
            var (c, o, b, w) = Fixture(); PetSlotPlanning.SetEffects(b, 1, new[] { Effect("Focus"), Effect("Click"), Effect("Worn"), Effect("Proc") }, "copy-one");
            var eq = PetEquipmentInference.Build(b, o, c); var item = b.Items[0];
            Check(PetInventoryPresentation.Effects(b, item, w, o, c, eq).Select(l => l.Kind).SequenceEqual(["Proc", "Focus", "Click", "Worn"]));
            var armor = new ItemDefinition { Key = "sample armor", Name = "Sample armor", Slots = ["CHEST"], Classes = ["ALL"], Stats = new() { ["AC"] = 5 }, MetadataComplete = true };
            Check(PetInventoryPresentation.Effects(b, item, armor, o, c, eq).Select(l => l.Kind).SequenceEqual(["Focus", "Click", "Worn", "Proc"]));
        });
        foreach (string kind in new[] { "Focus", "Click", "Worn", "Proc", "Ornamentation" })
            test("Pet effects: duplicate " + kind + " is retained but inactive", () => {
                var (c, o, b, w) = Fixture(); PetSlotPlanning.SetEffects(b, 1, new[] { Effect(kind), Effect(kind, "-second") }, "copy-one");
                string before = JsonSerializer.Serialize(b); var notes = new List<string>();
                Check(PetSlotPlanning.ActiveEffects(b, b.Items[0], w, "Primary", o, c, notes).Count == 0 && notes.Any(n => n.Contains("conflicting " + kind)));
                Check(JsonSerializer.Serialize(b) == before && b.SlotConfigurations[0].Effects.Count == 2);
                var lines = PetInventoryPresentation.Effects(b, b.Items[0], w, o, c, PetEquipmentInference.Build(b, o, c));
                Check(lines.Count == 2 && lines.All(l => !l.Active && l.Text.Contains("conflicting")));
            });
        test("Pet effects: one conflicting type does not disable a valid different type", () => {
            var (c, o, b, w) = Fixture(); PetSlotPlanning.SetEffects(b, 1, new[] { Effect("Focus"), Effect("Focus", "-second"), Effect("Proc") }, "copy-one");
            var active = PetSlotPlanning.ActiveEffects(b, b.Items[0], w, "Primary", o, c);
            Check(active.Count == 1 && active[0].EffectId == "proc");
        });
        test("Pet effects: assignment rejects occupied type and explicit replacement resolves conflict", () => {
            var (c, o, b, w) = Fixture(); var item = b.Items[0]; item.Exaltations = [Effect("Focus", "-old"), Effect("Focus", "-other")];
            var source = c.Find("Focus")!; var option = new ExaltationOption("focus-new", source, "Focus", source.Effects[0], 1, false, false);
            Check(!ExaltationPlanner.Assign(item, option, w, 4, "Primary", o, c, "Planned", false, out _) && item.Exaltations.Count == 2);
            Check(ExaltationPlanner.Assign(item, option, w, 4, "Primary", o, c, "Planned", true, out _) && item.Exaltations.Count == 1);
        });
        test("Pet effects: saving a conflicted slot is atomic and actionable", () => {
            var (_, _, b, _) = Fixture(); string before = JsonSerializer.Serialize(b); var proposed = EquipmentPositions.Copy(b.Items[0]); proposed.Exaltations = [Effect("Focus"), Effect("Focus", "-other")];
            Check(!PetInventoryEditing.Put(b, proposed, 1, false, out string error) && error.Contains("Choose one Focus") && JsonSerializer.Serialize(b) == before);
        });
        test("Pet effects: repeated observed identities are idempotent while disagreements remain reviewable", () => {
            var one = new InventoryEntry { Name = "Focus (Exaltation)", EffectIdentity = "focus", EffectText = "Focus: Selected Focus" };
            Check(PetEffectPolicy.Observed([one, one]).Count == 1);
            var two = EquipmentPositions.Copy(one); two.EffectIdentity = "other"; two.EffectText = "Focus: Different Focus";
            Check(PetEffectPolicy.Conflicts(PetEffectPolicy.Observed([one, two])).SetEquals(["Focus"]));
        });
        test("Pet effects: explicit socket type preserves unlabeled ornamentation and legacy fallback", () => {
            var one=new ExaltationAssignment{SocketType="Ornamentation",Effect="Appearance only"};var two=EquipmentPositions.Copy(one);
            Check(PetEffectPolicy.Conflicts([one,two]).SetEquals(["Ornamentation"]));
            var legacy=new ExaltationAssignment{Effect="Proc: Spark"};Check(PetEffectPolicy.Kind(legacy)=="Proc");
            Check(JsonSerializer.Deserialize<ExaltationAssignment>(JsonSerializer.Serialize(one))!.SocketType=="Ornamentation");
        });
        test("Pet effects: selected copy effects respect destination preview rank", () => {
            var copy=new InventoryEntry{Name="Sample +4",Augments=[new(){EffectText="Proc: Selected strike"}]};
            Check(PetEffectPolicy.SelectedTexts(copy).Count()==1&&PetEffectPolicy.SelectedTexts(copy,0).Count()==0);
        });
        test("Pet effects: rank locks and replacement do not inject catalog defaults", () => {
            var (c, o, b, w) = Fixture(); PetSlotPlanning.SetEffects(b, 1, [Effect("Proc")], "copy-one"); b.Items[0].Rank = 0;
            Check(PetSlotPlanning.ActiveEffects(b, b.Items[0], w, "Primary", o, c).Count == 0);
            Check(PetInventoryPresentation.Effects(b, b.Items[0], w, o, c, PetEquipmentInference.Build(b, o, c)).Single().Text.Contains("locked"));
            Check(b.SlotConfigurations[0].Effects.Count == 1 && w.Effects.Count == 2);
        });
        test("Pet effects: dormant Secondary and Undo retain explicit assignments", () => {
            var (c, _, b, w) = Fixture(); b.Items.Add(new() { Id = "copy-two", ItemKey = w.Key, Name = w.Name, Position = 2, Slot = "Secondary", Rank = 4 });
            PetSlotPlanning.SetEffects(b, 2, [Effect("Proc")], "copy-two"); PetBuildEditing.Remember(b, "Before toggle");
            PetSlotPlanning.Toggle(b, false, c, 7); Check(b.DormantSecondaryConfiguration?.Effects.Count == 1);
            PetSlotPlanning.Toggle(b, true, c, 7); Check(b.SlotConfigurations.Any(s => s.Position == 2 && s.Effects.Single().EffectId == "proc"));
        });
        test("Pet effects: comparison references and Self effects use separate policies", () => {
            var (c, o, _, w) = Fixture(); var item = new InventoryEntry { Name = w.Name, ConfirmedItemKey = w.Key };
            Check(ItemPairComparison.Create(w, item, w, 0, item, o, c, true).Candidate.Effects.Count == 0);
            Check(ItemPairComparison.Create(w, item, w, 0, item, o, c, false).Candidate.Effects.Count == 2);
        });
        test("Window placement: legacy records round-trip with optional placement fields", () => {
            var old = JsonSerializer.Deserialize<PanelState>("{\"Width\":915,\"Height\":800,\"Collapsed\":true}")!;
            Check(old.ScreenLeft == null && old.MonitorDevice == "" && old.Collapsed && old.Width == 915);
            old.ScreenLeft = -800; old.ScreenTop = 120; old.MonitorDevice = "Synthetic display"; old.DpiX = old.DpiY = 1.5;
            var restored = JsonSerializer.Deserialize<PanelState>(JsonSerializer.Serialize(old))!; Check(restored.ScreenLeft == -800 && restored.DpiX == 1.5 && restored.Collapsed);
        });
    }
}
