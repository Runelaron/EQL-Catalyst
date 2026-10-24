using EqlTracker.Core;

internal static class LocalIterationTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value) { if(!value) throw new Exception("Local iteration assertion failed"); }
        void Near(double expected,double? actual) { if(!actual.HasValue||Math.Abs(expected-actual.Value)>1e-9)throw new Exception($"Expected {expected}; got {actual}"); }
        var catalog=new Catalog{Items=Json.Read<EquipmentSnapshot>(Path.Combine(AppContext.BaseDirectory,"data/equipment.json")).Items};
        var wu=catalog.Find("Wu's Fist of Mastery")!;
        foreach(var (rank,damage) in new[]{(0,16d),(1,17d),(4,22d),(10,32d)})
            test("Local iteration: Wu rank "+rank+" reconciles numeric rating without editing inventory",()=>{
                var owner=new LoadoutProfile{Classes=["MNK","MAG","SHD"],Level=50,PetType="Earth",PetLevel=50};
                var build=new PetBuild{PetType="Earth",PetLevel=50,SlotConfigurationVersion=PetSlotPlanning.Version};
                string original=System.Text.Json.JsonSerializer.Serialize(build);var equipment=PetEquipmentInference.Build(build,owner,catalog);
                var session=new LoadoutEvaluationSession(catalog,owner,true,new(){["Ratio"]=100},equipment.Positions,equipment.Positions,petInventory:build,effectivePetEquipment:equipment);
                var result=session.Compare(wu,rank,"PRIMARY:1");var projection=RankedItemStats.Resolve(wu,rank);
                Near(damage,projection.Stats["Damage"]);Near(22,projection.Stats["Delay"]);Near(damage/22,projection.Stats["Ratio"]);
                Near(damage/22*100,result.Unbuffed.Delta);Near(result.Unbuffed.Delta!.Value,result.Unbuffed.Changes.Sum(c=>c.Points));
                Check(result.Status!="Conditional"&&!projection.WeaponUncertain&&projection.Kind!=ItemStatEvidenceKind.ClientObserved);
                Check(original==System.Text.Json.JsonSerializer.Serialize(build)&&wu.Stats["Damage"]==16);
                if(rank>0)Check(projection.Rules.Single(r=>r.Stat=="Damage").Confidence==StatRuleConfidence.AdoptedModel);
            });
        test("Local iteration: observed weapon values and weights override only exact ranks",()=>{
            var a=RankedItemStats.Resolve(catalog.Find("Revultant Whip")!,4);var b=RankedItemStats.Resolve(catalog.Find("Evensong")!,5);
            Near(7,a.Stats["Damage"]);Near(.5,a.Stats["Ratio"]);Near(.7,a.Weight);Near(16,b.Stats["Damage"]);Near(.64,b.Stats["Ratio"]);Near(1.7,b.Weight);
            Check(a.Kind==ItemStatEvidenceKind.ClientObserved&&b.Kind==ItemStatEvidenceKind.ClientObserved&&!a.WeightIsBase&&!b.WeightIsBase);
            Check(RankedItemStats.Resolve(catalog.Find("Evensong")!,4).Kind==ItemStatEvidenceKind.Estimated);
        });
        test("Local iteration: fractional weapon model has no ordinary minimum",()=>{
            Near(18,RankedItemStats.Resolve(wu,1,.5).Stats["Damage"]);
            var p=RankedItemStats.Resolve(new(){Stats=new(){["Damage"]=1,["Delay"]=20,["AC"]=1,["Haste"]=36,["STR"]=-4}},1);
            Near(1,p.Stats["Damage"]);Near(2,p.Stats["AC"]);Near(36,p.Stats["Haste"]);Near(-4,p.Stats["STR"]);Near(2.5,p.Accuracy);
            Check(p.UncertainStats.SetEquals(["Haste","STR"]));
        });
        test("Local iteration: missing weapon inputs stay missing and base weight stays separate",()=>{
            var missing=RankedItemStats.Resolve(new(){Display=new(){Skill="Hand to Hand"},Stats=new(){["Delay"]=22}},4);
            Check(missing.WeaponUncertain&&!missing.Stats.ContainsKey("Damage")&&!missing.Stats.ContainsKey("Ratio")&&!missing.WeightIsBase);
            var p=RankedItemStats.Resolve(wu,4);Near(.5,p.Weight);Check(p.WeightIsBase);
        });
    }
}
