namespace EqlTracker.Core;

internal sealed record PetSheetWeapon(string Hand,string Name,int? Rank,string State,
    IReadOnlyDictionary<string,double> Stats,double? Accuracy,double? Weight,bool BaseWeight,string Evidence);

internal sealed record PetSheetLoadout(bool Complete,IReadOnlyList<PetSheetWeapon> Weapons)
{
    internal const string Version="pet-sheet-loadout-v2";
    internal static readonly PetSheetLoadout Empty=new(false,[]);
    internal static PetSheetLoadout Build(PetBuild build,LoadoutProfile owner,Catalog catalog,EffectivePetEquipment equipment)
    {
        bool complete=build.Items.Where(i=>(i.ItemKey.Length>0||i.Name.Length>0)&&i.Position<=PetEquipmentPlanner.Capacity(owner.Classes))
            .All(i=>(catalog.FindKey(i.ItemKey)??catalog.Find(i.Name))?.MetadataComplete==true);
        var weapons=new List<PetSheetWeapon>();
        foreach(string hand in new[]{"Primary","Secondary"})
        {
            var position=equipment.Positions.FirstOrDefault(p=>p.Slot==hand);
            var entry=position?.Item;
            if(entry==null)
            {
                bool dormant=hand=="Secondary"&&!build.DualWield&&(build.DormantSecondary!=null||build.Items.Any(i=>i.Slot==hand||i.Position==2));
                weapons.Add(new(hand,dormant?"Inactive Secondary":"None",null,dormant?"Inactive":"Empty",new Dictionary<string,double>(),null,null,false,dormant?"Dual wield is off; retained Secondary does not contribute.":"No active weapon assigned to this hand."));continue;
            }
            var definition=catalog.FindKey(entry.ConfirmedItemKey)??catalog.Find(entry.Name);
            if(definition==null){weapons.Add(new(hand,entry.Name,entry.Rank,"Metadata missing",new Dictionary<string,double>(),null,null,false,"Item metadata is unavailable."));continue;}
            var projection=RankedItemStats.Resolve(definition,entry.Rank);
            bool weapon=ItemEquipmentIcons.IsWeapon(definition)||definition.Stats.GetValueOrDefault("Delay")>0;
            bool verified=equipment.Selections.Any(s=>s.PositionId==position!.Id&&s.Verified);
            weapons.Add(new(hand,definition.Name,entry.Rank,weapon?verified?"User-assigned hand":"Inferred hand":"Non-weapon",projection.Stats,projection.Accuracy,projection.Weight,projection.WeightIsBase,
                projection.Evidence+"\n"+(weapon?(verified?"User-assigned loadout hand; this does not establish automatic client observation.":"Effective loadout assignment; confirm equipped hand in game."):"This hand contains a non-weapon item; no weapon damage, delay or ratio is contributed.")));
        }
        return new(complete,weapons);
    }
}
