using EqlTracker.Core;

static class QuestAcquisitionTests
{
    public static void Register(Action<string, Action> test)
    {
        var c = Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data", "catalog.json"));
        static void Check(bool value) { if (!value) throw new Exception("Quest acquisition detail mismatch"); }
        static IEnumerable<QuestNode> Flatten(IEnumerable<QuestNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
        var war = c.Quests.Single(q => q.Class == "WAR" && q.Kind == "Epic"); var profile = new CharacterProfile();
        var nodes = Flatten(QuestHierarchy.Build(war, c, profile, new())).ToList();
        test("Warrior ingredient shows Permafrost sources without unrelated epic steps", () =>
        {
            var leaf = nodes.Single(n => n.ItemKeys.Contains("block of permafrost"));
            var detail = QuestAcquisition.Describe(leaf, war, profile);
            Check(detail.Kind == QuestNodeKind.Ingredient && detail.Steps.Count == 0 && detail.Prerequisites.Count == 0);
            Check(detail.ItemKeys.SequenceEqual(new[] { "block of permafrost" }) && c.FindKey(detail.ItemKeys[0])!.Sources.Any(s => s.Zone == "Permafrost"));
        });
        test("Warrior intermediate shows its own turn-in and three inputs only", () =>
        {
            var detail = QuestAcquisition.Describe(nodes.Single(n => n.ItemKeys.Contains("finely crafted dragon head hilt")), war, profile);
            Check(detail.Kind == QuestNodeKind.Step && detail.Steps.Single().Id == "war:3");
            Check(detail.Steps[0].Instructions.Contains("Wenden Blackhammer") && detail.Steps[0].Inputs.Count == 3);
            Check(detail.Prerequisites.Count == 0); // Rejesiam Ore is already a named input, not a second walkthrough.
        });
        test("Quest root retains full progression even when completed", () =>
        {
            var root = new QuestNode { Kind = QuestNodeKind.Quest, QuestId = war.Id, Status = "Completed" };
            var detail = QuestAcquisition.Describe(root, war, profile);
            Check(detail.Steps.Select(s => s.Id).SequenceEqual(QuestRecipes.Steps(war, profile).Select(s => s.Id)) && detail.ItemKeys.Contains(war.RewardKey));
        });
        test("All bundled hierarchies keep instructions local and owned ingredient sources available", () =>
        {
            foreach (var quest in c.Quests)
            foreach (var node in Flatten(QuestHierarchy.Build(quest, c, profile, new())))
            {
                var detail = QuestAcquisition.Describe(node, quest, profile);
                Check(detail.Steps.All(s => s.Id == node.StepId) && detail.Steps.Count <= 1);
                Check(detail.ItemKeys.SequenceEqual(node.ItemKeys));
                node.Status = "Covered by reward";
                var covered = QuestAcquisition.Describe(node, quest, profile);
                Check(covered.ItemKeys.SequenceEqual(detail.ItemKeys) && covered.Steps.SequenceEqual(detail.Steps));
            }
        });
        test("Only explicit local prerequisites appear, never the ingredient's consuming step", () =>
        {
            var q = new QuestDefinition { Steps = [new() { Id = "gate", Instructions = "Required access" }, new() { Id = "parent", Inputs = [new() { ItemKey = "leaf" }], Prerequisites = ["gate"] }] };
            var ingredient = QuestAcquisition.Describe(new() { Kind = QuestNodeKind.Ingredient, ItemKeys = ["leaf"] }, q, profile);
            var process = QuestAcquisition.Describe(new() { Kind = QuestNodeKind.Step, StepId = "parent" }, q, profile);
            Check(ingredient.Steps.Count == 0 && ingredient.Prerequisites.Count == 0 && process.Prerequisites.Single().Id == "gate");
        });
        test("Quest overview and intermediate details respect the selected alternative recipe", () =>
        {
            var q = c.Quests.First(q => q.Steps.Any(s => s.Optional && s.RouteGroup.Length > 0)); var p = new CharacterProfile();
            var alternate = q.Steps.First(s => s.Optional && s.RouteGroup.Length > 0); p.Routes[q.Id + "|" + alternate.RouteGroup] = alternate.Route;
            var detail = QuestAcquisition.Describe(new() { Kind = QuestNodeKind.Quest }, q, p);
            Check(detail.Steps.Contains(alternate) && detail.Steps.Where(s => s.RouteGroup == alternate.RouteGroup).All(s => s.Route == alternate.Route));
        });
    }
}
