using EqlTracker.Core;
internal static class OverviewPolishTests
{
    internal static void Register(Action<string,Action> test)
    {
        static void Check(bool condition){if(!condition)throw new Exception("Overview polish invariant failed");}
        var at=new DateTime(2026,10,25,10,0,0,DateTimeKind.Local);
        CombatEvent Hit(string ability,long amount,string target="a foe",string kind="Spell")=>new(){Id=ability,Time=at,Actor="ExampleHero",Owner="Player",Target=target,Ability=ability,Kind=kind,Damage=amount,Outcome="Hit"};
        Encounter Fight(params CombatEvent[] events)=>new(){Id="fight",Start=at,End=at.AddSeconds(10),Events=events.ToList()};
        test("Maximum enemy hit separates lifetap damage from healing self and friendly records",()=>
        {
            var self=Hit("self",9000,"ExampleHero");var heal=Hit("heal",8000,"a friend","Healing");var friendly=Hit("friend",7000,"a friend");friendly.Relevance="Incoming";
            var life=Hit("Drain Spirit VIII",1543);var f=Fight(self,heal,friendly,life,Hit("slash",400,"a foe","Melee"),Hit("zero",0));
            var peak=CombatMetrics.Peaks([f],new("Combined")).First();Check(peak.Ability==life.Ability&&peak.Damage==1543&&peak.Target=="a foe"&&peak.Attacker=="ExampleHero"&&peak.Direction=="Outgoing");
            Check(f.Events.Count==6&&f.Events[0].Damage==9000);
        });
        test("Enemy maxima retain filters and earliest ties",()=>
        {
            var early=Hit("bolt",100);var late=Hit("bolt",100);late.Time=at.AddSeconds(1);var f=Fight(early,late,Hit("other",500,"another foe"));
            Check(CombatMetrics.Peaks([f],new("Combined",Target:"a foe")).Single().Time==at);
            Check(CombatMetrics.Peaks([f],new("Pet")).Count==0);
        });
        test("Legacy summary maxima without outgoing proof are unavailable",()=>
        {
            var f=Fight();f.DetailAvailable=false;f.Peaks=[new("Player","legacy", "Magic",9999,at,f.Id)];Check(CombatMetrics.Peaks([f],new()).Count==0);
            f.Peaks.Add(CombatMetrics.Peak(Hit("proved",500),f.Id));Check(CombatMetrics.Peaks([f],new("Combined")).Single().Ability=="proved");
            Check(CombatMetrics.Peaks([f],new("Combined",From:at.AddSeconds(1))).Count==0);
        });
        test("Purchase price is a whole batch total and cannot adjust inventory",()=>
        {
            var receipt=PurchaseEvidence.Parse(at,"one:120","You purchased 13 Bottle from Merchant Example for  6 silver 5 copper.")!;
            Check(receipt.Copper==65&&receipt.Quantity==13&&receipt.Kind=="Spent"&&!receipt.Acquisition&&receipt.ItemKey=="");
            Check(PurchaseEvidence.Parse(at,"one:121","You purchased 100 Sheet Metal from Merchant Example for  105 platinum.")!.Copper==105000);
        });
        test("Purchase identity is idempotent across reader generations and separates same-second transactions",()=>
        {
            string text="You purchased 1 Bottle from Merchant Example for 5 copper.";
            Check(PurchaseEvidence.Parse(at,"live:15",text)!.Id==PurchaseEvidence.Parse(at,"backfill:15",text)!.Id);
            Check(PurchaseEvidence.Parse(at,"live:16",text)!.Id!=PurchaseEvidence.Parse(at,"live:15",text)!.Id);
        });
        test("Purchase evidence rejects unknown prices overflow non-purchases and invalid quantities",()=>
        {
            foreach(string text in new[]{"You purchased 0 Bottle from Merchant Example for 5 copper.","You purchased 1 Bottle from Merchant Example for unknown.","You purchased 1 Bottle from Merchant Example for -5 copper.","You sold a Bottle for 5 copper.","You purchased 1 Bottle from Merchant Example for 99999999999999999999 platinum."})Check(PurchaseEvidence.Parse(at,"x:1",text)==null);
        });
        test("Overview combines recorded income and keeps spending separate without duplicates",()=>
        {
            var s=new PlaySession{Start=at,LastObserved=at.AddHours(1)};var purchase=PurchaseEvidence.Parse(at,"x:2","You purchased 13 Bottle from Merchant Example for 6 silver 5 copper.")!;
            var recap=SessionRecap.Build(s,[new(){Id="coin",At=at,Kind="Coin",Copper=20},new(){Id="sale",At=at,Kind="Merchant",Copper=1000},purchase,purchase],[]);
            Check(recap.Earned==1020&&recap.Spent==65&&recap.Loot.Count==0);
            var missing=SessionRecap.Build(s,[],[]);Check(missing.Earned==null&&missing.Spent==null);
        });
        test("Purchase persistence is atomic independent and restart-idempotent",()=>
        {
            string file=Path.Combine(Path.GetTempPath(),"purchase-"+Guid.NewGuid()+".db");
            using(var db=new TrackerStore(file))
            {
                db.Set("combat-checkpoint",new FileCheckpoint{Offset=700});db.Set("healing-checkpoint",new FileCheckpoint{Offset=400});
                var row=PurchaseEvidence.Parse(at,"x:12","You purchased 1 Bottle from Merchant Example for 5 copper.")!;
                db.SavePurchaseEvidence([row],new(){Offset=100});db.SavePurchaseEvidence([row],new(){Offset=200});
                Check(db.LoadActivities().Count==1&&db.Get<FileCheckpoint>(PurchaseEvidence.Version)!.Offset==200&&db.Get<FileCheckpoint>("combat-checkpoint")!.Offset==700&&db.Get<FileCheckpoint>("healing-checkpoint")!.Offset==400);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();File.Delete(file);
        });
        test("Overview loot ratings exclude miscellaneous items and unknown ranks",()=>
        {
            var misc=new ItemDefinition{Name="Bottle"};Check(!OverviewLootRatings.Equipable(misc));
            var owner=new LoadoutProfile{Classes=["WAR","CLR","BRD"],Level=50,Preset="Damage"};var profile=new CharacterProfile();var catalog=new Catalog();
            var ratings=new OverviewLootRatings(catalog,owner,SelfRoleRatings.Resolve(profile,owner));Check(ratings.Evaluate(misc,null).Unbuffed==null);
        });
    }
}
