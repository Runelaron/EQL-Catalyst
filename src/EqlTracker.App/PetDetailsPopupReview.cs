using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    internal async Task ReviewPetDetailsPopup(string folder)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review required");
        Directory.CreateDirectory(folder);var checks=new List<string>();var clock=Stopwatch.StartNew();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);petDetailsPopup?.UpdateLayout();}
        async Task Wait(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>10000)throw new InvalidOperationException("Popup review timed out");await Task.Delay(25);Flush();}}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Capture(string name,FrameworkElement content,double width,double height){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content,width,height,1)));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);}
        GearBuildFixtures.Apply(Controller);var owner=Controller.Profile.ActiveLoadout;owner.Classes=["MAG","BRD","SHD"];owner.Level=50;Controller.Profile.PetElementOverride="";
        var at=new DateTime(2026,10,22,12,0,0,DateTimeKind.Local);Controller.Profile.PetIdentities=[new(){Name="Mistling",From=at.AddSeconds(5),Element="Air",ElementConfidence="Correlated summon",CanonicalSummonSpell="Greater Conjuration: Air",RawSummonSpell="Greater Conjuration: Air X",SummonRank=10,SummonCasterClass="MAG",SummonedAt=at}];
        var build=PetInventoryEditing.Confirmed(owner,Controller.Catalog);build.FollowDetectedSummon=true;Controller.RefreshDetectedPetContext();
        build.Items.Clear();build.SlotConfigurations.Clear();build.SlotConfigurationVersion=0;build.DualWield=true;
        foreach(var (itemName,slot,itemRank) in new[]{("Bloodmoon","Primary",4),("Evensong","Secondary",5),("Indicolite Breastplate","Chest",5),("Gauntlets of Fiery Might","Hands",4)})
        {var item=Controller.Catalog.Find(itemName)!;build.Items.Add(new(){Position=build.Items.Count+1,ItemKey=item.Key,Name=item.Name,Slot=slot,Rank=itemRank,ConfirmedAt=at,EquipmentVerified=true});}
        PetInventoryEditing.EnsureIdentities(build);PetSlotPlanning.Migrate(build,Controller.Catalog);PetSlotPlanning.Sync(build);
        var equipment=PetEquipmentInference.Build(PetEquipmentProjection.Prepare(build,owner,Controller.Catalog),owner,Controller.Catalog);
        var statContext=new LoadoutProfile{PetType=build.PetType,PetLevel=build.PetLevel,PetSummonSpell=build.SummonSpell,PetSummonRank=build.SummonRank,PetFocus=build.Focus};
        build.StatProfiles.Add(new(){Name="Synthetic controlled capture",ConfirmedAt=at,Level=50,Classes=owner.Classes.ToList(),PetSignature=LoadoutEvaluationSession.PetSignature(statContext),EquipmentFingerprint=EquipmentPositions.Fingerprint(equipment.Positions),Stats=new(){
            ["HP"]=new(){Value=6400},["HPCurrent"]=new(){Value=6100},["Mana"]=new(){Value=1200},["Endurance"]=new(){Value=900},
            ["ACLeft"]=new(){Value=510},["ACRight"]=new(){Value=580},["AttackLeft"]=new(){Value=333},["AttackRight"]=new(){Value=300},
            ["STR"]=new(){Value=170},["STA"]=new(){Value=145},["INT"]=new(){Value=65},["WIS"]=new(){Value=60},["AGI"]=new(){Value=155},["DEX"]=new(){Value=140},["CHA"]=new(){Value=25},
            ["AttackSpeedPercent"]=new(){Value=140},["HPRegen"]=new(){Value=1},["ManaRegen"]=new(){Value=4},["ResistFire"]=new(){Value=25},
            ["PrimaryDamage"]=new(){Value=22},["PrimaryDelay"]=new(){Value=26},["PrimaryRatio"]=new(){Value=22d/26},["PrimaryAccuracyPercent"]=new(){Value=8}
        },Buffs="Unbuffed · synthetic",Source="Synthetic fixture"});
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");RefreshPet();Width=915;Height=650;Flush();build=SelectedPetInventory!;
        var button=Usability.Tree(this).OfType<Button>().Single(b=>Equals(b.Content,"Pet Details"));
        Check(Math.Abs(button.ActualHeight-28)<.1,"Pet Details button measures 28 DIP");
        Check(button.Template.FindName("FlatFace",button)!=null&&button.Template.FindName("Arrow",button)==null,"Pet Details restores the flat disclosure face without a dropdown arrow");
        Check(button.Focusable&&button.IsTabStop&&button.Focus(),"Pet Details popup action participates in keyboard focus navigation");
        Check(!Usability.Tree(this).OfType<Expander>().Any(e=>Equals(e.Header,"Pet Details")),"Inline disclosure and reserved content are removed");
        string inventory=JsonSerializer.Serialize(build.Items),identity=JsonSerializer.Serialize(Controller.Profile.PetIdentities);
        Capture("pet-sheet-toolbar",(FrameworkElement)Content,915,650);
        Click(button);var popup=petDetailsPopup!;popup.Left=popup.Top=-15000;popup.Opacity=0;Flush();await Wait(()=>popup.Sheet.HasResult);
        Check(popup.Owner==this&&popup.IsVisible&&petSpell.IsEnabled==false&&petSummonTier.Text=="10","Current popup follows detected spell and rank");
        Click(button);Check(ReferenceEquals(petDetailsPopup,popup),"Repeated open focuses one owned popup");
        popup.Override.IsChecked=true;popup.Override.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));Flush();
        Check(!build.FollowDetectedSummon&&petSpell.IsEnabled,"Build-only override enables controls");
        petSummonTier.Text="9";Flush();Check(build.SummonRank==9&&Controller.Profile.EffectivePetElement=="Air"&&identity==JsonSerializer.Serialize(Controller.Profile.PetIdentities),"Manual rank changes build assumptions without identity changes");
        Click(popup.UseDetected);Flush();Check(build.FollowDetectedSummon&&build.SummonRank==10,"Use detected restores the confirmed summon");
        Check(inventory==JsonSerializer.Serialize(build.Items),"Context changes preserve physical inventory and item ranks");
        Controller.Profile.PetIdentities[0].SummonRank=13;Controller.RefreshDetectedPetContext();popup.RefreshRequested?.Invoke();Flush();Check(petSummonTier.Text=="13"&&build.SummonRank==13&&!petContextNote.Text.Contains("Hypothetical"),"Recorded summon ranks above ten remain displayed");
        Controller.Profile.PetIdentities[0].SummonRank=10;Controller.RefreshDetectedPetContext();popup.RefreshRequested?.Invoke();
        await Wait(()=>popup.Updating.Visibility==Visibility.Collapsed);
        Check(popup.Capture.Text.Contains("Synthetic controlled capture"),"Restoring summon rank restores its compatible observed capture: "+popup.Capture.Text);
        Check(popup.Sheet.Values["HP"].Text=="6100 / 6400"&&popup.Sheet.GearValues.GetValueOrDefault("HP")?.Text!="6100 / 6400","Observed resources stay separate from effective equipment contributions");
        Check(popup.Sheet.Values["Mana"].Text=="1200"&&popup.Sheet.ObservedLevel.Text=="Observed level: 50","Maximum-only resources do not acquire an inferred current value, and observed level comes from capture");
        Check(popup.Sheet.Values["ACLeft"].Text=="510 | 580"&&popup.Sheet.Values["AttackLeft"].Text=="333 | 300","AC and Attack preserve the recorded pair order without invented labels");
        Check(!Usability.Tree(popup).OfType<DataGrid>().Any()&&!Usability.Tree(popup.Sheet).OfType<ScrollViewer>().Any()&&UI.FindScroll(popup)!=null,"Character sheet has no table or nested scrolling and uses the outer scroll surface");
        Check(new[]{"HP","Mana","STR","STA","INT","WIS","AGI","DEX","CHA","PrimaryDamage","SecondaryDamage"}.All(popup.Sheet.Values.ContainsKey)&&Equals(popup.Sheet.Extra.Header,"Known spells & abilities"),"Visible stats and known abilities replace speculative numerical skills and absorption");
        Check(new[]{"Fire","Cold","Magic","Disease","Poison","Void","Corruption"}.All(r=>popup.Sheet.Values.ContainsKey("Resist"+r))&&popup.Sheet.Values["ResistCold"].Text=="+0","All seven resists show signed gear-only contributions including +0");
        Check(((System.Windows.Media.SolidColorBrush)popup.Sheet.Values["HP"].Foreground).Color==UI.Brush("PetHP").Color&&((System.Windows.Media.SolidColorBrush)popup.Sheet.Values["Mana"].Foreground).Color==UI.Brush("PetMana").Color&&!popup.Sheet.Values.ContainsKey("Endurance"),"HP and Mana retain their red/blue resources and Endurance stays historical only");
        Check(popup.Sheet.Added.IsChecked!=true,"Added begins unchecked and is transient to the popup");
        popup.Sheet.Added.IsChecked=true;Flush();Check(popup.Sheet.Values["STR"].Text.Contains(" (+")&&popup.Sheet.Values["HP"].Text.StartsWith("6100 / 6400 (")&&popup.Sheet.Values["ResistCold"].Text=="+0","Added annotates loadout contributions without changing observed resources or resist formatting");
        Capture("pet-sheet-added",(FrameworkElement)popup.Content,740,800);popup.Sheet.Added.IsChecked=false;Flush();
        var expectedWeapon=RankedItemStats.Resolve(Controller.Catalog.Find("Bloodmoon")!,4);Check(popup.Sheet.Values["PrimaryDamage"].Text==expectedWeapon.Stats["Damage"].ToString("0.###")&&popup.Sheet.Values["PrimaryDelay"].Text==expectedWeapon.Stats["Delay"].ToString("0.###"),"Weapon fields use effective ranked loadout values instead of absent captured weapon totals");
        var subtotal=PetDetailsStatistics.Project(build,owner,Controller.Catalog,null);
        var signedStats=new Dictionary<string,double>(subtotal.Equipment.Totals.Stats){["ResistFire"]=-5};
        popup.Sheet.Present(subtotal with {Equipment=subtotal.Equipment with {Totals=subtotal.Equipment.Totals with {Stats=signedStats}},Loadout=subtotal.Loadout with {Complete=false}});
        Check(popup.Sheet.Values["ResistFire"].Text=="-5"&&popup.Sheet.Values["ResistCold"].Text=="+0"&&popup.Sheet.Warnings.Text.Contains("Incomplete"),"Signed negative and zero resists retain incomplete-loadout coverage warnings");
        popup.Sheet.Added.IsChecked=true;popup.Sheet.Present(subtotal with {Observed=new Dictionary<string,double?>()});
        Check(popup.Sheet.Values["STR"].Text=="Unknown (+47)"&&popup.Sheet.Values["AttackSpeedPercent"].Text.Contains("unresolved")&&popup.Sheet.Values["ACLeft"].ToolTip.ToString()!.Contains("mapping is unresolved"),"Unknown totals show known additions without inventing client AC or speed conversions");
        popup.Sheet.Added.IsChecked=false;popup.Sheet.Present(subtotal);
        foreach(var (width,height,label) in new[]{(740d,800d,"normal"),(500d,580d,"minimum"),(500d,420d,"short")})
        {
            popup.Width=width;popup.Height=height;Flush();
            Check(popup.Sheet.ActualWidth<=popup.ActualWidth&&popup.Sheet.Main.IsWide==(width>=740),"Sheet switches between balanced sections and stacked groups at "+label+" width");
            var scroll=UI.FindScroll(popup)!;var close=Usability.Tree(popup).OfType<Button>().Single(b=>Equals(b.Content,"Close"));
            double footerY=close.TranslatePoint(new(),popup).Y;scroll.ScrollToEnd();Flush();
            Check(close.TranslatePoint(new(),popup).Y==footerY&&footerY+close.ActualHeight<=popup.ActualHeight,"Outer scrolling preserves fixed Close action at "+label+" height");
            scroll.ScrollToHome();Flush();Capture("pet-sheet-"+label,(FrameworkElement)popup.Content,width,height);
        }
        popup.Width=740;popup.Height=800;popup.Sheet.Extra.IsExpanded=true;popup.Sheet.Equipment.IsExpanded=true;Flush();UI.FindScroll(popup)!.ScrollToEnd();Flush();Capture("pet-sheet-equipment",(FrameworkElement)popup.Content,740,800);
        popup.Sheet.Extra.IsExpanded=false;popup.Sheet.Equipment.IsExpanded=false;UI.FindScroll(popup)!.ScrollToHome();Flush();
        Check(Usability.Tree(popup).OfType<Button>().Where(b=>b.IsEnabled).All(b=>b.Focusable&&b.IsTabStop),"Popup actions retain keyboard focus access");
        petSpell.Text=new string('W',95)+" · long catalog alias";Flush();Check(petDetailsEditor!.ActualWidth<popup.ActualWidth,"Long summon labels remain contained in wrapping settings");LoadPetContext();
        var partial=PetDetailsStatistics.Project(build,owner,Controller.Catalog,null) with {Observed=new Dictionary<string,double?>{["HP"]=-1,["HPCurrent"]=-1,["Mana"]=0,["ManaCurrent"]=0,["ACLeft"]=123,["AC"]=999,["AbsorptionRemaining"]=5,["EnduranceCurrent"]=222},ObservedLevel=null};
        popup.Sheet.Present(partial);
        Check(popup.Sheet.Values["HP"].Text=="Unknown"&&popup.Sheet.Values["Mana"].Text=="0 / 0"&&popup.Sheet.Values["ACLeft"].Text=="123 | Unknown"&&popup.Sheet.ObservedLevel.Text.EndsWith("Unknown"),"Invalid HP and partial pairs remain unknown while recorded zero resources are preserved");
        popup.Evidence.IsExpanded=true;Flush();
        Check(Usability.Tree(popup.Sheet.AdditionalEvidence).OfType<TextBlock>().Any(t=>t.Text=="Additional recorded AC: 999")&&Usability.Tree(popup.Sheet.AdditionalEvidence).OfType<TextBlock>().Any(t=>t.Text=="Recorded current Endurance: 222 · maximum Unknown"),"Additional scalar and current-only observations remain accessible in Evidence without fabricating pairs");
        popup.Evidence.IsExpanded=false;
        popup.Sheet.Present(PetDetailsStatistics.Project(build,owner,Controller.Catalog,null));
        var noCapture=PetDetailsStatistics.Project(build,owner,Controller.Catalog,null) with {Observed=new Dictionary<string,double?>(),CaptureName="",ObservedLevel=null};
        popup.Sheet.Present(noCapture);Check(popup.Sheet.Values["HP"].Text=="Unknown"&&popup.Sheet.Values["ResistFire"].Text.StartsWith('+')&&popup.Sheet.GearValues.Count>0,"Missing capture retains signed gear resists and contributions while observed resources stay Unknown");
        popup.Sheet.Present(PetDetailsStatistics.Project(build,owner,Controller.Catalog,null));
        var previousHP=popup.Sheet.Values["HP"].Text;popup.QueueStatistics(EquipmentPositions.Copy(build),EquipmentPositions.Copy(owner),Controller.Catalog,null,()=>true);
        Check(popup.Sheet.Values["HP"].Text==previousHP&&popup.Sheet.HasResult,"Recalculation retains completed sheet content");await Wait(()=>popup.Updating.Visibility==Visibility.Collapsed);
        // An obsolete asynchronous result must never repopulate a cleared selection.
        popup.QueueStatistics(EquipmentPositions.Copy(build),EquipmentPositions.Copy(owner),Controller.Catalog,null,()=>true);build.SummonSpell="";build.FollowDetectedSummon=false;popup.RequireSelection();await Task.Delay(120);Check(!popup.Sheet.HasResult,"Clearing selection rejects obsolete stat work");
        build.SummonSpell="Greater Conjuration: Air";build.FollowDetectedSummon=true;
        popup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(popup),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});Flush();Check(petDetailsPopup==null,"Escape closes the popup and releases its editor");
        OpenPetDetails();var reopened=petDetailsPopup!;reopened.Left=reopened.Top=-15000;Check(reopened.IsVisible,"Popup editor can be reopened");
        ShowView(TrackerTab.Items);Check(petDetailsPopup==null,"Changing tab closes the popup immediately");
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");OpenPetDetails();var plan=GuidedPetBuilds.Clone(build,"Independent plan");plan.SummonRank=4;owner.PetBuilds.Add(plan);RefreshBuildChoices(plan.Id);Flush();await Wait(()=>petDetailsPopup==null);
        OpenPetDetails();var planned=petDetailsPopup!;planned.Left=planned.Top=-15000;Flush();Check(petSpell.IsEnabled&&petSummonTier.Text=="4"&&planned.Override.Visibility==Visibility.Collapsed,"Planned popup loads its independent saved context");
        planned.Close();
        Json.Write(Path.Combine(folder,"pet-details-popup-checks.json"),new{Passed=checks.Count,Checks=checks,Milliseconds=clock.Elapsed.TotalMilliseconds,AppSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location)))});
    }
}
