using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace EqlTracker.Core;

public enum CurrentBackupChoice { Cancel, Replace, Save }
public static class BuildActivation
{
    public static string GearFingerprint(LoadoutProfile owner)=>EquipmentPositions.Fingerprint(EquipmentPositions.For(owner,false));
    public static string PetFingerprint(PetBuild build){PetInventoryEditing.EnsureIdentities(build);return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(PetBuildEditing.Capture(build,"Current"),Json.Options))));}
    private static bool Choice(CurrentBackupChoice choice,string name,out string error)
    { error=choice==CurrentBackupChoice.Cancel?"Cancelled.":choice==CurrentBackupChoice.Save&&string.IsNullOrWhiteSpace(name)?"Enter a backup build name.":"";return error.Length==0; }
    public static bool Gear(LoadoutProfile owner,GearDraft plan,string expected,CurrentBackupChoice choice,string name,out string error)
    {
        if(plan.IsTheorycraft){error="Theorycraft builds cannot become Current.";return false;}
        if(!Choice(choice,name,out error))return false;
        if(GearFingerprint(owner)!=expected){error="Current changed while the prompt was open.";return false;}
        if(choice==CurrentBackupChoice.Save)GearBuildEditing.Create(owner,name.Trim());
        return GearBuildEditing.UseCurrent(owner,plan,out error);
    }
    public static bool Pet(LoadoutProfile owner,PetBuild plan,Catalog catalog,string expected,CurrentBackupChoice choice,string name,out string error)
    {
        if(plan.IsTheorycraft){error="Theorycraft builds cannot become Current.";return false;}
        if(!Choice(choice,name,out error))return false;
        error=PetBuildEditing.ContextError(plan.PetLevel,plan.SummonRank,true)??"";if(error.Length>0)return false;
        var current=PetInventoryEditing.Confirmed(owner,catalog);
        if(PetFingerprint(current)!=expected){error="Current changed while the prompt was open.";return false;}
        var backup=choice==CurrentBackupChoice.Save?GuidedPetBuilds.Clone(current,name.Trim()):null;
        if(!PetBuildEditing.Activate(owner,plan,catalog,out error))return false;
        if(backup!=null)owner.PetBuilds.Add(backup);return true;
    }
}
