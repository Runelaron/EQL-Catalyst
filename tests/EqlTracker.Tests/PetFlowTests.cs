using EqlTracker.Core;
internal static class PetFlowTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new Exception("Pet flow assertion failed");}
        void Near(double a,double b){if(Math.Abs(a-b)>1e-9)throw new Exception($"Expected {a}; got {b}");}
        test("Pet flow: quest reward proof excludes mere related/input references",()=>{
            var item=new ItemDefinition{Key="sample blade",Name="Sample Blade"};var link=new QuestAcquisitionLink("Blade Quest","Blade Quest","https://eqlwiki.com/Blade_Quest");
            Check(ItemAcquisitionEvidence.Resolve(item,link,new("Blade Quest","== Requirements ==\nGive [[Sample Blade]] to a smith.",1))==null);
            Check(ItemAcquisitionEvidence.Resolve(item,link,new("Blade Quest","== Reward ==\n[[Sample Blade]]\n== Requirements ==\n[[Ore]]",2))?.Method=="Quest");
            Check(ItemAcquisitionEvidence.Resolve(item,link,new("Blade Quest","== Reward ==\n{{Gear Set\n|Sample Blade\n}}\n== Checklist ==\n[[Ore]]",3))?.Revision==3);
        });
        test("Pet flow: Sky reward first-cell association retains test anchor",()=>{
            var item=new ItemDefinition{Key="sky sash",Name="Sky Sash",IntroducedExpansion="Classic"};var link=new QuestAcquisitionLink("Plane of Sky","Monk Test","https://eqlwiki.com/Plane_of_Sky#Monk_Tests");
            var source=ItemAcquisitionEvidence.Resolve(item,link,new("Plane of Sky","{|\n|-\n| {{:Sky Sash}}\n| Monk Test\n| phrase\n| [[Rune]]\n|}\n",5));
            Check(source?.Zone=="Plane of Sky"&&source.WikiUrl==link.Url&&source.Mob=="Monk Test");
            Check(ItemAcquisitionEvidence.Resolve(new(){Key="rune",Name="Rune"},link,new("Plane of Sky","{|\n|-\n| {{:Sky Sash}}\n| Monk Test\n| [[Rune]]\n|}",5))==null);
        });
        test("Pet flow: merchant parsing keeps zone and vendor distinct",()=>{
            var item=WikiClient.ParseItem("Supply","{{Itempage\n|itemname=Supply\n|soldby=\n[[Town]]\n* [[Merchant One]]\n* [[Merchant Two]]\n}}",10);
            Check(item.Sources.Count==2&&item.Sources.All(s=>s.Method=="Purchase"&&s.Zone=="Town"&&s.Revision==10));
        });
        test("Pet flow: related links preserve aliases and fragments without establishing acquisition",()=>{
            var links=ItemAcquisitionEvidence.Related("{{Itempage\n|relatedquests=\n* [[Plane_of_Sky#Warrior Tests|Warrior Test of Think]]\n}}");
            Check(links.Count==1&&links[0].Target=="Plane of Sky"&&links[0].Name=="Warrior Test of Think"&&links[0].Url.EndsWith("#Warrior_Tests"));
        });
        test("Pet flow: positive fractional progress rounds without granting an early tier minimum",()=>{
            var p=RankedItemStats.Resolve(new(){Stats=new(){["AC"]=30,["DEX"]=1}},1,.5);Near(34,p.Stats["AC"]);Near(2,p.Stats["DEX"]);Check(p.Basis.CatalogRank==0&&p.Basis.ScenarioRank==1&&p.Basis.TierProgress==.5);
        });
        test("Pet flow: catalog base and unresolved haste are different evidence states",()=>{
            var item=new ItemDefinition{Stats=new(){["AC"]=5,["Haste"]=41}};var a=RankedItemStats.Resolve(item,0);var b=RankedItemStats.Resolve(item,1);
            Check(a.UncertainStats.Count==0&&a.Basis.CatalogRank==0&&b.UncertainStats.SetEquals(["Haste"])&&b.Rules.Single(r=>r.Stat=="Haste").Reason.Contains("ranked haste"));Near(6,b.Stats["AC"]);
        });
        test("Pet flow: Bloodmoon estimate is traceable and never observed by ownership",()=>{
            var item=new ItemDefinition{Key="bloodmoon",Name="Bloodmoon",Stats=new(){["Damage"]=18,["Delay"]=28}};var p=RankedItemStats.Resolve(item,4);
            Near(25d/28,p.Stats["Ratio"]);Check(!p.WeaponUncertain&&p.Rules.Single(r=>r.Stat=="Ratio").Confidence==StatRuleConfidence.AdoptedModel&&p.Kind!=ItemStatEvidenceKind.ClientObserved);
        });
        test("Pet flow: missing damage and negative scaling stay unresolved",()=>{
            var p=RankedItemStats.Resolve(new(){Display=new(){Skill="1H Slashing"},Stats=new(){["Delay"]=25,["STR"]=-4}},4);
            Check(!p.Stats.ContainsKey("Damage")&&!p.Stats.ContainsKey("Ratio")&&p.UncertainStats.IsSupersetOf(["Damage","Ratio","STR"]));Near(-4,p.Stats["STR"]);
        });
        test("Pet flow: effect projection covers every populated kind and inactive empty positions",()=>{
            var d=new ItemDefinition{Key="test sword",Name="Test sword",Slots=["PRIMARY"],Classes=["ALL"],Effects=["Focus Effect: Insight","Click Effect: Blink","Worn Effect: Guard","Proc: Spark"],Stats=new(){["Damage"]=10,["Delay"]=20}};
            var c=new Catalog{Items=[d]};var i=new PetInventoryItem{Id="one",ItemKey=d.Key,Name=d.Name,Position=1,Rank=4};var b=new PetBuild{SlotConfigurationVersion=PetSlotPlanning.Version,Items=[i],SlotConfigurations=[new(){Position=1,Effects=d.Effects.Select(e=>new ExaltationAssignment{Effect=e}).ToList()},new(){Position=2,Effects=[new(){Effect="Focus Effect: Memory"},new(){Effect=""}]}]};
            var eq=new EffectivePetEquipment([],[],new(new Dictionary<string,double>(),[],[],[]));
            var lines=PetInventoryPresentation.Effects(b,i,d,new(),c,eq);Check(lines.Select(l=>l.Kind).SequenceEqual(["Proc","Focus","Click","Worn"])&&lines.All(l=>!l.Active));
            string empty=PetInventoryPresentation.EmptyEffects(b,2);Check(empty.Contains("Memory")&&empty.Split('\n').Length==1&&empty.Contains("inactive"));
        });
        test("Pet flow: acquisition overlay is idempotent and does not change ownership",()=>{
            var catalog=Json.Read<Catalog>(Path.Combine(AppContext.BaseDirectory,"data/catalog.json"));CatalogItemMerge.Apply(catalog,Json.Read<EquipmentSnapshot>(Path.Combine(AppContext.BaseDirectory,"data/equipment.json")).Items);
            string path=Path.Combine(AppContext.BaseDirectory,"data/acquisition-evidence.json");ItemAcquisitionEvidence.Apply(catalog,path);int count=catalog.Items.Sum(i=>i.Sources.Count);ItemAcquisitionEvidence.Apply(catalog,path);Check(count==catalog.Items.Sum(i=>i.Sources.Count));
            foreach(string name in new[]{"Belt of the Four Winds","Golden Sash of Tranquility"}){var item=catalog.Find(name)!;Check(ItemAcquisition.For(item,catalog,new()).Any(r=>r.Kind=="Quest"&&r.Zone=="Plane of Sky"));}
        });
        test("Pet flow: unchanged unresolved terms cancel but haste competition remains conditional",()=>{
            var belt=new ItemDefinition{Key="uncertain belt",Name="Uncertain belt",Slots=["WAIST"],Classes=["ALL"],Stats=new(){["Haste"]=20},MetadataComplete=true};
            var cloak=new ItemDefinition{Key="known cloak",Name="Known cloak",Slots=["BACK"],Classes=["ALL"],Stats=new(){["Haste"]=30,["AC"]=5},MetadataComplete=true};
            var c=new Catalog{Items=[belt,cloak]};var before=new List<EquipmentPosition>{new(){Id="WAIST:1",Slot="Waist",Knowledge=EquipmentKnowledge.Occupied,Item=new(){Name=belt.Name+" +1",ConfirmedItemKey=belt.Key}},new(){Id="BACK:1",Slot="Back",Knowledge=EquipmentKnowledge.Empty}};
            var session=new LoadoutEvaluationSession(c,new(){Classes=["WAR","MNK","ROG"],Level=50},false,new(){["Haste"]=1,["AC"]=1},before,before);
            Check(session.CompareBuild(EquipmentPositions.Copy(before)).Status!="Conditional");
            var after=EquipmentPositions.Copy(before);after[1].Knowledge=EquipmentKnowledge.Occupied;after[1].Item=new(){Name=cloak.Name,ConfirmedItemKey=cloak.Key};Check(session.CompareBuild(after).Status=="Conditional");
            var unweighted=new LoadoutEvaluationSession(c,new(){Level=50},false,new(){["AC"]=1},before,before);Check(unweighted.CompareBuild(after).Status!="Conditional");
        });
        test("Pet flow: unchanged item revision still refreshes independent quest evidence",()=>{
            var handler=new WikiFixtureHandler();using var wiki=new WikiClient(handler);
            var items=wiki.FetchItemsAsync(["Test sword"],CancellationToken.None,new(){["test sword"]=1}).GetAwaiter().GetResult();
            Check(handler.Requests==2&&items.Single().Sources.Any(s=>s.Method=="Quest"&&s.Revision==2));
        });
        test("Pet flow: newest quest provenance is used for compact acquisition facts",()=>{
            var item=new ItemDefinition{Key="test",Name="Test",Sources=[new(){Method="Quest",Mob="Quest",Zone="Town",Revision=1,Availability="old"},new(){Method="Quest",Mob="Quest",Zone="Town",Revision=2,Availability="new"}]};
            var row=ItemAcquisition.For(item,new(),new()).Single();Check(row.Detail.Contains("new")&&row.Detail.Contains("revision 2"));
        });
    }
    private sealed class WikiFixtureHandler : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;string title=Requests==1?"Test sword":"Sword Quest";string text=Requests==1?"{{Itempage\n|itemname=Test sword\n|relatedquests=\n* [[Sword Quest]]\n}}":"== Reward ==\n[[Test sword]]\n== Requirements ==\n[[Ore]]";
            var main=new Dictionary<string,object>{{"*",text}};var revision=new{revid=Requests,slots=new{main}};var page=new{title,revisions=new[]{revision}};
            string json=System.Text.Json.JsonSerializer.Serialize(new{query=new{pages=new Dictionary<string,object>{{"1",page}}}});
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
