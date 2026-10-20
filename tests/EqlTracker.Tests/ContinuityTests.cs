using EqlTracker.Core;

internal static class ContinuityTests
{
    private static readonly DateTime Start = new(2026, 10, 14, 23, 0, 0);
    private static ParsedLine Parse(string text, int seconds = 0) => new LogParser("ExampleHero").Parse($"[{Start.AddSeconds(seconds):ddd MMM dd HH:mm:ss yyyy}] {text}", "continuity:" + seconds + ":" + text)!;
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Eq<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Expected {a}; got {b}"); }
    public static void Register(Action<string, Action> test)
    {
        test("Idle at twelve seconds retains a resumable encounter without idle tail", () =>
        {
            var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 100 points of damage."));
            e.Process(Parse("You slash a rat for 100 points of damage.", 3)); e.Tick(Start.AddSeconds(16));
            Check(e.Current!.IdleResumable); Eq(3d, e.Current.Duration); Eq(0, e.Encounters.Count);
            e.Tick(Start.AddSeconds(34)); Check(e.Current == null); Eq(3d, e.Encounters.Single().Duration);
        });
        test("Fear interruption resumes the same identity and includes the pause", () =>
        {
            var e = new CombatEngine(new()); e.Process(Parse("You slash a dracoliche for 100 points of damage.")); string id = e.Current!.Id;
            e.Tick(Start.AddSeconds(13)); e.Process(Parse("You slash a dracoliche for 200 points of damage.", 18));
            Eq(id, e.Current!.Id); Eq(1, e.Current.ResumeCount); Eq(18d, e.Current.Duration); Eq(300L, e.Current.Damage);
            Eq(300d / 18, DpsViews.Build([], e.Current, Start, new() { Actor = "Combined" }).Dps); Check(!e.Current.IdleResumable);
        });
        test("Same target after thirty seconds starts a new encounter", () =>
        { var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 10 points of damage.")); string id = e.Current!.Id; e.Process(Parse("You slash a rat for 20 points of damage.", 31)); Check(e.Current!.Id != id); Eq(1, e.Encounters.Count); });
        test("A different target after idle cannot resume the previous pull", () =>
        { var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 10 points of damage.")); e.Process(Parse("You slash a bat for 20 points of damage.", 16)); Eq(1, e.Encounters.Count); Eq(20L, e.Current!.Damage); });
        test("Logged target death prevents identical-name resumption", () =>
        { var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 10 points of damage.")); e.Process(Parse("a rat has been slain by ExampleHero!", 2)); e.Process(Parse("You slash a rat for 20 points of damage.", 18)); Eq(1, e.Encounters.Count); Eq(20L, e.Current!.Damage); });
        test("Incoming attack from an unresolved target can resume combat", () =>
        { var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 100 points of damage.")); e.Tick(Start.AddSeconds(13)); e.Process(Parse("a rat bites YOU for 10 points of damage.", 17)); Eq(0, e.Encounters.Count); Eq(100L, e.Current!.Damage); Eq(17d, e.Current.Duration); });
        test("Group continuation refreshes timing without crediting party damage", () =>
        {
            var p = new CharacterProfile(); var e = new CombatEngine(p);
            e.Process(Parse("You notify Naoko that you agree to join the group.")); e.Process(Parse("You have joined the group."));
            // Joining immediately after acceptance must preserve the inviter evidence.
            e.Process(Parse("You slash a rat for 100 points of damage.", 1));
            e.Process(Parse("Naoko slashes a rat for 500 points of damage.", 10)); e.Tick(Start.AddSeconds(20));
            Check(!e.Current!.IdleResumable); Eq(100L, e.Current.Damage); Eq(9d, e.Current.Duration);
        });
        test("Removal message clears group and active memberships", () =>
        { var p = new CharacterProfile(); var e = new CombatEngine(p); e.Process(Parse("Naoko has joined the group.")); e.Process(Parse("You have been removed from the group.", 2)); Check(!p.InGroup && !p.GroupRosterIncomplete); Check(p.GroupMembers.All(m => m.Until.HasValue)); });
        test("Target filtering ignores case while retaining original text", () =>
        { var f = new Encounter { Start = Start, End = Start.AddSeconds(10), Events = [new() { Id = "a", Owner = "Player", Target = "a rat", Time = Start, Damage = 10 }, new() { Id = "b", Owner = "Player", Target = "A RAT", Time = Start.AddSeconds(10), Damage = 20 }, new() { Id = "c", Owner = "Incoming", Target = "YOU", Time = Start, Damage = 90, ActivityOnly = true }] }; var v = DpsViews.Build([], f, Start, new() { Actor = "Combined", Target = "A Rat" }); Eq(30L, v.Damage); Eq(1, v.Targets.Count); Eq("a rat", f.Events[0].Target); });
        test("Snapshot events and death evidence cannot change under the UI", () =>
        { var f = new Encounter { Events = [new() { Owner = "Unknown", Damage = 10 }], ResolvedTargets = ["a rat"] }; var copy = CombatSnapshots.Copy(f); f.Events[0].Owner = "Pet"; f.ResolvedTargets.Clear(); Eq("Unknown", copy.Events[0].Owner); Check(copy.ResolvedTargets.Contains("a rat")); });
        test("Merge split and undo storage reconcile without duplicate events", () =>
        {
            var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 100 points of damage.")); e.Finish(); e.Process(Parse("You slash a rat for 200 points of damage.", 18)); e.Finish();
            var before = e.Encounters; var merged = EncounterEdits.Merge(before[0], before[1]); Eq(300L, merged.Damage); Eq(18d, merged.Duration);
            var parts = EncounterEdits.Split(merged, Start.AddSeconds(10)); Eq(300L, parts.Sum(f => f.Damage)); Eq(2, parts.Sum(f => f.Events.Count));
            string dir = Path.Combine(Path.GetTempPath(), "EQL-continuity-" + Guid.NewGuid().ToString("N"));
            using var store = new TrackerStore(Path.Combine(dir, "tracker.db")); foreach (var f in before) store.SaveEncounter(f);
            store.ReplaceEncounters(before.Select(f => f.Id), [merged], new(before, [merged.Id], "merge")); Eq(1, store.LoadEncounters().Count);
            var undo = store.Get<EncounterEditUndo>("encounterEditUndo")!; store.ReplaceEncounters(undo.AfterIds, undo.Before, null);
            Eq(2, store.LoadEncounters().Count); Eq(300L, store.LoadEncounters().Sum(f => f.Damage));
        });
        test("Resumable encounter survives storage and restart", () =>
        {
            var e = new CombatEngine(new()); e.Process(Parse("You slash a rat for 100 points of damage.")); e.Tick(Start.AddSeconds(14));
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<Encounter>(System.Text.Json.JsonSerializer.Serialize(e.Current))!;
            var restarted = new CombatEngine(new()); restarted.Restore([], snapshot); restarted.Process(Parse("You slash a rat for 50 points of damage.", 20));
            Eq(snapshot.Id, restarted.Current!.Id); Eq(150L, restarted.Current.Damage); Eq(20d, restarted.Current.Duration);
        });
        test("Synthetic interruption gaps retain one fight with exact totals", () =>
        {
            var e = new CombatEngine(new()); var parser = new LogParser("ExampleHero"); long expected = 0; int count = 0;
            foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "dragon-fear.txt")))
            {
                var parsed = parser.Parse(line, "dragon:" + count++)!;
                if (parsed.Combat is { } combat && (combat.Owner == "Player" || combat.Actor == "Mistling")) expected += combat.Damage;
                e.Process(parsed); e.Tick(parsed.Time);
            }
            Check(count > 100); e.Finish(); Eq(1, e.Encounters.Count); Eq(expected, e.Encounters.Single().Damage); Check(e.Encounters.Single().ResumeCount >= 2);
        });
        test("Synthetic archery attacks survive obsolete self-target filters", () =>
        {
            var p = new CharacterProfile(); p.Meter.Actor = "Combined"; p.Meter.Scope = "Session"; p.Meter.Target = "YOU";
            Check(MeterSelectionRepair.ClearIncomingSelfTarget(p.Meter, p.Character));
            var e = new CombatEngine(p); var parser = new LogParser(p.Character); int i = 0;
            foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "crushbone.txt")))
                if (parser.Parse(line, "crushbone:" + i++) is { } parsed) e.Process(parsed);
            e.Finish(); Eq(599L, e.Encounters.Sum(f => f.PlayerDamage)); Eq(153L, e.Encounters.Sum(f => f.PetDamage));
            var view = DpsViews.Build(e.Encounters, null, DateTime.MinValue, p.Meter); Eq(752L, view.Damage); Check(view.Targets.All(t => !CombatIdentity.Same(t.Target, "you")));
        });
        test("Self filter cleanup preserves intentional NPC filters", () =>
        { var selected = new DpsSelection { Target = "orc centurion" }; Check(!MeterSelectionRepair.ClearIncomingSelfTarget(selected, "ExampleHero")); Eq("orc centurion", selected.Target); });
        test("Environmental area messages do not reset a fight or zone", () =>
        { var parsed = Parse("You have entered an area where levitation effects do not function."); Check(!parsed.Boundary && parsed.Zone == null); });
        test("Clan Crushbone log alias matches the curated Crushbone sources", () => Eq(Identity.ZoneKey("Crushbone"), Identity.ZoneKey("Clan Crushbone")));
        test("Retrying a record is idempotent without removing identical real attacks", () =>
        {
            var e = new CombatEngine(new()); var parser = new LogParser("ExampleHero"); string text = $"[{Start:ddd MMM dd HH:mm:ss yyyy}] You slash a rat for 20 points of damage.";
            e.Process(parser.Parse(text, "file:100")!); e.Process(parser.Parse(text, "file:100")!); e.Process(parser.Parse(text, "file:200")!);
            Eq(40L, e.Current!.Damage); Eq(2, e.Current.Events.Count);
        });
        test("Oversized malformed line cannot stall the tailer indefinitely", () =>
        {
            string file = Path.Combine(Path.GetTempPath(), "EQL-oversized-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(file, new string('x', 1025) + "\nvalid\n"); FileCheckpoint? checkpoint = null; bool warning = false; var lines = new List<string>();
            for (int i = 0; i < 10; i++) { var batch = LogTailer.Read(file, checkpoint, 256); checkpoint = batch.Checkpoint; warning |= batch.Warning != null; lines.AddRange(batch.Lines.Select(l => l.Text)); if (batch.CaughtUp) break; }
            Check(warning); Check(lines.Contains("valid"));
        });
        test("Invalid damage never invents a zero-damage hit", () =>
        { var parsed = Parse("You slash a rat for 9999999999999999999999999 points of damage."); Check(parsed.Combat == null && parsed.ParseWarning != null); });
        test("EQL reave strike is parsed and attributed", () =>
        { var e = Parse("You reave a dracoliche for 2 points of damage.").Combat!; Eq("Player", e.Owner); Eq("reaves", e.Ability); Eq(2L, e.Damage); });
        test("Inventory identity index invalidates counts and rank merges", () =>
        { var item = new ItemDefinition { Name = "Sword", GameId = 10 }; var stock = new InventorySnapshot { CapturedAt = Start, Entries = [new() { Name = "Sword", GameId = 10, Count = 1, Location = "Bank1" }] }; Eq(1, stock.Count(item)); InventoryParser.Apply(stock, new(Start.AddSeconds(1), "Sword", "Looted", 2, "a rat")); Eq(3, stock.Count(item)); InventoryParser.Apply(stock, new(Start.AddSeconds(2), "Sword", "Destroyed", 2, "")); Eq(1, stock.Count(item)); });
    }
}
