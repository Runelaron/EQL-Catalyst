namespace EqlTracker.Core;

public sealed record GearPlacementTarget(string Id,string Slot,string Current,bool Allowed,string Reason,IReadOnlyList<string> Displaced,IReadOnlyList<string> Assumptions)
{ public string Label => Slot+" · "+Current; }
public sealed record GearPlacementResult(GearPlacementTarget Target,string Fingerprint,InventoryEntry Entry);

/// <summary>Shared cached evaluation for Add, destination selection and a drag session.</summary>
public sealed class GearPlacementSession
{
    private readonly List<EquipmentPosition> positions;
    private readonly LoadoutEvaluationSession evaluation;
    private readonly string fingerprint;
    public GearPlacementSession(Catalog catalog,LoadoutProfile owner,Dictionary<string,double> weights,List<EquipmentPosition> positions,List<EquipmentPosition> reference,bool theorycraft=false)
    { this.positions=EquipmentPositions.Copy(positions);fingerprint=EquipmentPositions.Fingerprint(positions);evaluation=new(catalog,owner,false,weights,this.positions,reference,theorycraft); }
    public IReadOnlyList<GearPlacementResult> Prepare(ItemDefinition item,int rank,InventoryEntry? instance)
    {
        return positions.Select(position=>{
            var entry=instance==null?new InventoryEntry():EquipmentPositions.Copy(instance);entry.Name=item.Name+(rank>0?" +"+rank:"");entry.ConfirmedItemKey=item.Key;entry.GameId=item.GameId??0;entry.Count=1;entry.Location=position.Slot;
            if(!EquipmentPositions.Fits(item,position,false))return new GearPlacementResult(new(position.Id,position.Label,position.Name,false,"Does not fit "+position.Label,[],[]),fingerprint,entry);
            var result=evaluation.Compare(item,rank,position.Id,instance);
            return new GearPlacementResult(new(position.Id,position.Label,position.Name,result.Eligibility=="Eligible",result.Eligibility,result.Replaced,result.Assumptions),fingerprint,entry);
        }).ToArray();
    }
    public static bool Commit(LoadoutProfile owner,GearDraft? build,GearPlacementResult placement,Catalog catalog,out string error)
    {
        error="";var positions=build?.Positions??EquipmentPositions.For(owner,false);
        if(!placement.Target.Allowed){error=placement.Target.Reason;return false;}
        if(EquipmentPositions.Fingerprint(positions)!=placement.Fingerprint){error="Build changed; choose the slot again.";return false;}
        if(!positions.Any(p=>p.Id==placement.Target.Id)){error="The destination is no longer available.";return false;}
        GearBuildEditing.Remember(owner,build);GearBuildEditing.SetPositions(owner,build,EquipmentPositions.Replace(positions,placement.Target.Id,placement.Entry,catalog));
        if(build?.IsTheorycraft==true)build.Assumptions=placement.Target.Assumptions.ToList();return true;
    }
}
