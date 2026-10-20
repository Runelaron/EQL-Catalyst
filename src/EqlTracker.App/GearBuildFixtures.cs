using EqlTracker.Core;

namespace EqlTracker.App;

internal static class GearBuildFixtures
{
    internal static void Apply(TrackerController c)
    {
        if (!c.Demo) throw new InvalidOperationException("Gear fixtures require an isolated preview controller.");
        var p = c.Profile.ActiveLoadout; p.Classes = ["SHD", "BRD", "MAG"]; p.Level = 49; p.Preset = "Balanced Tank"; p.PetPreset = "Pet damage";
        p.EquipmentPositions = []; EquipmentPositions.Complete(p.EquipmentPositions); foreach (var position in p.EquipmentPositions) position.Knowledge = EquipmentKnowledge.Empty;
        void Equip(string slot, string name, int rank)
        { var item = c.Catalog.Find(name)!; var position = p.EquipmentPositions.Single(s => s.Id == EquipmentPositions.Key(slot)); position.Knowledge = EquipmentKnowledge.Occupied; position.Item = new() { Name = name + (rank > 0 ? " +"+rank : ""), ConfirmedItemKey = item.Key, Location = slot, GameId = item.GameId ?? 0, Count = 1 }; }
        Equip("Chest", "Apothic Robe", 5); Equip("Primary", "Bloodmoon", 0); p.Equipment = p.EquipmentPositions.Where(s=>s.Item!=null).Select(s=>s.Item!).ToList(); p.EquipmentDraft = null;
        p.StatProfiles = Profiles(p,p.EquipmentPositions,false); p.PetType = "Water"; p.PetLevel = 49; p.PetSummonSpell = "Greater Conjuration: Water Summon"; p.PetSummonRank = 8; p.PetFocus = "Staff of Elemental Mastery: Water";
        var pet = new PetBuild { Name="Confirmed Water inventory",PetType="Water",PetLevel=49,SummonSpell=p.PetSummonSpell,SummonRank=8,Focus=p.PetFocus };
        int pos=0; foreach (var (name,slot) in new[] {("Indicolite Breastplate","Chest"),("Gauntlets of Fiery Might","Hands")})
        { var item=c.Catalog.Find(name)!;pet.Items.Add(new() {Position=++pos,ItemKey=item.Key,Name=item.Name,Slot=slot,ConfirmedAt=DateTime.Now,EquipmentVerified=true}); }
        var reference=GuidedPetBuilds.Positions(pet,p); pet.StatProfiles=Profiles(p,reference,true); PetInventoryEditing.Confirm(p,pet,DateTime.Now);
        p.PetBuilds=[GuidedPetBuilds.Clone(pet,"Water · sustained damage")]; c.Profile.GearTarget="Self";c.Profile.GearPositionId="CHEST:1";c.Profile.GearExpansions=["Classic","Kunark","Velious","Unknown"];c.Save();
    }
    private static List<GearStatProfile> Profiles(LoadoutProfile p,List<EquipmentPosition> positions,bool pet) => new[] {"Unbuffed","Usual buffs"}.Select(name=> new GearStatProfile {
        Name=name,ConfirmedAt=DateTime.Now,Level=pet?p.PetLevel??49:p.Level,Classes=p.Classes.ToList(),EquipmentFingerprint=EquipmentPositions.Fingerprint(positions),PetSignature=pet?LoadoutEvaluationSession.PetSignature(p):"",Source="Illustrative preview fixture · not measured character stats",
        Stats=new() { ["AC"]=new() {Value=name=="Unbuffed"?700:900},["HP"]=new() {Value=4500},["STA"]=new() {Value=300},["DEX"]=new() {Value=name=="Unbuffed"?300:510,Overflow=name=="Unbuffed"?null:25},["AGI"]=new() {Value=250},["STR"]=new() {Value=250},["ATK"]=new() {Value=700},["Mana"]=new() {Value=3500},["INT"]=new() {Value=270} },
        Limits=[new() {Stat="DEX",Value=510,Confirmed=true,Source="Illustrative user override · not a bundled EQL cap"}],Buffs=name=="Usual buffs"?"Example raid buffs; totals entered directly":"",AAs=[new() {Name="Combat Agility",Rank=3,Target=pet?"Pet":"Self",Source=GearMechanicsEvidence.AaSource}]
    }).ToList();
}
