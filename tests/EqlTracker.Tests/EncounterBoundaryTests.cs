using EqlTracker.Core;

internal static class EncounterBoundaryTests
{
    private static readonly DateTime Start = new(2026, 1, 6, 12, 0, 0);
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static ParsedLine Parse(string text, int seconds = 0) => new LogParser("ExampleHero").Parse($"[{Start.AddSeconds(seconds):ddd MMM dd HH:mm:ss yyyy}] {text}", "boundary:" + seconds + ":" + text)!;
    private static CombatEngine Engine()
    {
        var engine = new CombatEngine(new() { EncounterTimeoutSeconds = 10, EncounterResumeSeconds = 0 });
        engine.Process(Parse("Cloudling told you, 'Attacking a large rat Master.'"));
        return engine;
    }
    public static void Register(Action<string, Action> test)
    {
        test("Synthetic multi-target fights and loot reconcile after the resume window", () =>
        {
            var engine = new CombatEngine(new()); var parser = new LogParser("ExampleHero");
            var stock = new InventorySnapshot { CapturedAt = Start }; var loot = new List<ItemObservation>(); int index = 0;
            foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "two-fights.txt")))
            {
                var parsed = parser.Parse(line, "east-freeport:" + index++)!; engine.Process(parsed);
                if (parsed.Item is { } item) { loot.Add(item); InventoryParser.Apply(stock, item); }
            }
            engine.Tick(engine.Current!.End.AddSeconds(31)); Equal(2, engine.Encounters.Count);
            var session = DpsViews.Build(engine.Encounters, null, Start, new() { Scope = "Session", Actor = "Combined" });
            Equal(140L, session.Damage); Equal(19d, session.Seconds); Equal(140L, session.Rows.Sum(x => x.Damage));
            Equal(88L, engine.Encounters.Sum(x => x.PlayerDamage)); Equal(52L, engine.Encounters.Sum(x => x.PetDamage));
            var fight = DpsViews.Build(engine.Encounters, null, Start, new() { Actor = "Player" }); Equal(53L, fight.Damage); Equal(8d, fight.Seconds); Equal(6.625, fight.Dps);
            Equal(6, loot.Count); Equal(5, loot.Count(x => x.Action == "Sold")); Equal("Fire Beetle Eye", loot.Single(x => x.Action == "Looted").Name);
            Equal(1, stock.Entries.Count); Equal("Fire Beetle Eye", stock.Entries.Single().Name); Equal((int?)1, stock.Entries.Single().Count);
            Equal(19L, fight.Rows.Single(x => x.Ability == "bashes").MaxHit);
        });
        test("Normal decorated loot retains quantity and source", () =>
        {
            var item = Parse("--You have looted 2 Spiderling Silk from a spiderling's corpse.--").Item!;
            Equal("Looted", item.Action); Equal("Spiderling Silk", item.Name); Equal(2, item.Count); Equal("a spiderling", item.Source);
        });
        test("Default fight timeout is twelve seconds and excludes its idle tail", () =>
        {
            var profile = new CharacterProfile(); Equal(12, profile.EncounterTimeoutSeconds);
            var engine = new CombatEngine(profile);
            engine.Process(Parse("You slash a large rat for 100 points of damage."));
            engine.Process(Parse("You slash a large rat for 50 points of damage.", 3));
            engine.Tick(Start.AddSeconds(11)); Check(engine.Current != null);
            engine.Tick(Start.AddSeconds(16)); Check(engine.Current!.IdleResumable);
            engine.Tick(Start.AddSeconds(34)); Check(engine.Current == null);
            Equal(3d, engine.Encounters.Single().Duration);
        });
        test("Idle closure excludes the ten-second waiting tail", () =>
        {
            var engine = Engine();
            engine.Process(Parse("Cloudling slashes a large rat for 100 points of damage."));
            engine.Process(Parse("You slash a large rat for 50 points of damage.", 5));
            engine.Tick(Start.AddSeconds(15)); Check(engine.Current != null);
            engine.Tick(Start.AddSeconds(15.5)); Check(engine.Current == null);
            Equal(5d, engine.Encounters.Single().Duration);
            Equal(150L, engine.Encounters.Single().Damage);
        });
        test("Pet misses and incoming pet attacks refresh the fight timer", () =>
        {
            var engine = Engine();
            engine.Process(Parse("Cloudling slashes a large rat for 100 points of damage."));
            engine.Process(Parse("Cloudling tries to slash a large rat, but misses!", 9));
            engine.Process(Parse("a large rat bites Cloudling for 2 points of damage.", 18));
            engine.Tick(Start.AddSeconds(28)); Check(engine.Current != null);
            engine.Tick(Start.AddSeconds(28.5)); Check(engine.Current == null);
            Equal(18d, engine.Encounters.Single().Duration);
            Equal(100L, engine.Encounters.Single().PetDamage);
            Equal(1, CombatMetrics.Breakdown(engine.Encounters, new("Pet")).Single().Misses);
        });
        test("Buffs loot and unrelated fights do not extend our fight", () =>
        {
            var engine = Engine();
            engine.Process(Parse("You slash a large rat for 100 points of damage."));
            engine.Process(Parse("Your feet move faster.", 4));
            var loot = Parse("You looted a Rat Whiskers from a large rat's corpse.", 7);
            engine.Process(loot); Check(loot.Item != null);
            engine.Process(Parse("Someone slashes an orc pawn for 50 points of damage.", 9));
            engine.Tick(Start.AddSeconds(11)); Check(engine.Current == null);
            Equal(Start, engine.Encounters.Single().End);
            Equal(1d, engine.Encounters.Single().Duration);
        });
        test("Two fights reconcile with session totals and filtered DPS", () =>
        {
            var engine = Engine();
            engine.Process(Parse("You slash a large rat for 100 points of damage."));
            engine.Process(Parse("Cloudling slashes a large rat for 50 points of damage.", 2));
            engine.Process(Parse("You hit an orc pawn for 200 points of magic damage by Spirit Tap.", 15));
            engine.Process(Parse("Cloudling slashes an orc pawn for 150 points of damage.", 18));
            engine.Tick(Start.AddSeconds(29)); Equal(2, engine.Encounters.Count);
            var fight = DpsViews.Build(engine.Encounters, null, Start, new() { Actor = "Combined" });
            Equal(350L, fight.Damage); Equal(3d, fight.Seconds);
            var session = DpsViews.Build(engine.Encounters, null, Start, new() { Actor = "Combined", Scope = "Session" });
            Equal(500L, session.Damage); Equal(5d, session.Seconds); Equal(100d, session.Dps);
            Equal(40d, session.PetDps); Equal(60d, session.PlayerDps);
            Equal(session.Damage, session.Rows.Sum(x => x.Damage));
            var spell = DpsViews.Build(engine.Encounters, null, Start, new() { Actor = "Combined", Scope = "Session", Kind = "Spell" });
            Equal(200L, spell.Damage); Equal(5d, spell.Seconds); Equal(40d, spell.Dps);
        });
        test("Changing targets within the idle limit keeps a multi-target fight", () =>
        {
            var engine = Engine();
            engine.Process(Parse("You slash a large rat for 100 points of damage."));
            engine.Process(Parse("Cloudling slashes an orc pawn for 200 points of damage.", 8));
            engine.Tick(Start.AddSeconds(19)); Equal(1, engine.Encounters.Count);
            Equal(300L, engine.Encounters.Single().Damage);
            Equal(8d, engine.Encounters.Single().Duration);
        });
    }
}
