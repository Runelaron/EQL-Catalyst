using EqlTracker.Core;

internal static class Beta16Tests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new Exception("Beta 1.6 evidence assertion failed");}
        test("Beta16 incomplete item template retains only supported acquisition",()=>
        {
            var item=WikiClient.ParseItem("New Robe","{{Itempage\n|itemname=ITEM_NAME\n|statsblock=Slot: PRIMARY / SECONDARY / RANGE / AMMO / ANY / CHEST<br>Skill: 1H Blunt / 2H Blunt / 1H Slashing / 2H Slashing<br>DMG: ? AC: ? STR: +? WT: 0.0<br>Class: ALL / BER, BRD, BST, CLR<br>Race: ALL<br>\n|dropsfrom=\n[[Nektropos Castle]]\n* [[A Named Ghost]]\n|soldby=[[Vendor_Name]]\n}}\n[[Category:Primary]][[Category:Chest]]",7);
            Check(item.Name=="New Robe"&&!item.MetadataComplete&&item.Slots.Count==0&&item.Classes.Count==0&&item.Stats.Count==0&&item.Display.Weight==null&&item.Effects.Count==0);
            Check(item.Sources.Count==1&&item.Sources[0].Mob=="A Named Ghost"&&item.Sources[0].Revision==7);
        });
        test("Beta16 vendor tables retain proof and reject placeholder vendors",()=>
        {
            var item=WikiClient.ParseItem("Vendor Gem","{{Itempage\n|itemname=Vendor Gem\n|soldby=\n{{ItemWhereRow | [[Lake Nerius]] | [[Gem Merchant]] | | (1,2) }}\n* [[Vendor_Name]]\n}}",12);
            Check(item.Sources.Count==1&&item.Sources[0].Method=="Purchase"&&item.Sources[0].Mob=="Gem Merchant"&&item.Sources[0].Zone=="Lake Nerius"&&item.Sources[0].Revision==12);
        });
        test("Beta16 Starts In headers and missing NPC placeholders are parsed separately",()=>
        {
            var p=QuestStartEvidence.Parse("Access","! ''' Starts In: '''\n| [[Lake Nerius]]\n! ''' Quest Giver: '''\n| [[Retainer Ozval]]",4);Check(p.StartZone=="Lake Nerius"&&p.StartNpc=="Retainer Ozval");
            var incomplete=QuestStartEvidence.Parse("Incomplete","! ''' Starts In: '''\n| [[ZONE_NAME]]\n! ''' Quest Giver: '''\n| [[NPC_NAME]]",5);Check(incomplete.StartZone==""&&incomplete.StartNpc=="");
        });
        test("Beta16 named NPC evidence is qualified by zone and explicit aliases",()=>
        {
            var named=new NamedNpcIndex(new(){Records=[new(){Name="Named Ghost",Zone="Nektropos Castle",Aliases=["The Named Ghost"],Url="https://eqlwiki.com/Named_Ghost",Revision=4,ClassificationEvidence="Reviewed named-NPC fixture"}]});
            Check(named.Matches("Nektropos Castle D1","the named ghost")&&!named.Matches("Lake Nerius","Named Ghost")&&!named.Matches("Nektropos Castle","A Proper Name"));
            var mixed=new Encounter{Zone="Nektropos Castle",Events=[new(){Owner="Pet",Target="ordinary ghost"},new(){Owner="Player",Target="The Named Ghost"}]};Check(named.Matches(mixed));
            Check(!named.Matches(new Encounter{Zone="Nektropos Castle",Events=[new(){Owner="Unknown",Target="Named Ghost"}]}));
        });
        test("Beta16 generic Namedmobpage and capitalization are not named-spawn evidence",()=>
        {
            Check(NamedNpcClassification.Evidence("{{Namedmobpage\n|name=A Nektulos Spiderling\n|description=An ordinary spider.\n}}")=="");
            Check(NamedNpcClassification.Evidence("{{Namedmobpage\n|name=Ghost\n|description=A rare spawn in the castle.\n}}").Length>0);
            Check(NamedNpcClassification.Evidence("{{Namedmobpage\n|name=Named Ghost\n|description=This is not a rare spawn.\n}}")=="");
            Check(!new NamedNpcIndex(new(){Records=[new(){Name="A Proper Name",Zone="Castle",Url="https://eqlwiki.com/Example",Revision=1}]}).Matches("Castle","A Proper Name"));
        });
        test("Beta16 source quality cannot reacquire old template weapon fields on merge",()=>
        {
            var old=new ItemDefinition{Name="Unknown Robe",Key="unknown robe",Revision=1,Stats=new(){["Damage"]=1},Display=new(){Weight=0},Slots=["PRIMARY"],Classes=["ALL"],MetadataComplete=true};
            var clean=WikiClient.ParseItem("Unknown Robe","{{Itempage\n|statsblock=Skill: 1H Blunt / 2H Blunt / 1H Slashing\n}}",2);var c=new Catalog{Items=[old]};CatalogItemMerge.Apply(c,[clean]);var result=c.Find("Unknown Robe")!;
            Check(result.Stats.Count==0&&result.Display.Weight==null&&!result.MetadataComplete&&result.Slots.Count==0);
        });
        test("Beta16 single-level reward headings prove outputs but related links alone do not",()=>
        {
            var item=new ItemDefinition{Name="Reward Gem",Key="reward gem"};var link=new QuestAcquisitionLink("Known Quest","Known Quest","https://eqlwiki.com/Known_Quest");
            Check(ItemAcquisitionEvidence.Resolve(item,link,new("Known Quest","= Reward =\n* {{:Reward Gem}}\n= Summary =\n* Speak with the NPC",3))!=null);
            Check(ItemAcquisitionEvidence.Resolve(item,link,new("Known Quest","= Related quests =\n* [[Reward Gem]]",4))==null);
        });
    }
}
