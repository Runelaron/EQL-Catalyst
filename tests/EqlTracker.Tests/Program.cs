using System.Diagnostics;
using EqlTracker.Core;

if(args.Length==2&&args[0]=="--refresh-private-pet-buff-evidence"){PrivatePetBuffRefresh.Run(args[1]);return;}
if(args.Length==3&&args[0]=="--validate-private-pet-research")
{
    var state=Json.Read<PetResearchState>(args[1]);foreach(var sheet in state.Sheets)sheet.Validate();foreach(var buff in state.Buffs)PetResearch.NormalizeBuff(buff);
    var fights=Json.Read<List<Encounter>>(args[2]);PetResearch.Collect(state,fights,new());var copy=EquipmentPositions.Copy(state);
    if(copy.Samples.Values.Any(s=>s.Components.Values.Sum()!=s.Damage||s.BuffPeriodDamage.Values.Sum()!=s.Damage||Math.Abs(s.BuffPeriodSeconds.Values.Sum()-s.Seconds)>.0001))throw new InvalidDataException("Research totals did not reconcile");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Sheets=copy.Sheets.Count,Buffs=copy.Buffs.Count,Air=PetResearch.Summary(copy,"Air"),InvalidHP=copy.Sheets.Any(s=>s.Context.Pet=="Monster"&&s.Values["HP"]==null&&s.InvalidValues["HP"]==-1),ValidatedModels=copy.Models.Count(m=>m.Gate=="Validated locally")}));return;
}

if(args.Length==3&&args[0]=="--audit-inventory-replay"){InventoryReplayAudit.Run(args[1],args[2]);return;}

if (args.Length == 2 && args[0] == "--refresh-item-display") { await ItemDisplayRefresh.Run(args[1]); return; }
if (args.Length == 2 && args[0] == "--audit-item-display") { await ItemDisplayRefresh.Run(args[1], false); return; }

var tests = new List<(string Name, Action Run)>();
Beta17Tests.Register((name,run)=>tests.Add((name,run)));
WorkspaceToolbarTests.Register((name,run)=>tests.Add((name,run)));
UpdateTests.Register((name,run)=>tests.Add((name,run)));
ProgressRewardTests.Register((name,run)=>tests.Add((name,run)));
ProgressBrowseTests.Register((name,run)=>tests.Add((name,run)));
ProgressActionTests.Register((name,run)=>tests.Add((name,run)));
CompactPetTests.Register((name,run)=>tests.Add((name,run)));
CorrectPetRatingTests.Register((name,run)=>tests.Add((name,run)));
QuestPetSlotTests.Register((name,run)=>tests.Add((name,run)));
RoleRatingTests.Register((name,run)=>tests.Add((name,run)));
PetComparisonTests.Register((name,run)=>tests.Add((name,run)));
LivePetTests.Register((name,run)=>tests.Add((name,run)));
CompactSettingsTests.Register((name,run)=>tests.Add((name,run)));
CascadeWidthTests.Register((name,run)=>tests.Add((name,run)));
CharacterSelectionTests.Register((name,run)=>tests.Add((name,run)));
BetaTests.Register((name,run)=>tests.Add((name,run)));
GroupingTests.Register((name,run)=>tests.Add((name,run)));
TrackerPolishTests.Register((name,run)=>tests.Add((name,run)));
ItemInspectionTests.Register((name, run) => tests.Add((name, run)));
ClearItemTests.Register((name, run) => tests.Add((name, run)));
EquipmentIconTests.Register((name, run) => tests.Add((name, run)));
GearControlsTests.Register((name, run) => tests.Add((name, run)));
void Test(string name, Action run) => tests.Add((name, run));
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
var catalog = Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory, "data", "catalog.json"));
var parser = new LogParser("ExampleHero");
string Line(string text, int seconds = 0) => $"[Tue Oct 13 21:00:{seconds:00} 2026] {text}";
InventorySnapshot Stock(params (string Name, int Count)[] values) => new() { Entries = values.Select(x => new InventoryEntry { Name = x.Name, GameId = catalog.Find(x.Name)?.GameId ?? 0, Count = x.Count, Location = "Bank1" }).ToList() };
CharacterProfile Warrior() => new() { CurrentClasses = [], OptionalClasses = ["WAR"] };

Test("Catalog class coverage and attribution", () => { Equal(16, catalog.Quests.Count(q => q.Kind == "Epic")); Assert(catalog.Quests.Where(x => x.Kind=="Epic"&&x.Class != "BST").All(x => x.Requirements.Count > 0)); Assert(catalog.Quests.Where(x=>x.Class=="ALL").All(x=>x.Steps.Count>0)); Assert(catalog.Quests.Single(x => x.Class == "BST").Coverage.Contains("missing")); Assert(catalog.Documents.Count > 800); Assert(catalog.Items.All(x => x.Key.Length > 0)); Equal(catalog.Items.Count, catalog.Items.Select(x => x.Key).Distinct().Count()); });
Test("Canonical rank and Exaltation identity", () => { Equal("Hand of the Maestro", Identity.Canonical("Hand of the Maestro +6")); Equal("Sword", Identity.Canonical("Sword (Exaltation)")); Equal(6, Identity.Rank("Sword +6")); Assert(Identity.IsAugment("Sword (Exaltation)")); });
Test("Inventory distinguishes keyring presence and stock", () =>
{
    var text = "Location\tName\tID\tCount\tSlots\nGeneral 1\tSpiroc Wingblade +1\t20679\t1\t10\nGeneral 1-Slot7\tSpiroc Wingblade (Exaltation)\t20679\t1\t10\nSharedBank1\tEmpty\t0\t0\t0\n\nKeyRing\tName\tID\t\nEquipment\tSpiroc Wingblade +1\t20679\n";
    var snap = InventoryParser.Parse(text, DateTime.Now); Assert(snap.Complete); Equal(1, snap.Count(catalog.Find("Spiroc Wingblade")!)); Equal(1, snap.Entries[0].Augments.Count); Assert(snap.Entries.Last().Count == null);
});
Test("Partial export is rejected", () => Assert(!InventoryParser.Parse("Location\tName\tID\tCount\tSlots\nGeneral 1\tDiamond\t10037\t1\t10\n", DateTime.Now).Complete));
Test("Invalid export fails visibly", () => { try { InventoryParser.Parse("not inventory", DateTime.Now); throw new Exception("Accepted invalid data"); } catch (InvalidDataException) { } });
Test("Collection proves presence without inflating copies", () => { var snap = new InventorySnapshot { Entries = [new() { Name = "Spiroc Wingblade +1", GameId = 20679, Location = "Equipment", Count = null }, new() { Name = "Spiroc Wingblade +1", GameId = 20679, Location = "Equipment", Count = null }] }; Equal(1, snap.Count(catalog.Find("Spiroc Wingblade")!)); });
Test("Ambiguous names never pick a random item", () => { var c = new Catalog { Items = [new() { Key = "pipe1", Name = "A Metal Pipe", GameId = 1 }, new() { Key = "pipe2", Name = "A Metal Pipe", GameId = 2 }] }; Assert(c.Find("A Metal Pipe") == null); Equal("pipe2", c.Find("A Metal Pipe", 2)!.Key); });
Test("Permafrost acceptance objectives", () => { var rows = QuestPlanner.Plan(catalog, Warrior(), Stock()); var local = rows.Where(x => QuestPlanner.InZone(x, "Permafrost", "Classic")).ToList(); Assert(local.Any(x => x.Item == "Heart of Frost")); Assert(local.Any(x => x.Item == "Block of Permafrost")); Assert(!local.Any(x => x.Item == "Ancient Blade")); });
Test("Owned intermediate suppresses old ingredients", () => { var rows = QuestPlanner.Plan(catalog, Warrior(), Stock(("Jeweled Dragon Head Hilt", 1))); Assert(!rows.Any(x => x.Item is "Diamond" or "Jacinth" or "Black Sapphire" or "Unjeweled Dragon Head Hilt")); });
Test("Confirmed turn-in is reversible", () => { var p = Warrior(); p.CompletedSteps.Add("war:1"); Assert(!QuestPlanner.Plan(catalog, p, Stock()).Any(x => x.Item == "Diamond")); p.CompletedSteps.Clear(); Assert(QuestPlanner.Plan(catalog, p, Stock()).Any(x => x.Item == "Diamond")); });
Test("Completed epic suppresses its objectives", () => Assert(QuestPlanner.Plan(catalog, Warrior(), Stock(("Jagged Blade of War", 1))).Count == 0));
Test("Shared quantities are not counted twice", () => { var p = new CharacterProfile { CurrentClasses = ["BRD"], OptionalClasses = ["WAR"] }; var rows = QuestPlanner.Plan(catalog, p, Stock(("Red Dragon Scales", 1))).Where(x => x.Item == "Red Dragon Scales").ToList(); Equal(2, rows.Count); Equal(1, rows.Sum(x => x.Owned)); Equal(1, rows.Sum(x => x.Remaining)); Equal(1, rows.Single(x => x.Class == "BRD").Owned); });
Test("User priority changes allocation", () => { var p = new CharacterProfile { CurrentClasses = ["BRD"], OptionalClasses = ["WAR"], GoalPriority = ["WAR", "BRD"] }; var rows = QuestPlanner.Plan(catalog, p, Stock(("Red Dragon Scales", 1))); Equal(1, rows.Single(x => x.Item == "Red Dragon Scales" && x.Class == "WAR").Owned); });
Test("No absence implies a turn-in", () => Assert(QuestPlanner.Plan(catalog, Warrior(), Stock()).Any(x => x.Item == "Heart of Frost" && x.Remaining == 1)));
Test("Roster parser only trusts the character", () => { var line = parser.Parse(Line("[50 SHD/BRD/MAG] ExampleHero (High Elf) ZONE: Permafrost")); Assert(line!.RosterObserved); Equal(3, line.Classes!.Count); Assert(parser.Parse(Line("[50 WAR/CLR/SHM] Somebody (Human)"))!.Classes == null); });
Test("Pet message and spell parsing", () => { Equal("Mistling", parser.Parse(Line("Mistling told you, 'Attacking an ice giant Master.'"))!.PetName); var e = parser.Parse(Line("Mistling hit an ice giant for 84 points of cold damage by Frost Strike. (Critical)"))!.Combat!; Equal("Spell", e.Kind); Equal("Frost Strike", e.Ability); Assert(e.Critical); Equal(84L, e.Damage); });
Test("DoT attribution", () => { var e = parser.Parse(Line("an ice giant has taken 20 damage from ExampleHero by Heat Blood."))!.Combat!; Equal("DoT", e.Kind); Equal("Player", e.Owner); });
Test("Incoming generic non-melee is not pet damage", () => Assert(parser.Parse(Line("You were hit by non-melee for 376 damage."))!.Combat == null));
Test("Strike kinds are preserved", () => { foreach (var verb in new[] { "cleaves", "backstabs", "kicks", "strikes", "slashes", "flying kicks" }) Equal(verb, parser.Parse(Line($"Mistling {verb} an ice giant for 82 points of damage."))!.Combat!.Ability); });
Test("Misses are retained", () => { var e = parser.Parse(Line("Mistling tries to slash an ice giant, but misses!"))!.Combat!; Equal(0L, e.Damage); Equal("misses", e.Outcome); });
Test("Loot, sales, destruction, merges", () => { Equal("Sold", parser.Parse(Line("You looted a Diamond from an ice giant's corpse and sold it for 100 platinum."))!.Item!.Action); var merge = parser.Parse(Line("You looted a Sword from an ice giant's corpse to create a Sword +4"))!.Item!; Equal("Merged", merge.Action); Equal("Sword +4", merge.Result); Equal(2, parser.Parse(Line("You successfully destroyed 2 Scare Straw."))!.Item!.Count); });
Test("Log estimates preserve sales and merges", () => { var date = DateTime.Now; var stock = Stock(("Diamond", 3)); stock.CapturedAt = date; InventoryParser.Apply(stock, new(date.AddMinutes(1), "Diamond", "Sold", 1, "")); Equal(3, stock.Count(catalog.Find("Diamond")!)); InventoryParser.Apply(stock, new(date.AddMinutes(2), "Diamond", "Destroyed", 2, "")); Equal(1, stock.Count(catalog.Find("Diamond")!)); InventoryParser.Apply(stock, new(date.AddMinutes(3), "Diamond", "Looted", 2, "")); Equal(3, stock.Count(catalog.Find("Diamond")!)); });
Test("Events before export do not double count", () => { var date = DateTime.Now; var stock = Stock(("Diamond", 3)); stock.CapturedAt = date; InventoryParser.Apply(stock, new(date, "Diamond", "Looted", 1, "")); Equal(3, stock.Count(catalog.Find("Diamond")!)); });
Test("Identical same-second attacks remain distinct", () =>
{
    var p = new CharacterProfile(); var engine = new CombatEngine(p); engine.Process(parser.Parse(Line("Mistling told you, 'Attacking an ice giant Master.'"))!);
    engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 100 points of damage."), "a")!); engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 100 points of damage."), "b")!);
    Equal(2, engine.Current!.Events.Count); Equal(200L, engine.Current.PetDamage);
});
Test("Encounter boundaries and common DPS denominator", () =>
{
    var p = new CharacterProfile(); var engine = new CombatEngine(p); engine.Process(parser.Parse(Line("Mistling told you, 'Attacking an ice giant Master.'"))!);
    engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 100 points of damage."), "a")!);
    engine.Process(parser.Parse(Line("You hit an ice giant for 200 points of magic damage by Spirit Tap.", 5), "b")!);
    engine.Process(parser.Parse(Line("Mistling backstabs an ice giant for 300 points of damage. (Critical)", 10), "c")!);
    var rows = CombatMetrics.Breakdown([engine.Current!], new("Combined")); Equal(60d, rows.Sum(x => x.Dps)); Equal(10d, engine.Current!.Duration);
    Equal(40d, CombatMetrics.Breakdown([engine.Current], new("Pet")).Sum(x => x.Dps));
    engine.Tick(engine.Current.End.AddSeconds(31)); Equal(1, engine.Encounters.Count); Assert(engine.Current == null);
});
Test("Pet ownership changes over time", () => { var p = new CharacterProfile(); var engine = new CombatEngine(p); engine.Process(parser.Parse(Line("Mistling told you, 'Attacking an ice giant Master.'"))!); engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 100 points of damage.", 1), "a")!); engine.Process(parser.Parse(Line("Cloudling told you, 'Attacking an ice giant Master.'", 4))!); engine.Process(parser.Parse(Line("Cloudling slashes an ice giant for 90 points of damage.", 5), "b")!); Equal(190L, engine.Current!.PetDamage); Assert(p.PetIdentities[0].Until.HasValue); });
Test("Late pet message corrects related ambiguous damage", () => { var engine = new CombatEngine(new()); engine.Process(parser.Parse(Line("You slashes an ice giant for 1 points of damage."), "a")!); engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 100 points of damage.", 1), "b")!); Equal(100L, engine.Current!.UnknownDamage); engine.Process(parser.Parse(Line("Mistling told you, 'Attacking an ice giant Master.'", 2))!); Equal(100L, engine.Current.PetDamage); Equal(0L, engine.Current.UnknownDamage); });
Test("Level 50 requires a known roster", () => { var p = new CharacterProfile(); var engine = new CombatEngine(p); engine.Process(parser.Parse(Line("You have gained a level! Welcome to level 50!"))!); Equal(0, p.Level50Classes.Count); engine.Process(parser.Parse(Line("[50 SHD/BRD/MAG] ExampleHero (High Elf)"))!); Equal(3, p.Level50Classes.Count); });
Test("Pet equipment uses pet plus owner's classes", () => { var item = new ItemDefinition { Name = "Rogue blade", Slots = ["PRIMARY"], Classes = ["ROG"], MetadataComplete = true }; var p = new LoadoutProfile { PetType = "Water" }; Equal("Eligible", GearScorer.Eligibility(item, p, true, null, catalog)); Equal("Requires another class / pet", GearScorer.Eligibility(item, p, false, null, catalog)); p.PetType = "Air"; Equal("Requires another class / pet", GearScorer.Eligibility(item, p, true, null, catalog)); });
Test("Two-hand replacement removes secondary baseline", () => { var c = new Catalog { Items = [new() { Key = "one", Name = "One", Slots = ["PRIMARY", "SECONDARY"], Classes = ["ALL"], Stats = new() { ["ATK"] = 10 }, MetadataComplete = true }, new() { Key = "two", Name = "Two", Slots = ["PRIMARY"], Classes = ["ALL"], Stats = new() { ["ATK"] = 30 }, TwoHanded = true, MetadataComplete = true }] }; var p = new LoadoutProfile { Equipment = [new() { Location = "Primary", Name = "One" }, new() { Location = "Secondary", Name = "One" }] }; var r = GearScorer.Compare(c.Items[1], 0, "PRIMARY", p, false, c, new() { ["ATK"] = 1 }); Equal(20d, r.BaselineScore); Equal(10d, r.Delta); });
Test("Exaltations restrict candidate slots", () => { var c = new Catalog { Items = [new() { Key = "blade", Name = "Blade", Slots = ["PRIMARY", "SECONDARY"], Classes = ["ALL"], MetadataComplete = true }, new() { Key = "proc", Name = "Proc", Slots = ["PRIMARY"], Classes = ["WAR"], MetadataComplete = true }] }; var p = new LoadoutProfile { Classes = ["WAR"] }; var r = GearScorer.Compare(c.Items[0], 0, "SECONDARY", p, false, c, new(), [new() { Name = "Proc (Exaltation)" }]); Equal("Slot restricted by Exaltation", r.Eligibility); });
Test("Paired slots compare against lower stat score", () => { var c = new Catalog { Items = [new() { Name = "Low", Stats = new() { ["HP"] = 10 }, MetadataComplete = true }, new() { Name = "High", Stats = new() { ["HP"] = 30 }, MetadataComplete = true }] }; var p = new LoadoutProfile { Equipment = [new() { Location = "Ear", Name = "Low" }, new() { Location = "Ear", Name = "High" }] }; var r = GearScorer.Compare(new() { Name = "Candidate", Slots = ["EAR"], Classes = ["ALL"], Stats = new() { ["HP"] = 20 }, MetadataComplete = true }, 0, "EAR", p, false, c, new() { ["HP"] = 1 }); Equal(10d, r.Delta); });
Test("Ranked weapons use adopted 10% damage tiers independently of ordinary minimums", () => { var s = GearScorer.EffectiveStats(new() { Stats = new() { ["Damage"] = 20, ["Delay"] = 40, ["Ratio"] = .5, ["HP"] = 20 } }, 5); Equal(.75, s["Ratio"]); Equal(30d, s["Damage"]); Equal(40d, s["Delay"]); Equal(30d, s["HP"]); });
Test("Wiki parser retains stats and sources", () => { var item = WikiClient.ParseItem("Blade", "{{Itempage\n|itemname=Blade\n|statsblock=Slot: PRIMARY SECONDARY<br>\nSkill: 1H Slashing Atk Delay: 20<br>\nDMG: 10 AC: 5<br>\nClass: WAR ROG<br>\nRace: ALL<br>\n|dropsfrom=\n[[Permafrost]]\n* [[an ice giant]]\n}}", 1); Equal(.5, item.Stats["Ratio"]); Assert(item.MetadataComplete); Equal("Permafrost", item.Sources[0].Zone); });
Test("Tailer buffers partial lines and preserves duplicates", () => { var dir = Path.Combine(Path.GetTempPath(), "EQLTracker-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "log"); File.WriteAllText(path, "same\nsame\npartial"); var batch = LogTailer.Read(path, null); Equal(2, batch.Lines.Count); Assert(batch.Lines[0].Id != batch.Lines[1].Id); File.AppendAllText(path, " end\n"); var next = LogTailer.Read(path, batch.Checkpoint); Equal("partial end", next.Lines[0].Text); var empty = LogTailer.Read(path, next.Checkpoint); Equal(0, empty.Lines.Count); File.WriteAllText(path, "new\n"); var reset = LogTailer.Read(path, next.Checkpoint); Assert(reset.Reset); Equal("new", reset.Lines[0].Text); });
Test("SQLite checkpoints and encounter detail survive restart", () => { var dir = Path.Combine(Path.GetTempPath(), "EQLTracker-db-" + Guid.NewGuid()); var path = Path.Combine(dir, "tracker.db"); var encounter = new Encounter { Finished = true, Start = DateTime.Now, End = DateTime.Now.AddSeconds(5), PlayerDamage = 42, Events = [new() { Id = "offset:1", Time = DateTime.Now, Owner = "Player", Damage = 42, Ability = "slashes" }] }; using (var store = new TrackerStore(path)) { store.Commit(new() { Offset = 25 }, new(), new(), [encounter], null, []); } using (var store = new TrackerStore(path)) { Equal(25L, store.Get<FileCheckpoint>("checkpoint")!.Offset); Equal(42L, store.LoadEncounters()[0].Events[0].Damage); store.Retain(DateTime.Now.AddDays(1)); var summary = store.LoadEncounters()[0]; Equal(42L, summary.PlayerDamage); Assert(!summary.DetailAvailable); } });
Test("Synthetic inventory export parses", () => { var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "inventory.txt"); var snapshot = InventoryParser.Parse(File.ReadAllText(path), DateTime.Now); Assert(snapshot.Complete, snapshot.Warning); Equal(4, snapshot.Count(catalog.Find("Black Sapphire")!)); Assert(snapshot.Entries.Any(x => x.Name == "Hand of the Maestro +6" && x.IsCollection)); Assert(snapshot.Entries.Any(x => x.IsAugment)); });
Test("Player first-person melee grammar", () => Equal(35L, parser.Parse(Line("You bash an elemental warrior for 35 points of damage."))!.Combat!.Damage));
Test("Synthetic log fixture parses and aggregates", () => { var engine = new CombatEngine(new()); int recognized = 0; foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "combat.txt"))) if (parser.Parse(line, "real:" + recognized) is { } parsed) { engine.Process(parsed); if (parsed.Combat != null) recognized++; } engine.Finish(); Assert(recognized > 50); Assert(engine.Encounters.Sum(x => x.PetDamage) > 0); foreach (var fight in engine.Encounters) Equal(fight.Damage, CombatMetrics.Breakdown([fight], new("Combined")).Sum(x => x.Damage)); });
Test("Monk pipes remain distinct and source-aware", () => { var p = new CharacterProfile { CurrentClasses = [], OptionalClasses = ["MNK"], Era = "Kunark" }; var stock = Stock(("A Metal Pipe", 1)); var rows = QuestPlanner.Plan(catalog, p, stock).Where(x => x.Item.StartsWith("A Metal Pipe (")).ToList(); Equal(2, rows.Count); Equal(0, rows.Sum(x => x.Owned)); stock.Entries.Add(new() { Name = "A Metal Pipe", Count = 1, AcquiredFrom = "an iksar betrayer" }); Equal(1, QuestPlanner.Plan(catalog, p, stock).Where(x => x.Item.StartsWith("A Metal Pipe (")).Sum(x => x.Owned)); });
Test("Distinct-source loot does not merge pipe provenance", () => { var snapshot = new InventorySnapshot { CapturedAt = DateTime.Now }; InventoryParser.Apply(snapshot, new(DateTime.Now.AddSeconds(1), "A Metal Pipe", "Looted", 1, "an iksar betrayer")); InventoryParser.Apply(snapshot, new(DateTime.Now.AddSeconds(2), "A Metal Pipe", "Looted", 1, "a drolvarg pawbuster")); Equal(2, snapshot.Entries.Count); });
Test("Warrior dual-wield conversion counts as epic ownership", () => Assert(QuestPlanner.Plan(catalog, Warrior(), Stock(("Blade of Strategy", 1), ("Blade of Tactics", 1))).Count == 0));
Test("Classic prep preserves unavailable and unverified claims", () => { var item = catalog.Find("Blue Crystal Staff")!; Assert(item.Sources.Any(x => x.Availability == "Unavailable")); Assert(catalog.Items.Any(x => x.Sources.Any(s => s.Availability == "Unverified"))); });
Test("Late-section truncation keeps previous inventory usable", () => Assert(!InventoryParser.Parse("Location\tName\tID\tCount\tSlots\nSharedBank1\tEmpty\t0\t0\t0\n", DateTime.Now).Complete));
Test("Incoming combat extends duration without adding outgoing damage", () => { var engine = new CombatEngine(new() { EncounterTimeoutSeconds = 10 }); engine.Process(parser.Parse(Line("You slash an ice giant for 100 points of damage."), "a")!); engine.Process(parser.Parse(Line("an ice giant crushes YOU for 20 points of damage.", 10), "b")!); Equal(100L, engine.Current!.PlayerDamage); Equal(10d, engine.Current.Duration); engine.Tick(engine.Current.End.AddSeconds(5)); Assert(engine.Current != null); });
Test("Date-filtered DPS uses clipped wall-clock duration", () => { var start = new DateTime(2026, 10, 13, 21, 0, 0); var fight = new Encounter { Start = start, End = start.AddSeconds(20), Events = [new() { Owner = "Pet", Damage = 100, Time = start.AddSeconds(10), Ability = "kicks" }, new() { Owner = "Pet", Damage = 200, Time = start.AddSeconds(15), Ability = "slashes" }] }; Equal(30d, CombatMetrics.Breakdown([fight], new("Pet", From: start.AddSeconds(10), Until: start.AddSeconds(20))).Sum(x => x.Dps)); });
Test("Every quest reference resolves to a catalog item", () => Assert(catalog.Quests.SelectMany(x => x.Requirements.Concat(x.Steps.SelectMany(s => s.Inputs.Concat(s.Outputs)))).All(r => catalog.Items.Any(i => i.Key == r.ItemKey))));
Test("Player/pet strike grammar groups consistently", () => { Equal("slashes", parser.Parse(Line("You slash an ice giant for 10 points of damage."))!.Combat!.Ability); Equal("flying kicks", parser.Parse(Line("Mistling tries to flying kick an ice giant, but misses!"))!.Combat!.Ability); });
Test("Pet death ends attribution until a new ownership message", () => { var p = new CharacterProfile(); var engine = new CombatEngine(p); engine.Process(parser.Parse(Line("Mistling told you, 'Attacking an ice giant Master.'"))!); engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 100 points of damage.", 1), "a")!); engine.Process(parser.Parse(Line("Mistling has been slain by an ice giant!", 2))!); engine.Process(parser.Parse(Line("Mistling slashes an ice giant for 50 points of damage.", 3), "b")!); Equal(100L, engine.Current!.PetDamage); Equal(50L, engine.Current.UnknownDamage); });
Test("Stable step IDs and prerequisites are unique and valid", () => { foreach (var quest in catalog.Quests) { Equal(quest.Steps.Count, quest.Steps.Select(x => x.Id).Distinct().Count()); Assert(quest.Steps.SelectMany(x => x.Prerequisites).All(id => quest.Steps.Any(s => s.Id == id))); } });
Test("Refresh preserves level/deity/resistance restrictions", () => { var item = WikiClient.ParseItem("Guard", "{{Itempage\n|itemname=Guard\n|statsblock=Slot: CHEST<br>\nAC: 10<br>\nSV MAGIC: +8<br>\nRequired Level: 50<br>\nClass: WAR<br>\nRace: ALL<br>\nDeity: Rallos Zek<br>\n}}", 4); Equal(50, item.RequiredLevel); Equal("Rallos Zek", item.Deity); Equal(8d, item.Stats["ResistMagic"]); });

AdditionalTests.Register(Test);
Beta16Tests.Register(Test);
EncounterBoundaryTests.Register(Test);
AdvancedTrackerTests.Register(Test);
RefinementTests.Register(Test);
ContinuityTests.Register(Test);
HealingTests.Register(Test);
LiveAppearanceTests.Register(Test);
PlanningTests.Register(Test);
ClassicRecipeTests.Register(Test);
QuestAcquisitionTests.Register(Test);
GearBuildTests.Register(Test);
StatEvidenceTests.Register(Test);
UsabilityTests.Register(Test);
DirectPetEditingTests.Register(Test);
PetUpgradeTests.Register(Test);
PetControlsTests.Register(Test);
CombatInspectionTests.Register(Test);
SessionTests.Register(Test);
IconAppearanceTests.Register(Test);
PetDetailsTests.Register(Test);
PetFlowTests.Register(Test);
PlacementEffectTests.Register(Test);
LocalIterationTests.Register(Test);
OctoberContentTests.Register(Test);
IndependentDisplayTests.Register(Test);
PetResearchTests.Register(Test);
PetDetailsPopupTests.Register(Test);
PetWorkspaceTests.Register((name,run)=>tests.Add((name,run)));
PetContributionTests.Register(Test);
StandardPetBuffTests.Register(Test);
OverviewTests.Register(Test);
IntegratedCompanionTests.Register(Test);
BardCompatibilityAcceptance.Register(Test);
CompanionTransportTests.Register(Test);
var watch = Stopwatch.StartNew(); int failed = 0;
var selectedTests = args.Length==2&&args[0]=="--filter"?tests.Where(t=>t.Name.Contains(args[1],StringComparison.OrdinalIgnoreCase)).ToList():tests;
foreach (var (name, run) in selectedTests) { try { run(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
Console.WriteLine($"{selectedTests.Count - failed}/{selectedTests.Count} tests passed in {watch.Elapsed.TotalSeconds:N2}s");
Environment.ExitCode = failed == 0 ? 0 : 1;

