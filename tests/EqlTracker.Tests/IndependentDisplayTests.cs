using System.Text.Json;
using EqlTracker.Core;

internal static class IndependentDisplayTests
{
    internal static void Register(Action<string,Action> test)
    {
        void Check(bool ok) { if(!ok)throw new Exception("Independent display assertion failed"); }
        foreach(string original in new[]{"Pet","Player","Combined","Group"})
            test("Independent DPS: legacy "+original+" initializes footer once",()=>{
                var profile=new CharacterProfile{OverlayMeter=new(){Actor=original=="Group"?"Player":original,DisplayMode=original=="Group"?OverlayCombatMode.Group:OverlayCombatMode.Personal,Scope="Session",Ability="Fire"}};
                string before=JsonSerializer.Serialize(profile.OverlayMeter);TrackerPresentation.Migrate(profile);
                Check(profile.OverlayFooterDisplay!.Value==original&&before==JsonSerializer.Serialize(profile.OverlayMeter));
                profile.OverlayFooterDisplay.Select(original=="Pet"?"Combined":"Pet");string footer=JsonSerializer.Serialize(profile.OverlayFooterDisplay);
                TrackerPresentation.Migrate(profile);Check(footer==JsonSerializer.Serialize(profile.OverlayFooterDisplay));
                var restored=JsonSerializer.Deserialize<CharacterProfile>(JsonSerializer.Serialize(profile))!;TrackerPresentation.Migrate(restored);
                Check(before==JsonSerializer.Serialize(restored.OverlayMeter)&&footer==JsonSerializer.Serialize(restored.OverlayFooterDisplay));
            });
        test("Independent DPS: all sixteen actor pairs preserve separate choices and scope",()=>{
            foreach(string main in new[]{"Pet","Player","Combined","Group"})foreach(string footer in new[]{"Pet","Player","Combined","Group"})
            {
                var profile=new CharacterProfile{OverlayMeter=new(){Actor=main=="Group"?"Combined":main,DisplayMode=main=="Group"?OverlayCombatMode.Group:OverlayCombatMode.Personal,Scope="Session",Target="Target",Kind="Melee",Sort="Hits"}};
                TrackerPresentation.Migrate(profile);string savedMain=JsonSerializer.Serialize(profile.OverlayMeter),savedLab=JsonSerializer.Serialize(profile.Meter);
                profile.OverlayFooterDisplay!.Select(footer);
                Check(profile.OverlayFooterDisplay.Value==footer&&savedMain==JsonSerializer.Serialize(profile.OverlayMeter)&&savedLab==JsonSerializer.Serialize(profile.Meter));
                Check(DpsDisplayChoice.NeedsGroup(profile)==(main=="Group"||footer=="Group"));
            }
        });
        test("Independent DPS: personal rates use common denominator without primary actor",()=>{
            var view=new DpsViewState([],new("Pet","","","",false,null,null),[],10,200,20,30,50,0,0,0,true,false);
            var choice=new DpsDisplayChoice();foreach(var (actor,rate) in new[]{("Pet",20d),("Player",30d),("Combined",50d)}) { choice.Select(actor);Check(choice.Rate(view,null)==rate&&view.Filter.Owner=="Pet"); }
        });
        test("Independent DPS: missing group coverage stays unavailable and observed zero stays zero",()=>{
            var view=new DpsViewState([],new("Pet","","","",false,null,null),[],10,200,20,30,50,0,0,0,true,false);var choice=new DpsDisplayChoice();choice.Select("Group");
            GroupProjection Projection(long? total)=>new("test",[new("member","Example member",total,null,null,0,"Test",[],[])],10,"Synthetic coverage");
            Check(choice.Rate(view,null)==null&&choice.Rate(view,Projection(null))==null&&choice.Rate(view,Projection(0))==0&&choice.Rate(view,Projection(700))==70);
        });
        test("Independent DPS: invalid actor rejected without changing saved selection",()=>{
            var choice=new DpsDisplayChoice{Actor="Combined"};try{choice.Select("Incoming");throw new Exception("Invalid choice accepted");}catch(ArgumentException){}Check(choice.Value=="Combined");
        });
    }
}
