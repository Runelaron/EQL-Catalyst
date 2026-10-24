namespace EqlTracker.Core;

public static class PetDetectedContext
{
    public const string Version="pet-summon-context-v1";
    public static PetIdentity? Latest(CharacterProfile profile)=>profile.PetIdentities.MaxBy(p=>p.From);
    // Only context changes; physical inventory, effects, identity and combat records are untouched.
    public static bool Apply(CharacterProfile profile,PetBuild build,PetContextCatalog catalog)
    {
        if(!build.FollowDetectedSummon)return false;
        var identity=Latest(profile);if(identity==null)return false;
        string family=PetIdentityResolver.CurrentFamily(profile);
        bool compatible=family==identity.Element&&(identity.CanonicalSummonSpell.Length==0||catalog.Find(family,identity.CanonicalSummonSpell,identity.SummonCasterClass)!=null);
        string spell=compatible?identity.CanonicalSummonSpell:"";
        int? rank=compatible?identity.SummonRank:null;
        string caster=compatible?identity.SummonCasterClass:"";
        bool familyChanged=build.PetType!=family;
        var scenario=new PetBuild{PetType=family,SummonSpell=spell,SummonClass=caster,SummonRank=rank??0,Focus=familyChanged?"":build.Focus};
        var level=rank.HasValue?catalog.Estimate(scenario,profile.ActiveLoadout.Level):new PetLevelEstimate(null,"Estimated level unavailable: summon rank or spell is unknown.");
        string evidence=level.Explanation+"\n"+(identity.SummonedAt is {} at?$"Detected summon {at:MMM dd HH:mm:ss}. ":"Exact summon not recovered. ")+identity.SummonEvidence;
        var before=(build.PetType,build.SummonSpell,build.SummonClass,build.SummonRank,build.SummonRankKnown,build.PetLevel,build.LevelMode,build.LevelEvidence,build.Focus);
        build.PetType=family;build.SummonSpell=spell;build.SummonClass=caster;build.SummonRank=rank??0;build.SummonRankKnown=rank.HasValue;build.PetLevel=level.Level;build.LevelMode=PetLevelMode.Automatic;build.LevelEvidence=evidence;if(familyChanged)build.Focus="";
        return before!=(build.PetType,build.SummonSpell,build.SummonClass,build.SummonRank,build.SummonRankKnown,build.PetLevel,build.LevelMode,build.LevelEvidence,build.Focus);
    }
}
