using EqlTracker.Core;

internal static class Beta17Tests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new Exception("Beta 1.7 evidence assertion failed");}
        test("Beta17 wording updates preserve reviewed semantic step identity",()=>
        {
            var old=new QuestStep{Id="quest:old-action",Name="Visit the merchant"};
            var updated=QuestChecklistRevision.Merge("quest",[old],[new("merchant",new(){Name="Speak to the named merchant at the documented location"},old.Id)]);
            Check(updated.Single().Id==old.Id&&!updated[0].Optional&&old.Name=="Visit the merchant");
        });
        test("Beta17 old incomplete markers cannot complete new quest actions",()=>
        {
            var old=new QuestStep{Id="quest:unknown",Name="Further steps undocumented",Inputs=[new(){ItemKey="old requirement"}]};
            var steps=QuestChecklistRevision.Merge("quest",[old],[new("library-key",new(){Name="Combine four documented components"})]);
            Check(steps.Count==2&&steps[0].Id!="quest:unknown"&&!steps[0].Optional&&steps[1].Id==old.Id&&steps[1].Optional&&steps[1].Inputs.Count==0);
        });
        test("Beta17 repeated quest evidence refreshes are idempotent",()=>
        {
            var reviewed=new[]{new ReviewedQuestStep("new-action",new(){Name="New action"})};
            var first=QuestChecklistRevision.Merge("quest",[new(){Id="quest:prior",Name="Older action"}],reviewed);
            var second=QuestChecklistRevision.Merge("quest",first,reviewed);Check(PetResearch.Hash(first)==PetResearch.Hash(second));
        });
        test("Beta17 duplicate or unreviewed step identity mappings fail closed",()=>
        {
            bool duplicate=false,missing=false;
            try{QuestChecklistRevision.Merge("quest",[],[new("same",new()),new("same",new())]);}catch(InvalidDataException){duplicate=true;}
            try{QuestChecklistRevision.Merge("quest",[],[new("step",new(),"missing")]);}catch(InvalidDataException){missing=true;}
            Check(duplicate&&missing);
        });
        test("Beta17 route coordinates require their own explicit label",()=>
        {
            string text="A cellar door is reached through a tunnel.\nTunnel entrance: 1620, -930, 0\nCellar door: 2435, -1155, -10";
            Check(PublicContentNotes.Coordinate(text,"Tunnel entrance")=="1620, -930, 0"&&PublicContentNotes.Coordinate(text,"Cellar door")=="2435, -1155, -10"&&PublicContentNotes.Coordinate(text,"Unknown")=="Undocumented");
        });
        test("Beta17 newer wiki omissions remain unknown after saved catalog merges",()=>
        {
            var old=new ItemDefinition{Key="mask",Name="Mask",Revision=1,MetadataVersion=11,WikiUrl="https://eqlwiki.com/Mask",Stats=new(){["AC"]=9,["ResistVoid"]=4}};
            var fresh=new ItemDefinition{Key="mask",Name="Mask",Revision=2,MetadataVersion=12,WikiUrl="https://eqlwiki.com/Mask",Stats=new(){["AC"]=10}};
            var catalog=new Catalog{Items=[old]};CatalogItemMerge.Apply(catalog,[fresh]);Check(catalog.Items[0].Stats["AC"]==10&&!catalog.Items[0].Stats.ContainsKey("ResistVoid")&&old.Stats["ResistVoid"]==4);
        });
        test("Beta17 explicit quest reward prose quarantines contradictory drop fields",()=>
        {
            var item=WikiClient.ParseItem("Quest Sword","{{Itempage\n|itemname=Quest Sword\n|dropsfrom=[[Nektropos Castle]]\n* [[Example Lord]]\n}}\nThis item is a quest reward.",2);
            var old=new ItemDefinition{Key=item.Key,Name=item.Name,Revision=1,Sources=[new(){Method="Drop",Mob="Example Lord",Availability="Wiki documented"},new(){Method="Drop",Mob="Observed actor",Availability="Observed drop"}]};
            var catalog=new Catalog{Items=[old]};CatalogItemMerge.Apply(catalog,[item]);Check(item.AvailabilityEvidence==WikiContentQuality.QuestRewardConflict&&catalog.Items[0].Sources.Count==1&&catalog.Items[0].Sources[0].Availability=="Observed drop");
        });
    }
}
