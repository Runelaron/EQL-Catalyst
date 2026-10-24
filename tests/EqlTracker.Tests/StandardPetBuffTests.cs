using EqlTracker.Core;

static class StandardPetBuffTests
{
    public static void Register(Action<string,Action> test)
    {
        void Check(bool ok,string message="Standard buff assertion failed"){if(!ok)throw new Exception(message);}
        var at=new DateTime(2026,10,23,9,0,0,DateTimeKind.Local);
        CharacterProfile Profile()=>new(){Character="ExampleHero",PetIdentities=[new(){Name="Cloudling",From=at,Element="Monster",CanonicalSummonSpell="Monster Summoning II",SummonRank=9}]};
        SpellActivityEvidence Landing(string spell,int? rank,int seconds=10,string caster="ExampleHero",string recipient="Cloudling")=>new(){Id="fixture:"+seconds+spell,At=at.AddSeconds(seconds),Canonical=spell,Rank=rank,Caster=caster,Recipient=recipient,Transition="Landing",Source="Synthetic correlated landing"};
        test("Standard buffs learn confirmed sustained own-pet landings only",()=>{
            var history=new ConnectedSpellState{History=[Landing("Burnout III",4),Landing("Frenzied Burnout I",null),Landing("Verses of Victory",6,12,"Othercaster"),Landing("Verses of Victory",6,13,recipient:"Otherpet"),new(){Canonical="Burnout III",At=at,Transition="Cast attempt",Caster="ExampleHero"}]};var state=new StandardPetBuffState();
            Check(StandardPetBuffs.Learn(state,history,new([]),Profile())&&state.Buffs.Count==1&&state.Buffs[0].Rank==4);
        });
        test("Standard buffs retain exclusions and overrides across rank updates and restart",()=>{
            var state=new StandardPetBuffState();var history=new ConnectedSpellState{History=[Landing("Burnout III",4)]};StandardPetBuffs.Learn(state,history,new([]),Profile());state.Buffs[0].Excluded=true;state.Buffs[0].OverrideRank=true;state.Buffs[0].SelectedRank=2;
            state=EquipmentPositions.Copy(state);history.History.Add(Landing("Burnout III",3,15));StandardPetBuffs.Learn(state,history,new([]),Profile());long revision=state.Revision;Check(!StandardPetBuffs.Learn(state,history,new([]),Profile())&&state.Revision==revision&&state.Buffs[0].Excluded&&state.Buffs[0].Rank==3&&state.Buffs[0].EffectiveRank==2);
        });
        test("Standard buffs retain unknown and above-ten ranks without clamping",()=>{
            var state=new StandardPetBuffState();StandardPetBuffs.Learn(state,new(){History=[Landing("Burnout III",14)]},new([]),Profile());Check(state.Buffs[0].Rank==14&&StandardPetBuffs.Magnitude("Burnout III",14,null).Values.Count==0&&StandardPetBuffs.Magnitude("Burnout III",null,null).Values.Count==0);
        });
        test("Standard buffs scoped exact arithmetic matches Burnout and Victory",()=>{
            foreach(var (r,str,ac,haste) in new[]{(2,52d,12d,62d),(4,54d,12d,64d),(5,55d,13d,65d),(10,60d,14d,70d)}){var m=StandardPetBuffs.Magnitude("Burnout III",r,null);Check(m.Values["STR"]==str&&m.Values["AC"]==ac&&m.Values["HastePercent"]==haste);}
            var victory=StandardPetBuffs.Magnitude("Verses of Victory",6,null);Check(victory.Values["STR"]==34&&victory.Values["AGI"]==34&&victory.Values["AC"]==14&&victory.Values["HastePercent"]==36&&StandardPetBuffs.Magnitude("Other buff",4,null).Values.Count==0);
        });
        test("Standard buffs exact inspected fields override fitted estimates",()=>{
            var research=new PetResearchState{Buffs=[new(){SpellLine="Burnout III",UpgradeRank=4,Transition="Inspect",Displayed=new(){["AdjustedSTR"]=57,["AdjustedAC"]=12,["AdjustedHastePercent"]=64}}]};var m=StandardPetBuffs.Magnitude("Burnout III",4,research);Check(m.Values["STR"]==57&&!m.Estimated);
        });
        test("Standard buffs canonical ranked landing resolves the last uninterrupted cast",()=>{
            var history=new ConnectedSpellState();var index=new ConnectedSpellIndex([]);
            foreach(var (second,transition) in new[]{(0,"Cast attempt"),(4,"Interrupted / failed"),(5,"Cast attempt"),(9,"Interrupted / failed"),(10,"Cast attempt")})ConnectedSpellEvidence.Add(history,new(){Id="cast:"+second,At=at.AddSeconds(second),Raw="Burnout III IV",Canonical="Burnout III",Rank=4,Caster="ExampleHero",Transition=transition});
            ConnectedSpellEvidence.Effect(history,new(){LogId="land",Actor="Cloudling",Owner="Pet",At=at.AddSeconds(16),State="Landing",Spells=["Burnout III","Fury"]},index);
            var landed=history.History.Single(e=>e.Id=="land:spell-effect");Check(landed.Canonical=="Burnout III"&&landed.Rank==4&&landed.Transition=="Landing"&&landed.Caster=="ExampleHero");
        });
        test("Standard buffs failed and competing casts stay ambiguous",()=>{
            var index=new ConnectedSpellIndex([]);var state=new ConnectedSpellState{History=[new(){At=at,Canonical="Burnout III",Raw="Burnout III IV",Rank=4,Caster="ExampleHero",Transition="Cast attempt"},new(){At=at.AddSeconds(1),Canonical="Burnout III",Caster="ExampleHero",Transition="Interrupted / failed"}]};
            ConnectedSpellEvidence.Effect(state,new(){LogId="failed",At=at.AddSeconds(2),Actor="Cloudling",Spells=["Burnout III","Fury"],State="Landing"},index);Check(state.History.First(e=>e.Id=="failed:spell-effect").Transition.StartsWith("Ambiguous"));
        });
        test("Standard buffs metadata upgrades ambiguous records idempotently",()=>{
            var state=new ConnectedSpellState();ConnectedSpellEvidence.Add(state,new(){Id="same",Transition="Ambiguous Landing"});Check(ConnectedSpellEvidence.Add(state,new(){Id="same",Transition="Landing",Canonical="Burnout III"}));long revision=state.Revision;Check(!ConnectedSpellEvidence.Add(state,new(){Id="same",Transition="Landing",Canonical="Burnout III"})&&state.Revision==revision);
        });
        test("Standard buffs delayed ownership survives restart but not failure or pet loss",()=>{
            var detector=new PetElementDetector();var profile=new CharacterProfile();detector.Process(new(){Time=at,PlayerCast="Monster Summoning II IX"},profile);var resumed=new PetElementDetector();resumed.RestorePending(EquipmentPositions.Copy(detector.Pending!));profile.PetIdentities.Add(new(){Name="Cloudling",From=at.AddMinutes(11)});resumed.Process(new(){Time=at.AddMinutes(11),PetName="Cloudling"},profile);Check(profile.PetIdentities[0].Element=="Monster"&&profile.PetIdentities[0].SummonRank==9);
            foreach(bool failed in new[]{false,true}){var d=new PetElementDetector();var p=new CharacterProfile();d.Process(new(){Time=at,PlayerCast="Monster Summoning II IX"},p);d.Process(new(){Time=at.AddSeconds(5),CastFailed=failed,PetLost=!failed},p);p.PetIdentities.Add(new(){Name="Cloudling",From=at.AddMinutes(11)});d.Process(new(){Time=at.AddMinutes(11),PetName="Cloudling"},p);Check(p.PetIdentities[0].Element!="Monster");}
        });
        test("Standard buffs unrelated activity does not grant delayed identity",()=>{
            var d=new PetElementDetector();var p=new CharacterProfile();d.Process(new(){Time=at,PlayerCast="Monster Summoning II IX"},p);d.Process(new(){Time=at.AddMinutes(11),Combat=new(){Actor="Unknown NPC"}},p);Check(p.PetIdentities.Count==0);
        });
        test("Standard buffs paired scenario conserves snapshot and gear contributions",()=>{
            var context=new PetResearchContext{Pet="Monster",Summon="Monster Summoning II",Rank=9,Level=50,Equipment="same captured gear",Patch=PetDefinition.Version,Buffs="Unbuffed"};var baseline=new PetStatSheet{Id="base",Context=context,Reviewed=true,Values=new(){["HP"]=8000,["STR"]=200,["ACLeft"]=400,["ACRight"]=500,["AttackLeft"]=300,["AttackRight"]=310,["AttackSpeedPercent"]=140}};
            var buffed=EquipmentPositions.Copy(baseline);buffed.Id="buffed";buffed.Context.Buffs="Burnout III +4";buffed.Values["STR"]=254;buffed.Values["ACLeft"]=404;buffed.Values["AttackLeft"]=336;buffed.Values["AttackSpeedPercent"]=171;
            var research=new PetResearchState{Sheets=[baseline,buffed]};var state=new StandardPetBuffState{Buffs=[new(){Spell="Burnout III",Rank=4}],Pairs=[new(){Baseline="base",Buffed="buffed",Spell="Burnout III",Rank=4,SpeedCeiling=171}]};var stats=new PetDetailsStatistics(new([],[],new(new Dictionary<string,double>(),[],[],[])),baseline.Values,"base",true,[]){CaptureId="base",Unbuffed=true};
            var result=StandardPetBuffs.Project(stats,new(),state,research,true);Check(result.Values["STR"]==254&&result.Values["ACLeft"]==404&&result.Values["AttackRight"]==310&&result.Hints["AttackSpeedPercent"].Contains("Capped")&&stats.Observed["STR"]==200);
            state.Buffs[0].Rank=5;var estimate=StandardPetBuffs.Project(stats,new(),state,research,true);Check(estimate.Values["STR"]==255&&estimate.Values["ACLeft"]==400&&estimate.Hints["ACLeft"].Contains("unknown"));Check(StandardPetBuffs.Project(stats,new(),state,research,false).Values["STR"]==200);
        });
        test("Standard buffs missing baseline and stacking preserve captured content",()=>{
            var stats=new PetDetailsStatistics(new([],[],new(new Dictionary<string,double>(),[],[],[])),new Dictionary<string,double?>{{"STR",200}},"recorded",true,[]);var setup=new StandardPetBuffState{Buffs=[new(){Spell="Burnout III",Rank=4}]};Check(StandardPetBuffs.Project(stats,new(),setup,null,true).Title.Contains("unavailable"));setup.Buffs.Add(new(){Spell="Verses of Victory",Rank=6});var view=StandardPetBuffs.Project(stats with{Unbuffed=true},new(),setup,null,true);Check(view.Title.Contains("stacking")&&view.Values["STR"]==200);
        });
        test("Standard buffs cap hints require known overflow and do not invent pet caps",()=>{Check(StandardPetBuffs.CapHint(171,171,null,"observed").Contains("excess unknown")&&StandardPetBuffs.CapHint(171,171,178,"confirmed").StartsWith("7 over")&&StandardPetBuffs.CapHint(140,171,null,"observed")=="");});
        test("Standard buffs phase totals and seconds are conserved across transitions",()=>{
            var state=new PetResearchState();var start=at;foreach(string spell in new[]{"Burnout III","Frenzied Burnout I"})PetResearch.Observe(state,new(){Id=spell,Actor="Cloudling",At=start,Spell=spell,Transition="CorrectionOff",Evidence="Reviewed unbuffed start"});
            PetResearch.Observe(state,new(){Id="b",Actor="Cloudling",At=start.AddSeconds(114),Spell="Burnout III +4",Transition="Landing",DurationSeconds=5040});PetResearch.Observe(state,new(){Id="f",Actor="Cloudling",At=start.AddSeconds(140),Spell="Frenzied Burnout I",Transition="Landing",DurationSeconds=66});
            var fight=new Encounter{Id="synthetic",Finished=true,PetName="Cloudling",Start=start,End=start.AddSeconds(202),PetDamage=2000,Events=[new(){Id="1",Actor="Cloudling",Owner="Pet",Time=start.AddSeconds(20),Damage=1000},new(){Id="2",Actor="Cloudling",Owner="Pet",Time=start.AddSeconds(114),Damage=400},new(){Id="3",Actor="Cloudling",Owner="Pet",Time=start.AddSeconds(140),Damage=600}]};
            PetResearch.Collect(state,[fight],Profile());var s=state.Samples["synthetic"];Check(s.BuffPeriodDamage.Values.Sum()==2000&&s.BuffPeriodSeconds.Values.Sum()==202&&s.BuffPeriodSeconds["Neither observed active"]==114&&s.BuffPeriodSeconds["Burnout"]==26&&s.BuffPeriodSeconds["Mixed"]==62);
            long revision=state.Revision;Check(!PetResearch.Collect(state,[fight],Profile())&&state.Revision==revision);
        });
        test("Standard buffs later confirmed fade preserves duration disagreement",()=>{
            var state=new PetResearchState{Buffs=[new(){Actor="Cloudling",Spell="Burnout III",At=at,Transition="CorrectionOff"},new(){Actor="Cloudling",Spell="Frenzied Burnout I",At=at,Transition="Landing",DurationSeconds=66},new(){Actor="Cloudling",Spell="Frenzied Burnout I",At=at.AddSeconds(71),Transition="Fade"}]};Check(PetResearch.BuffPeriod(state.Buffs,"Cloudling",at.AddSeconds(68))=="Frenzied Burnout");
        });
    }
}
