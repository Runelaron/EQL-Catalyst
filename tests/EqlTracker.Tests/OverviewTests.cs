using EqlTracker.Core;
internal static class OverviewTests
{
    internal static void Register(Action<string,Action> test)
    {
        static void Check(bool value){if(!value)throw new Exception("Overview invariant failed");}
        var at=new DateTime(2026,10,23,10,0,0,DateTimeKind.Local);
        PlaySession Session()=>new(){Id="session",Start=at,LastObserved=at.AddHours(1),NextLogin=at.AddHours(2)};
        Encounter Fight(string id,int seconds,long self,long pet)=>new(){Id=id,Start=at.AddMinutes(5),End=at.AddMinutes(5).AddSeconds(seconds),PlayerDamage=self,PetDamage=pet,HealingDetailAvailable=false,HealingCoverage="Complete",HealingSummaries=[new("Done","Player",50,0,0,1,50),new("Received","Player",900,0,0,1,900)]};
        test("Overview full-session sums preserve combat denominator and deduplicate records",()=>
        {
            var fight=Fight("fight",20,200,100);var activity=new JournalActivity{Id="receipt",At=at.AddMinutes(1),Acquisition=true,Kind="Item",Name="Gem",ItemKey="gem",Quantity=2};
            var view=SessionOverview.Build(Session(),[activity,activity,new(){Id="sale",At=activity.At,Kind="Merchant",Copper=1000},new(){Id="coin",At=activity.At,Kind="Coin",Copper=20}],[fight,fight],false);
            Check(view.Recap.Encounters==1&&view.Recap.Loot.Single().Quantity==2&&view.Recap.MerchantIncome==1000&&view.Recap.Coin==20);
            Check(view.CombatSeconds==CombatMetrics.Duration([fight],new())&&view.SelfDps==200/Math.Max(1,view.CombatSeconds)&&view.PetDps==100/Math.Max(1,view.CombatSeconds));Check(view.ElapsedSeconds==3600&&view.Healing==50&&fight.PlayerDamage==200);
        });
        test("Overview unknown income and unavailable healing stay unknown",()=>{var f=Fight("x",2,0,1);f.HealingCoverage="Unavailable";var view=SessionOverview.Build(Session(),[],[f],false);Check(view.Recap.Coin==null&&view.Recap.MerchantIncome==null&&view.Healing==null&&view.Qualification.Contains("last observed"));});
        test("Overview session edges and newer live snapshots are deterministic",()=>{var f=Fight("x",2,100,1);var updated=Fight("x",3,300,5);var outside=Fight("outside",1,999,999);outside.Start=Session().NextLogin!.Value;var view=SessionOverview.Build(Session(),[],[f,outside,updated],true);Check(view.Recap.PlayerDamage==300&&view.Recap.Encounters==1&&view.Active&&view.Qualification.Contains("In progress"));});
        test("Overview planned setup and manual checklist evidence remain separate",()=>{var view=SessionOverview.Build(Session(),[new(){Id="p",At=at,Kind="Planned setup",Name="Swap"},new(){Id="q",At=at,Kind="Progress",Name="Checked",Source="Manual checklist"}],[],false,true);Check(view.Recap.Setup.Single().Name=="Swap"&&view.Recap.Progress.Single().Source=="Manual checklist"&&view.Recap.HistoryIncomplete);});
        test("Overview all retained receipts load beyond journal page size",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"catalyst-overview-"+Guid.NewGuid()+".db");
            using(var store=new TrackerStore(path)){store.SaveActivities(Enumerable.Range(0,10001).Select(n=>new JournalActivity{Id="r"+n,At=at.AddSeconds(1),Kind="Item",Acquisition=true,Name="Gem",ItemKey="gem",Quantity=1}));var rows=store.ActivitiesForSession(Session(),default);Check(rows.Count==10001&&ActivityJournal.Loot(rows,Session()).Single().Quantity==10001);using var cancel=new CancellationTokenSource();cancel.Cancel();bool rejected=false;try{store.ActivitiesForSession(Session(),cancel.Token);}catch(OperationCanceledException){rejected=true;}Check(rejected);}
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();File.Delete(path);
        });
        test("Overview state is per-character and additive beside independent meters",()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"catalyst-overview-state-"+Guid.NewGuid());Directory.CreateDirectory(root);
            foreach(string name in new[]{"one","two"})using(var store=new TrackerStore(Path.Combine(root,name+".db"))){store.Set("profile",new CharacterProfile{Character=name,Meter=new(){SessionId="lab"},OverlayMeter=new(){SessionId="overlay"}});store.Set("overview-session-v1",name);}
            foreach(string name in new[]{"one","two"})using(var store=new TrackerStore(Path.Combine(root,name+".db"))){Check(store.Get<string>("overview-session-v1")==name&&store.Get<CharacterProfile>("profile")!.Meter.SessionId=="lab"&&store.Get<CharacterProfile>("profile")!.OverlayMeter!.SessionId=="overlay");}
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);
        });
        test("Level 50 alone never establishes the ordinary pet speed ceiling",()=>
        {
            var context=new EvaluationContext{SummonedPet=true,PetLevel=50,ObservedOrdinaryAttackSpeed=171,SpeedEvidence=EvidenceQuality.Observed,SpeedSource="Inspection",Fingerprint="gear",Patch=PetDefinition.Version};
            Check(!BuildCompatibility.HasOrdinaryCeiling(context));context.ReviewedOrdinaryCeiling=171;context.CeilingFingerprint="other";Check(!BuildCompatibility.HasOrdinaryCeiling(context));context.CeilingFingerprint="gear";Check(BuildCompatibility.HasOrdinaryCeiling(EquipmentPositions.Copy(context)));context.Patch="historical";Check(!BuildCompatibility.HasOrdinaryCeiling(context));
        });
        test("Compatibility evidence never rewrites equipment preference points",()=>
        {
            var item=new ItemDefinition{Key="fixture",Name="Fixture",MetadataComplete=true,Stats=new(){["HP"]=20,["Haste"]=40}};var catalog=new Catalog{Items=[item]};var owner=new LoadoutProfile{Classes=["WAR"],Level=50};
            var before=new List<EquipmentPosition>{new(){Id="chest",Slot="Chest",Knowledge=EquipmentKnowledge.Empty}};var after=EquipmentPositions.Copy(before);after[0].Knowledge=EquipmentKnowledge.Occupied;after[0].Item=new(){Name="Fixture",ConfirmedItemKey="fixture"};
            var original=new LoadoutEvaluationSession(catalog,owner,false,new(){["HP"]=1,["Haste"]=1},before,before).CompareBuild(after);
            owner.Mechanics=new(){Revision="fixture",SelfUsualBuffs=[new(){Identity=new("unknown","Unknown",null),Family="Unresolved buff"}]};
            var reviewed=new LoadoutEvaluationSession(catalog,owner,false,new(){["HP"]=1,["Haste"]=1},before,before).CompareBuild(after);
            Check(reviewed.Unbuffed.CandidateScore==original.Unbuffed.CandidateScore&&reviewed.Buffed.CandidateScore==original.Buffed.CandidateScore&&reviewed.Status==original.Status&&reviewed.UnbuffedCompatibility!=null);
        });
        test("Overview is appended without losing prior workspace order or dimensions",()=>{var state=new WorkspaceToolbarState{Order=["CombatLab","Tracker"],Active="CombatLab",Placement=new(){Width=590,Height=28}};state.Normalize();Check(state.Order.SequenceEqual(["CombatLab","Tracker","Overview"])&&state.Placement.Width==590&&state.Active=="CombatLab");Check(WorkspaceToolbarLayout.Move(state,"Overview",-1)&&WorkspaceToolbarLayout.Move(state,"Overview",-1)&&!WorkspaceToolbarLayout.Move(state,"Overview",-1));});
    }
}
