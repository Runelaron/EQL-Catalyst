using System.Text.Json;
using EqlTracker.Core;

internal static class EquipmentIconTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new Exception("Equipment icon assertion failed");}
        ItemDefinition Weapon(string skill,WeaponShape shape=WeaponShape.Unknown)=>new(){Name="Unreliable item name",Slots=["PRIMARY","SECONDARY","RANGE"],Display=new(){Type=skill,Skill=skill,WeaponShape=shape,WeaponShapeEvidence=shape==WeaponShape.Unknown?"":"Verified structured page field"},Stats=new(){["Damage"]=12,["Delay"]=24,["Ratio"]=.5}};
        test("Legacy cached Armor is refined offline for every body slot",()=>{
            var expected=new Dictionary<string,string>{{"HEAD","Head armor"},{"FACE","Face armor"},{"SHOULDER","Shoulder armor"},{"ARMS","Arm armor"},{"WRIST","Wrist armor"},{"CHEST","Chest armor"},{"HANDS","Gloves"},{"LEGS","Leg armor"},{"FEET","Boots"}};
            foreach(var (slot,label) in expected){var item=new ItemDefinition{Slots=[slot],Display=new(){Type="Armor"},MetadataVersion=5};Check(PetItemRules.Type(item)==label&&ItemEquipmentIcons.For(item).Single().Label==label);}
        });
        test("Multiple armor slots expose all icons in alphabetical slot order",()=>{var i=new ItemDefinition{Slots=["WRIST","CHEST","ARMS","ARMS"],Display=new(){Type="Armor"}};Check(ItemEquipmentIcons.For(i).Select(x=>x.Role).SequenceEqual(["item-arms","item-armor","item-wrists"])&&PetItemRules.Type(i)=="Arm armor / Chest armor / Wrist armor");});
        test("Paired accessory slots do not duplicate icons",()=>{var i=new ItemDefinition{Slots=["EAR","EAR","FINGERS"]};Check(ItemEquipmentIcons.For(i).Select(x=>x.Role).SequenceEqual(["item-earring","item-ring"]));});
        test("Multi-slot dagger remains one dagger instead of slot icons",()=>{var icons=ItemEquipmentIcons.For(Weapon("1H Piercing",WeaponShape.Dagger));Check(icons.Count==1&&icons[0].Role=="glyph-dagger"&&icons[0].Label=="1H Piercing · Dagger");});
        test("Sword axe hammer and mace have distinct one/two-hand artwork",()=>{
            foreach(var (shape,stem,skill) in new[]{(WeaponShape.Sword,"sword","Slashing"),(WeaponShape.Axe,"axe","Slashing"),(WeaponShape.Hammer,"hammer","Blunt"),(WeaponShape.Mace,"mace","Blunt")}){
                var one=ItemEquipmentIcons.For(Weapon("1H "+skill,shape)).Single();var two=ItemEquipmentIcons.For(Weapon("2H "+skill,shape)).Single();Check(one.Role!=two.Role&&one.Label.StartsWith("1H ")&&two.Label.StartsWith("2H "));
            }
        });
        test("Blunt skill defaults distinguish handedness without claiming a mace subtype",()=>{var one=ItemEquipmentIcons.For(Weapon("1H Blunt")).Single();var two=ItemEquipmentIcons.For(Weapon("2H Blunt")).Single();Check(one.Role!=two.Role&&one.Label=="1H Blunt"&&two.Label=="2H Blunt"&&two.Evidence.Contains("shape unverified"));});
        test("Item names never establish weapon shape",()=>{var i=Weapon("1H Slashing");i.Name="Enormous Two-Handed Hammer";Check(ItemEquipmentIcons.For(i).Single().Role=="glyph-sword"&&i.Display.WeaponShape==WeaponShape.Unknown);});
        test("Unknown classification stays neutral",()=>{var i=new ItemDefinition{Name="Oak Helmet Hammer"};Check(PetItemRules.Type(i)=="Unknown"&&ItemEquipmentIcons.For(i).Single().Role=="glyph-unknown");});
        test("Unverified shape cannot replace documented category",()=>{var i=Weapon("1H Slashing",WeaponShape.Hammer);i.Display.WeaponShapeEvidence="";Check(ItemEquipmentIcons.For(i).Single().Role=="glyph-sword");});
        test("Unknown armor slots do not invent body locations",()=>{var i=new ItemDefinition{Display=new(){Type="Armor"}};Check(PetItemRules.Type(i)=="Armor · slot unverified"&&ItemEquipmentIcons.For(i).Single().Role=="glyph-unknown");});
        test("Ammunition and unspecified throwing share assorted-ammo glyph",()=>{Check(ItemEquipmentIcons.For(new(){Slots=["AMMO"]}).Single().Role=="item-ammunition"&&ItemEquipmentIcons.For(Weapon("Throwingv2")).Single().Role=="item-ammunition");});
        test("Shield evidence takes precedence over armor fallback",()=>{var i=new ItemDefinition{Slots=["SECONDARY"],Display=new(){Type="SHIELD"}};Check(ItemEquipmentIcons.For(i).Single().Role=="glyph-shield"&&PetItemRules.Type(i)=="Shield");});
        test("Structured weapon shape parser preserves skill and scoring",()=>{var i=WikiClient.ParseItem("Misleading Sword","{{Itempage\n|statsblock=Slot: PRIMARY<br>Skill: 2H Blunt Atk Delay: 30<br>DMG: 15<br>Class: ALL<br>\n|weapon_shape=Hammer\n}}",31);Check(i.Display.WeaponShape==WeaponShape.Hammer&&i.Display.WeaponShapeEvidence.Contains("revision 31")&&i.Display.Skill=="2H Blunt"&&i.Stats["Ratio"]==.5&&ItemEquipmentIcons.For(i).Single().Role=="item-hammer-2h");});
        test("Itemtype field supplies shape despite existing combat skill",()=>{var i=WikiClient.ParseItem("Example","{{Itempage\n|statsblock=Slot: PRIMARY<br>Skill: 1H Slashing Atk Delay: 20<br>DMG: 10<br>Class: ALL<br>\n|itemtype=Axe\n}}",2);Check(ItemEquipmentIcons.For(i).Single().Role=="item-axe-1h"&&PetItemRules.Type(i)=="1H Slashing");});
        test("Documented staff and fist have their own equipment motifs",()=>Check(ItemEquipmentIcons.For(Weapon("2H Blunt",WeaponShape.Staff)).Single().Role=="item-staff"&&ItemEquipmentIcons.For(Weapon("Hand to Hand")).Single().Role=="glyph-fist"));
        test("Shape metadata survives serialization without changing stable item identity",()=>{var i=Weapon("1H Blunt",WeaponShape.Hammer);i.Key="same-key";var copy=JsonSerializer.Deserialize<ItemDefinition>(JsonSerializer.Serialize(i))!;Check(copy.Key==i.Key&&copy.Display.WeaponShape==WeaponShape.Hammer&&copy.Display.WeaponShapeEvidence==i.Display.WeaponShapeEvidence&&copy.Stats["Ratio"]==i.Stats["Ratio"]);});
        test("Metadata refresh preserves independent sourced shape evidence",()=>{var old=Weapon("1H Blunt",WeaponShape.Hammer);old.Key="same";old.Revision=10;var fresh=Weapon("1H Blunt");fresh.Key="same";fresh.Revision=11;fresh.Display.Source="New wiki version";var catalog=new Catalog{Items=[old]};CatalogItemMerge.Apply(catalog,[fresh]);Check(catalog.Items.Single().Display.WeaponShape==WeaponShape.Hammer&&catalog.Items.Single().Revision==11);});
        test("Icon evaluation never mutates stats or equipment evidence",()=>{var i=Weapon("2H Blunt",WeaponShape.Hammer);var before=JsonSerializer.Serialize(i);_ = ItemEquipmentIcons.For(i);_ = PetItemRules.Type(i);Check(JsonSerializer.Serialize(i)==before);});
        test("Older parser records remain eligible for resumable refresh",()=>{string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");try{using var cache=new EquipmentCatalog(path);cache.Index(["Example"]);cache.Store([], ["Example"],6);Check(cache.Pending(DateTime.MinValue).SequenceEqual(["Example"]));}finally{File.Delete(path);}});
    }
}
