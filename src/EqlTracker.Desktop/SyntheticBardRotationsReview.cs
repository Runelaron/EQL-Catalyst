using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class TrackerController
{
    internal void SeedBardRotationsReview()
    {
        if(!Demo)throw new InvalidOperationException();Profile.CurrentClasses=["BRD","WAR","CLR"];Profile.ActiveLoadout.Classes=[..Profile.CurrentClasses];Profile.ActiveLoadout.Level=50;Profile.Meter.GroupPeriodId="";
        BardSong Song(string name,int rank,string stat,double amount)=>new(){Identity=new(name,name,rank),Available=true,RequiredLevel=1,Mana=0,CooldownMs=0,NonTargetedArea=true,Effects=[new(){Identity=new(name,name,rank),Family=stat,Amount=amount,Quality=EvidenceQuality.UserConfirmed,Source="Synthetic reviewed component",Channel=EffectChannel.Attribute}]};
        var songs=new[]{Song("Battle Verse",2,"STR",30),Song("Hymn of Restoration",3,"HPRegen",12),Song("Psalm of Warmth",0,"ResistFire",30),Song("Clarity Chorus",5,"ManaRegen",8),Song("Selo's Accelerando",4,"MovementSpeed",50)};
        integrated.Bard=new(){ConfirmedAuraRank=5,AuraEvidence="Synthetic rank confirmation",Songs=songs.ToList(),Available=[Song("Psalm of Frost",2,"ResistCold",30)],Instrument=1,Singing=1,Focus=1,EffectiveCasterLevel=50,AaContext="Synthetic reviewed context"};
        var set=new BardRotationSet{Id=BardRotations.Fingerprint(songs),Name="Familiar five-song set",Origin="Repeated observations",Songs=songs.ToList(),First=DateTime.Now.AddMinutes(-20),Last=DateTime.Now,Cycles=90,Episodes=3,ObservedSeconds=900,Sessions=["review-session"],Evidence="Synthetic repeated observations"};
        bardResearch=new(){Current=EquipmentPositions.Copy(set),Common=[set],FileEvidence="Synthetic saved sets"};bardInitialized=true;
        foreach(var song in songs.Concat(integrated.Bard.Available))BardRotations.Learn(bardResearch,song,DateTime.Now.AddMinutes(-25),"Synthetic memorization");
        Profile.GroupMembers.Add(new(){Name="OtherBard",From=DateTime.Now.AddHours(-1)});
        var other=Song("Battle Verse",6,"STR",40);integrated.Coverage=[new(){Id="synthetic-cover",Song=other.Identity,Caster="OtherBard",Recipient=Profile.Character,At=DateTime.Now.AddMinutes(-1),Until=DateTime.Now.AddHours(1),SuccessfulApplication=true,Attribution=EvidenceQuality.Observed,Effects=other.Effects,Source="Synthetic recipient-qualified landing"}];
    }
    internal void AdvanceBardReviewCounter(){bardResearch.Common[0].Cycles++;bardResearch.Current!.Last=DateTime.Now;}
    internal void ChangeBardReviewCurrent(){var current=EquipmentPositions.Copy(bardResearch.Current!);current.Id="different-detected";current.Name="Another detected set";bardResearch.Current=current;}
}
