using EqlTracker.Core;

static class GearBuildTests
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value, string message = "Gear build assertion failed") { if (!value) throw new Exception(message); }
        static ItemDefinition Item(string key, Dictionary<string, double> stats, params string[] slots) => new() { Key = key, Name = key, Stats = stats, Slots = slots.ToList(), Classes = ["ALL"], Races = ["ALL"], MetadataComplete = true };
        static EquipmentPosition Slot(string slot, ItemDefinition? item = null, int index = 1) => new() { Id = EquipmentPositions.Key(slot, index), Slot = slot, Index = index, Knowledge = item == null ? EquipmentKnowledge.Empty : EquipmentKnowledge.Occupied, Item = item == null ? null : new() { Name = item.Name, ConfirmedItemKey = item.Key, Location = slot, Count = 1 } };
        static GearStatProfile Profile(LoadoutProfile p, List<EquipmentPosition> positions, string name, double dex) => new() { Name = name, ConfirmedAt = DateTime.Now, Level = p.Level, Classes = p.Classes.ToList(), EquipmentFingerprint = EquipmentPositions.Fingerprint(positions), Stats = new() { ["DEX"] = new() { Value = dex } }, Limits = [new() { Stat = "DEX", Value = 510, Confirmed = true, Source = "Test-confirmed cap" }] };
        test("Gear migration adds DEX/AGI without changing custom weights or old score reproducibility", () => {
            var p = new CharacterProfile { Weights = new() { ["Tank"] = new(GearScorer.Defaults["Tank"]) } }; GearRatingMigration.Apply(p);
            Check(p.ActiveLoadout.Preset == "Balanced Tank" && p.Weights["Balanced Tank"]["DEX"] == .5 && p.Weights["Balanced Tank"]["AGI"] == .5 && p.Weights["Legacy Tank"].Count == 3);
            var custom = new CharacterProfile { Weights = new() { ["Tank"] = new() { ["DEX"] = 7 } } }; GearRatingMigration.Apply(custom); GearRatingMigration.Apply(custom); Check(custom.ActiveLoadout.Preset == "Tank" && custom.Weights["Tank"]["DEX"] == 7);
        });
        test("Inventory preserves Finger aliases, both Any positions and explicit empty slots", () => {
            var inventory = InventoryParser.Parse("Location\tName\tID\tCount\tSlots\nFingers\tRing\t123\t1\t0\nFingers\tEmpty\t0\t0\t0\nAny Slot\tShield\t124\t1\t0\nAny Slot\tEmpty\t0\t0\t0\nGeneral1\tEmpty\t0\t0\t0\nKeyRing\tName\tID\n", DateTime.Now);
            Check(inventory.Complete && inventory.EquipmentPositions.Count == 4); Check(inventory.EquipmentPositions.Single(p => p.Id == "FINGER:2").Knowledge == EquipmentKnowledge.Empty); Check(inventory.EquipmentPositions.Single(p => p.Id == "ANY SLOT:1").Item!.Name == "Shield");
        });
        test("Observed loot remains inventory stock and never becomes confirmed equipment", () => {
            var at=DateTime.Now;var inventory=InventoryParser.Parse("Location\tName\tID\tCount\tSlots\nFingers\tRing\t123\t1\t0\nGeneral1\tBag\t124\t1\t0\nKeyRing\tName\tID\n",at);
            InventoryParser.Apply(inventory,new ItemObservation(Time:at.AddSeconds(1),Name:"Sword",Action:"Looted",Count:1,Source:""));
            Check(inventory.Entries.Count==3 && inventory.Entries.Single(e=>e.Name=="Sword").Estimated);
            var worn=inventory.Entries.Where(InventoryParser.IsEquipped).ToList();Check(worn.Count==1 && worn[0].Name=="Ring");
            var p=new CharacterProfile { GearRatingVersion=1 };p.ActiveLoadout.Equipment=inventory.Entries.ToList();GearRatingMigration.Apply(p);
            Check(p.ActiveLoadout.Equipment.All(e=>e.Name!="Sword") && inventory.Entries.Any(e=>e.Name=="Sword"));
        });
        test("Duplicate cached catalog keys merge safely and refresh same-count lookup indexes", () => {
            var a=Item("shovel",new(){["AC"]=1},"PRIMARY");a.Revision=10;a.MetadataVersion=2;a.GameId=16545;
            var b=Item("shovel",new(){["AC"]=2},"PRIMARY");b.Revision=10;b.MetadataVersion=3;b.GameId=16545;
            var c=new Catalog { Items=[a,a,b] };CatalogItemMerge.Apply(c,[]);Check(c.Items.Count==1 && c.Find("shovel")!.Stats["AC"]==2);
            var next=Item("shovel",new(){["AC"]=3},"PRIMARY");next.Revision=11;CatalogItemMerge.Apply(c,[next,next]);
            Check(c.Items.Count==1 && c.Find("shovel")!.Stats["AC"]==3 && c.Find("shovel")!.GameId==16545);
        });
        test("Catalog refresh retains curated identity, source claims and observed drops", () => {
            var a=Item("fragment-a",new(){["AC"]=1},"PRIMARY");a.GameId=10;a.RequiresSourceIdentity=true;a.RequiresExplicitIdentity=true;a.Sources=[new(){EvidenceId="classic-fragment",Zone="Verified zone"},new(){Availability="Observed drop",Zone="Observed zone"}];
            var b=Item("fragment-a",new(){["AC"]=2},"PRIMARY");b.Revision=10;b.GameId=99;b.Sources=[new(){Zone="Uncurated zone"}];
            var c=new Catalog { Items=[a,Item("fragment-b",new(),"PRIMARY")] };CatalogItemMerge.Apply(c,[b]);var resolved=c.FindKey("fragment-a")!;
            Check(c.Items.Count==2 && resolved.GameId==10 && resolved.RequiresExplicitIdentity && resolved.Sources.Count==2 && !resolved.Sources.Any(s=>s.Zone=="Uncurated zone"));
        });
        test("Whole-build contexts value capped DEX differently without a universal cap", () => {
            var old = Item("old", new() { ["DEX"] = 10 }, "CHEST"); var next = Item("next", new() { ["DEX"] = 30 }, "CHEST"); var c = new Catalog { Items = [old, next] }; var p = new LoadoutProfile(); var slots = new List<EquipmentPosition> { Slot("Chest", old) };
            p.EquipmentPositions = slots; p.StatProfiles = [Profile(p, slots, "Unbuffed", 450), Profile(p, slots, "Usual buffs", 505)]; var s = new LoadoutEvaluationSession(c, p, false, new() { ["DEX"] = .5 }); var r = s.Compare(next, 0, "CHEST:1");
            Check(r.Unbuffed.Delta == 10 && r.Buffed.Delta == 2.5); Check(r.Buffed.Changes.Single().Capped == 15 && r.Unbuffed.Changes.Single().ItemChange == 20);
            p.StatProfiles.Clear(); var unknown = new LoadoutEvaluationSession(c, p, false, new() { ["DEX"] = .5 }).Compare(next, 0, "CHEST:1"); Check(unknown.Unbuffed.Delta == 10 && unknown.Buffed.Delta == null && unknown.Unbuffed.Quality == "Gear-only estimate");
        });
        test("Hidden overcap loss is uncertain while known raw overflow is preserved", () => {
            var old = Item("old", new() { ["DEX"] = 30 }, "CHEST"); var next = Item("next", new() { ["DEX"] = 10 }, "CHEST"); var c = new Catalog { Items = [old,next] }; var p = new LoadoutProfile(); var slots = new List<EquipmentPosition> { Slot("Chest", old) }; p.EquipmentPositions = slots; var profile = Profile(p, slots, "Unbuffed", 510); p.StatProfiles = [profile];
            Check(new LoadoutEvaluationSession(c,p,false,new() { ["DEX"] = 1 }).Compare(next,0,"CHEST:1").Unbuffed.Changes.Single().Uncertain);
            profile.Stats["DEX"].Overflow = 30; var r = new LoadoutEvaluationSession(c,p,false,new() { ["DEX"] = 1 }).Compare(next,0,"CHEST:1"); Check(r.Unbuffed.Delta == 0 && !r.Unbuffed.Changes.Single().Uncertain);
        });
        test("AA bonuses are never added twice and AA-off rejects unknown ranks", () => {
            var old = Item("old",new() { ["DEX"] = 10 },"CHEST"); var next = Item("next",new() { ["DEX"] = 30 },"CHEST"); var c = new Catalog { Items=[old,next] }; var p = new LoadoutProfile(); var slots = new List<EquipmentPosition> { Slot("Chest",old) }; p.EquipmentPositions=slots; var profile=Profile(p,slots,"Unbuffed",500); profile.AAs=[new() { Name="Innate", Rank=5, Target="Self", Kind=AaModifierKind.FlatStat,Stat="DEX", Value=10, Confirmed=true }]; p.StatProfiles=[profile];
            profile.AAListComplete=true; var session=new LoadoutEvaluationSession(c,p,false,new() { ["DEX"]=1 }); Check(session.Compare(next,0,"CHEST:1").Unbuffed.Delta==10); Check(session.Compare(next,0,"CHEST:1",includeAAs:false).Unbuffed.Delta==20);
            profile.AAs[0].Rank=null; Check(new LoadoutEvaluationSession(c,p,false,new() { ["DEX"]=1 }).Compare(next,0,"CHEST:1",includeAAs:false).Unbuffed.Delta==null);
        });
        test("Known-empty positions compare with zero; unknown and incomplete baselines do not", () => {
            var item=Item("next",new() { ["HP"]=20 },"FINGER"); var c=new Catalog { Items=[item] }; var p=new LoadoutProfile { EquipmentPositions=[Slot("Finger")] }; var session=new LoadoutEvaluationSession(c,p,false,new() { ["HP"]=1 }); Check(session.Compare(item,0,"FINGER:1").Unbuffed.Delta==20); Check(session.Compare(item,0,"FINGER:2").Unbuffed.Delta==null);
        });
        test("Two-handed and Any Slot replacements use physical positions without weapon ratio leakage", () => {
            var main=Item("main",new() { ["Ratio"]=.5 },"PRIMARY"); var off=Item("off",new() { ["Ratio"]=.4 },"SECONDARY"); var two=Item("two",new() { ["Ratio"]=1.1 },"PRIMARY"); two.TwoHanded=true; var c=new Catalog { Items=[main,off,two] }; var p=new LoadoutProfile { EquipmentPositions=[Slot("Primary",main),Slot("Secondary",off),Slot("Any Slot")] };
            var session=new LoadoutEvaluationSession(c,p,false,new() { ["Ratio"]=100 }); var r=session.Compare(two,0,"PRIMARY:1"); Check(Math.Abs(r.Unbuffed.Delta!.Value-20)<.001 && r.Replaced.Count==2); Check(session.Compare(two,0,"ANY SLOT:1").Unbuffed.Delta==0);
        });
        test("Stacking haste compares strongest equipment rather than adding both item values", () => {
            var a=Item("a",new() { ["Haste"]=20 },"WAIST"); var b=Item("b",new() { ["Haste"]=15 },"HANDS"); var n=Item("n",new() { ["Haste"]=18 },"HANDS"); var c=new Catalog { Items=[a,b,n] }; var p=new LoadoutProfile { EquipmentPositions=[Slot("Waist",a),Slot("Hands",b)] };
            Check(new LoadoutEvaluationSession(c,p,false,new() { ["Haste"]=3 }).Compare(n,0,"HANDS:1").Unbuffed.Delta==0);
        });
        test("Stale profiles lose cap adjustments without fabricating a buffed result", () => {
            var a=Item("a",new() { ["DEX"]=1 },"CHEST"); var c=new Catalog { Items=[a] }; var p=new LoadoutProfile { EquipmentPositions=[Slot("Chest")] }; var profile=Profile(p,p.EquipmentPositions,"Usual buffs",510); p.StatProfiles=[profile]; p.Level++;
            var r=new LoadoutEvaluationSession(c,p,false,new() { ["DEX"]=1 }).Compare(a,0,"CHEST:1"); Check(r.Buffed.Delta==null && r.Buffed.Notes.Any(n=>n.Contains("stale")));
        });
        test("Pet drafts preserve summon context, inactive stock, planned effects and distinct stats", () => {
            var p=new LoadoutProfile { Classes=["MAG"], PetStatProfiles=[new() { Name="Unbuffed" }] }; var build=new PetBuild { Name="Water", PetType="Water", PetLevel=49, SummonSpell="Greater Conjuration", SummonRank=9, Focus="Water focus", Items=[new() { Position=1, Slot="Primary", Name="Blade", ItemKey="blade" },new() { Position=10,Slot="Chest",Name="Inactive",ItemKey="inactive" }] };
            var clone=GuidedPetBuilds.Clone(build,"Draft"); var context=GuidedPetBuilds.Context(p,clone); Check(clone.Id!=build.Id && context.PetSummonRank==9 && context.PetFocus=="Water focus"); Check(GuidedPetBuilds.Positions(clone,p).Count==7 && clone.Items.Count==2); clone.Items[0].Rank=10; Check(build.Items[0].Rank==0);
        });
        test("Unchanged unknown equipment permits a labeled marginal change but never fabricated build totals",()=>{
            var old=Item("old",new(){["HP"]=10},"CHEST");var next=Item("next",new(){["HP"]=30},"CHEST");var missing=Item("missing",new(),"HEAD");missing.MetadataComplete=false;var c=new Catalog{Items=[old,next,missing]};var p=new LoadoutProfile{EquipmentPositions=[Slot("Chest",old),Slot("Head",missing)]};
            var r=new LoadoutEvaluationSession(c,p,false,new(){["HP"]=1}).Compare(next,0,"CHEST:1");Check(r.Unbuffed.Delta==20 && r.Unbuffed.CurrentScore==null && r.Unbuffed.Quality.StartsWith("Partial"));Check(r.Buffed.Delta==null);
            Check(new LoadoutEvaluationSession(c,p,false,new(){["HP"]=1}).Compare(next,0,"HEAD:1").Unbuffed.Delta==null);
        });
        test("Break-even scans both contexts through +20 while preserving equip and proc level distinctions",()=>{
            var old=Item("old",new(){["DEX"]=20},"CHEST");var next=Item("next",new(){["DEX"]=15},"CHEST");next.EffectActivationLevel=50;var c=new Catalog{Items=[old,next]};var p=new LoadoutProfile{EquipmentPositions=[Slot("Chest",old)]};p.StatProfiles=[Profile(p,p.EquipmentPositions,"Unbuffed",100),Profile(p,p.EquipmentPositions,"Usual buffs",200)];
            var session=new LoadoutEvaluationSession(c,p,false,new(){["DEX"]=1});Check(session.BreakEven(next,"CHEST:1")==4);Check(session.Compare(next,4,"CHEST:1").ActivationNote.Contains("inactive"));next.RequiredLevel=50;Check(new LoadoutEvaluationSession(c,p,false,new(){["DEX"]=1}).BreakEven(next,"CHEST:1")==null);
        });
        test("Confirmed worn-AC soft caps and reversible AA changes never cap displayed composite AC",()=>{
            var old=Item("old",new(){["AC"]=80},"CHEST");var next=Item("next",new(){["AC"]=120},"CHEST");var c=new Catalog{Items=[old,next]};var p=new LoadoutProfile{EquipmentPositions=[Slot("Chest",old)]};var profile=Profile(p,p.EquipmentPositions,"Unbuffed",100);profile.Stats["AC"]=new(){Value=1000};profile.Limits=[new(){Stat="AC",Kind=StatLimitKind.WornAcSoftCap,Value=100,AboveCapWeight=.5,Confirmed=true,Source="fixture"}];p.StatProfiles=[profile];var r=new LoadoutEvaluationSession(c,p,false,new(){["AC"]=1}).Compare(next,0,"CHEST:1");Check(r.Unbuffed.Delta==30 && r.Unbuffed.Changes.Single().UsableChange==30);
            profile.AAs=[new(){Name="A",Rank=1,Stat="AC",Kind=AaModifierKind.SoftCapPercent,Value=10,Confirmed=true},new(){Name="B",Rank=1,Stat="AC",Kind=AaModifierKind.SoftCapPercent,Value=2,Confirmed=true}];Check(new LoadoutEvaluationSession(c,p,false,new(){["AC"]=1}).Compare(next,0,"CHEST:1",includeAAs:false).Unbuffed.Delta==null);
        });
        test("Pet availability counts physical copies and manual stock without inventing collection quantities",()=>{
            var item=Item("ring",new(),"FINGER");var c=new Catalog{Items=[item]};var plan=new PetBuild{Items=[new(){ItemKey="ring"},new(){ItemKey="ring"}]};var inventory=new InventorySnapshot{Entries=[new(){Name="ring",Count=null,Location="Equipment"}]};Check(GuidedPetBuilds.Availability(plan,new(),inventory,c).Single().Contains("obtain or transfer 2"));inventory.Entries.Add(new(){Name="ring",Count=1,Location="Bank1"});inventory.Invalidate();Check(GuidedPetBuilds.Availability(plan,new(){Items=[new(){ItemKey="ring"}]},inventory,c).Count==0);
        });
        test("Socketed effects enforce destination rank and selected physical slot",()=>{
            var blade=Item("blade",new(){["HP"]=1},"PRIMARY","SECONDARY");var focus=Item("focus",new(),"PRIMARY");focus.Effects=["Focus: Example"];var c=new Catalog{Items=[blade,focus]};var p=new LoadoutProfile{EquipmentPositions=[Slot("Primary"),Slot("Secondary")]};var entry=new InventoryEntry{Augments=[new(){Name="focus (Exaltation)",EffectText="Focus: Example"}]};var s=new LoadoutEvaluationSession(c,p,false,new(){["HP"]=1});Check(s.Compare(blade,0,"PRIMARY:1",entry).Eligibility.Contains("+1"));Check(s.Compare(blade,1,"SECONDARY:1",entry).Eligibility.Contains("Slot restricted"));Check(s.Compare(blade,1,"PRIMARY:1",entry).Unbuffed.Delta==2);
        });
        test("Buff notes without observed buffed totals cannot masquerade as a buffed ranking",()=>{
            var item=Item("next",new(){["DEX"]=5},"CHEST");var c=new Catalog{Items=[item]};var p=new LoadoutProfile{EquipmentPositions=[Slot("Chest")]};var profile=Profile(p,p.EquipmentPositions,"Usual buffs",100);profile.Buffs="Buff notes only";profile.BuffsIncludedInTotals=false;p.StatProfiles=[profile];Check(new LoadoutEvaluationSession(c,p,false,new(){["DEX"]=1}).Compare(item,0,"CHEST:1").Buffed.Delta==null);
        });
    }
}
