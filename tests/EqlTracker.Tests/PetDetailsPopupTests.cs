using System.Text.Json;
using EqlTracker.Core;

internal static class PetDetailsPopupTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool ok){if(!ok)throw new Exception("Pet details popup assertion failed");}
        var catalog=PetContextCatalog.Load(Path.Combine(AppContext.BaseDirectory,"data","pet-context.json"));
        var at=new DateTime(2026,10,22,12,0,0,DateTimeKind.Local);
        CharacterProfile Profile()=>new(){Loadouts=[new(){Level=50,Classes=["MAG","WAR","SHD"]}],PetIdentities=[new(){Name="Mistling",From=at.AddSeconds(5),Element="Air",ElementConfidence="Correlated summon",CanonicalSummonSpell="Greater Conjuration: Air",RawSummonSpell="Greater Conjuration: Air X",SummonRank=10,SummonCasterClass="MAG",SummonedAt=at}]};
        test("Pet details popup: longest catalog match separates spell line and upgrade suffix",()=>{var value=catalog.ResolveCast("Monster Summoning II VIII")!;Check(value.Spell.Name=="Monster Summoning II"&&value.Rank==8);Check(catalog.ResolveCast("Monster Summoning II")!.Rank==0);Check(catalog.ResolveCast("Greater Conjuration: Air X")!.Rank==10);Check(catalog.ResolveCast("Greater Conjuration: Air +13")!.Rank==13);Check(catalog.ResolveCast("Greater Conjuration: Air IIX")==null);});
        foreach(var family in catalog.Spells.GroupBy(s=>s.PetType))
        {var spell=family.First();test("Pet details popup: catalog family "+spell.PetType,()=>{var detector=new PetElementDetector(catalog);var p=new CharacterProfile();detector.Process(new(){Time=at,PlayerCast=spell.Name+" IV"},p);Check(p.PetIdentities.Count==0);p.PetIdentities.Add(new(){Name="Mistling",From=at.AddMinutes(4)});detector.Process(new(){Time=at.AddMinutes(4),PetName="Mistling"},p);var identity=p.PetIdentities.Single();Check(identity.Element==spell.PetType&&identity.CanonicalSummonSpell==spell.Name&&identity.SummonRank==4&&identity.SummonedAt==at);});}
        test("Pet details popup: aliases resolve before suffix parsing",()=>{var spell=catalog.Spells.First(s=>s.Aliases.Count>0);var value=catalog.ResolveCast(spell.Aliases[0]+" II")!;Check(value.Spell.Name==spell.Name&&value.Rank==2);});
        test("Pet details popup: failed summon clears provisional spell metadata",()=>{var p=Profile();p.PetIdentities.Clear();var d=new PetElementDetector(catalog);d.Process(new(){Time=at,PlayerCast="Greater Conjuration: Air X"},p);p.PetIdentities.Add(new(){Name="Mistling",From=at.AddSeconds(5)});d.Process(new(){Time=at.AddSeconds(5),PetName="Mistling"},p);d.Process(new(){Time=at.AddSeconds(6),CastFailed=true,FailedSpell="Greater Conjuration: Air X"},p);Check(p.PetIdentities[0].CanonicalSummonSpell==""&&p.PetIdentities[0].SummonRank==null);});
        test("Pet details popup: reviewed family permits compatible metadata recovery",()=>{var p=Profile();var id=p.PetIdentities[0];id.ElementConfidence="User reviewed";id.CanonicalSummonSpell="";id.SummonRank=null;id.From=at.AddMinutes(5);var d=new PetElementDetector(catalog);d.Process(new(){Time=at,PlayerCast="Greater Conjuration: Air X"},p);d.Process(new(){Time=at.AddMinutes(5),PetName=id.Name},p);Check(id.CanonicalSummonSpell=="Greater Conjuration: Air"&&id.SummonRank==10&&id.ElementConfidence=="User reviewed");});
        test("Pet details popup: current follows without editing inventory or identity",()=>{var p=Profile();var b=new PetBuild{PetType="Earth",Items=[new(){Id="physical",Name="Armor",Rank=7}]};string inventory=JsonSerializer.Serialize(b.Items),identity=JsonSerializer.Serialize(p.PetIdentities);Check(PetDetectedContext.Apply(p,b,catalog));Check(b.PetType=="Air"&&b.SummonRank==10&&b.SummonRankKnown&&b.LevelMode==PetLevelMode.Automatic&&b.PetLevel==50);Check(inventory==JsonSerializer.Serialize(b.Items)&&identity==JsonSerializer.Serialize(p.PetIdentities));Check(!PetDetectedContext.Apply(p,b,catalog));});
        test("Pet details popup: unknown rank stays unknown and existing identity override cannot mix spells",()=>{var p=Profile();p.PetIdentities[0].SummonRank=null;var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);Check(!b.SummonRankKnown&&b.PetLevel==null);p.PetElementOverride="Water";PetDetectedContext.Apply(p,b,catalog);Check(b.PetType=="Water"&&b.SummonSpell==""&&!b.SummonRankKnown&&p.EffectivePetElement=="Water");});
        test("Pet details popup: manual and planned contexts remain independent",()=>{var p=Profile();var b=new PetBuild{FollowDetectedSummon=false,PetType="Earth",SummonSpell="Conjuration: Earth",SummonRank=4};string before=JsonSerializer.Serialize(b);Check(!PetDetectedContext.Apply(p,b,catalog)&&before==JsonSerializer.Serialize(b)&&p.EffectivePetElement=="Air");});
        test("Pet details popup: observed ranks above ten survive editing and Undo restores follow state",()=>{var p=Profile();p.PetIdentities[0].SummonRank=13;var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);Check(b.SummonRank==13);PetBuildEditing.Remember(b,"Override");b.FollowDetectedSummon=false;b.SummonRankKnown=false;PetBuildEditing.Undo(b,out _);Check(b.FollowDetectedSummon&&b.SummonRankKnown&&b.SummonRank==13);Check(PetBuildEditing.ApplyContext(b,b.PetType,b.PetLevel,b.SummonSpell,13,"",true,out _));});
        test("Pet details popup: nullable identity and follow mode survive restart",()=>{var p=Profile();p.PetIdentities[0].SummonRank=null;var copy=EquipmentPositions.Copy(p);Check(copy.PetIdentities[0].SummonRank==null&&copy.PetIdentities[0].RawSummonSpell.EndsWith("X"));Check(EquipmentPositions.Copy(new PetBuild{FollowDetectedSummon=false}).FollowDetectedSummon==false);});
        test("Pet details popup: missing catalog never invents a canonical spell or rank",()=>{var d=new PetElementDetector(new());var p=new CharacterProfile();d.Process(new(){Time=at,PlayerCast="Greater Conjuration: Air X"},p);p.PetIdentities.Add(new(){Name="Mistling",From=at.AddSeconds(5)});d.Process(new(){Time=at.AddSeconds(5),PetName="Mistling"},p);Check(p.PetIdentities[0].Element=="Air"&&p.PetIdentities[0].CanonicalSummonSpell==""&&p.PetIdentities[0].SummonRank==null);});
        test("Pet details popup: related capture keeps unknown resists and invalid HP out of totals",()=>{var p=Profile();var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);var c=new Catalog();var sheet=new PetStatSheet{Reviewed=true,Context=new(){Pet="Air",Summon=b.SummonSpell,Rank=10,Level=50,Patch=PetDefinition.Version,Equipment="Narrative seven-item evidence"}};sheet.Values["HP"]=-1;sheet.Values["STR"]=204;sheet.Validate();var state=new PetResearchState{Sheets=[sheet]};var result=PetDetailsStatistics.Project(b,p.ActiveLoadout,c,state);Check(!result.MatchingEquipment&&result.Observed["HP"]==null&&result.Observed["ResistFire"]==null&&result.Observed["STR"]==204&&result.Capture.Contains("Related"));b.SummonRank=9;Check(PetDetailsStatistics.Project(b,p.ActiveLoadout,c,state).Observed.Count==0);});
        test("Pet details popup: mismatched equipment fingerprint cannot supply current totals",()=>{var p=Profile();var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);var sheet=new PetStatSheet{Reviewed=true,Context=new(){Pet="Air",Summon=b.SummonSpell,Rank=10,Level=50,Patch=PetDefinition.Version,Equipment=new string('A',64)}};sheet.Values["HP"]=7000;var result=PetDetailsStatistics.Project(b,p.ActiveLoadout,new(),new(){Sheets=[sheet]});Check(result.Observed.Count==0&&result.Related.Count==1);});
        test("Pet details popup: character sheet preserves capture context and every recorded stat",()=>
        {
            var p=Profile();var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);
            var sheet=new PetStatSheet{Reviewed=true,Label="Synthetic sheet",CapturedAt=new DateTimeOffset(at),Context=new(){Pet="Air",Summon=b.SummonSpell,Rank=10,Level=50,Patch=PetDefinition.Version,Equipment="Narrative evidence",Buffs="Unbuffed"},Effects="Recorded effect",Weapons="Recorded weapon"};
            foreach(string field in PetResearch.StatFields)sheet.Values[field]=123;
            var result=PetDetailsStatistics.Project(b,p.ActiveLoadout,new(),new(){Sheets=[sheet]});
            Check(PetResearch.StatFields.All(f=>result.Observed[f]==123)&&result.ObservedLevel==50&&result.CaptureName==sheet.Label&&result.CapturedAt==sheet.CapturedAt&&result.Conditions=="Unbuffed"&&result.Effects==sheet.Effects&&result.WeaponEvidence==sheet.Weapons&&!result.MatchingEquipment);
        });
        test("Pet details popup: invalid current resources and nonfinite sheet values stay unknown",()=>
        {
            var p=Profile();var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);
            var sheet=new PetStatSheet{Reviewed=true,Context=new(){Pet="Air",Summon=b.SummonSpell,Rank=10,Level=50,Patch=PetDefinition.Version,Equipment="Narrative evidence"}};
            sheet.Values["HP"]=-1;sheet.Values["HPCurrent"]=-1;sheet.Values["Mana"]=0;sheet.Values["ManaCurrent"]=0;sheet.Values["STR"]=double.NaN;
            var result=PetDetailsStatistics.Project(b,p.ActiveLoadout,new(),new(){Sheets=[sheet]});
            Check(result.Observed["HP"]==null&&result.Observed["HPCurrent"]==null&&result.Observed["STR"]==null&&result.Observed["Mana"]==0&&result.Observed["ManaCurrent"]==0);
        });
        test("Pet details popup: confirmed profile retains its actual level and capture date",()=>
        {
            var p=Profile();var b=new PetBuild();PetDetectedContext.Apply(p,b,catalog);var c=new Catalog();
            var context=new LoadoutProfile{PetType=b.PetType,PetLevel=b.PetLevel,PetSummonSpell=b.SummonSpell,PetSummonRank=b.SummonRank,PetFocus=b.Focus};
            var equipment=PetEquipmentInference.Build(PetEquipmentProjection.Prepare(b,p.ActiveLoadout,c),p.ActiveLoadout,c);
            b.StatProfiles.Add(new(){Name="Synthetic profile",ConfirmedAt=at,Level=50,Classes=p.ActiveLoadout.Classes.ToList(),PetSignature=LoadoutEvaluationSession.PetSignature(context),EquipmentFingerprint=EquipmentPositions.Fingerprint(equipment.Positions),Stats=new(){["HP"]=new(){Value=6400},["HPCurrent"]=new(){Value=-1}},Buffs="Unbuffed"});
            var result=PetDetailsStatistics.Project(b,p.ActiveLoadout,c);
            Check(result.ObservedLevel==50&&result.CaptureName=="Synthetic profile"&&result.CapturedAt==new DateTimeOffset(at)&&result.MatchingEquipment&&!result.Observed.ContainsKey("HPCurrent")&&result.Observed["HP"]==6400);
        });
    }
}
