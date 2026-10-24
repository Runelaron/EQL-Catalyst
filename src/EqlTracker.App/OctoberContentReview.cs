using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    internal void ReviewOctoberCharacter(Action<bool,string> check)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only");
        SelectSection(SettingsSection.Character);
        int classLevel=Controller.Profile.ActiveLoadout.Level;
        race.Text="Half Dark Elf";raceLevel.Text="7";Save();
        check(Controller.Profile.ActiveLoadout.Race=="Ayr'Haeth"&&Controller.Profile.ActiveLoadout.RaceLevel==7&&Controller.Profile.ActiveLoadout.Level==classLevel,"Race settings save aliases and a separate level");
        raceLevel.Text="51";Save();
        check(Controller.Profile.ActiveLoadout.RaceLevel==7&&validation.Text.Contains("Race level"),"Invalid race level retains the saved fact");
        raceLevel.Text="";Save();check(Controller.Profile.ActiveLoadout.RaceLevel==null,"Blank race level remains Unknown");
        raceLevel.Text="7";Save();
        check(petType.Items.Cast<string>().Contains("Monster"),"Settings override includes Monster");
        UpdateLayout();
        foreach(var disclosure in Usability.Tree(Body).OfType<Expander>())if(disclosure.Header?.ToString()=="October content & race abilities")disclosure.IsExpanded=true;
        UpdateLayout();
        check(Usability.Tree(Body).OfType<TextBlock>().Any(t=>t.Text.Contains("Ayr'Haeth")&&t.Text.Contains("Cursed Heritage")),"Offline race reference retains published abilities");
    }
}

public sealed partial class QuestWindow
{
    internal async Task ReviewOctoberContent(string folder,OverlayWindow overlay)
    {
        if(!Controller.Demo)throw new InvalidOperationException("Synthetic review only");
        Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){UpdateLayout();overlay.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        void Capture(string name,FrameworkElement element,double width,double height){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(element,width,height,1)));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);}
        GearBuildFixtures.Apply(Controller);var profile=Controller.Profile;var owner=profile.ActiveLoadout;owner.Classes=profile.CurrentClasses=["MAG","CLR","MNK"];owner.Level=50;
        owner.ConfirmedPetInventory=new(){PetType="Monster",PetLevel=42,SlotConfigurationVersion=PetSlotPlanning.Version};
        Check(OctoberContent.Zones.All(z=>zone.Items.Cast<string>().Contains(z.Name)),"Tracker lists all three sourced zones offline");
        zone.SelectedItem="Nektropos Castle";Flush();Check(zone.ToolTip?.ToString()?.Contains("Retainer Ozval")==true,"New zone selection exposes documented access evidence");
        ShowView(TrackerTab.Pet);RefreshBuildChoices("current");petElement.SelectedItem="Monster";RefreshPet();Flush();
        Check(petElement.SelectedItem?.ToString()=="Monster","Pet workspace selects Monster");
        string inventory=JsonSerializer.Serialize(SelectedPetInventory);
        foreach(double width in new[]{915d,640d}){Width=width;Height=650;Flush();Check(petItems.ActualHeight>180&&petTotals.IsVisible,"Pet layout retains inventory and totals at "+width+" DIP");}
        Width=915;Flush();Capture("monster-pet",(FrameworkElement)Content,915,650);
        var settings=new SettingsWindow(Controller){Owner=this,Left=-15000,Top=-15000,Opacity=0,ShowActivated=false,Width=940,Height=780};settings.Show();settings.ReviewOctoberCharacter(Check);settings.UpdateLayout();Capture("race-settings",(FrameworkElement)settings.Content,940,780);settings.Close();
        var comparison=new PetComparisonWindow(Controller,SelectedPetInventory!,()=>{}){Owner=this,Left=-15000,Top=-15000,Opacity=0,ShowActivated=false};comparison.Show();await comparison.LoadReferences();await comparison.Recalculate();comparison.StopReviewPolling();Flush();
        var monster=comparison.Rows.Single(r=>r.Pet=="Monster");comparison.Results.SelectedItem=monster;comparison.ShowSelected();comparison.UpdateLayout();
        Check(monster.Definition.Classes.Contains("BER")&&monster.DisplayDps==null&&monster.Hp==null,"Monster comparison distinguishes WAR BER equipment from unknown predictions");
        comparison.SelectedDetails.IsExpanded=true;
        foreach(var e in comparison.Details.Children.OfType<Expander>())if(e.Header?.ToString()=="October 22 mechanics")e.IsExpanded=true;
        comparison.UpdateLayout();
        Check(Usability.Tree(comparison).OfType<TextBlock>().Any(t=>t.Text.Contains("Summoned pet HP depends on STA")),"Comparison retains current-patch mechanics evidence");
        foreach(var e in Usability.Tree(comparison).OfType<Expander>())if(e.Header?.ToString()=="Encounter & buff assumptions")e.IsExpanded=true;
        comparison.UpdateLayout();
        Check(Usability.Tree(comparison).OfType<Button>().Any(b=>b.Content?.ToString()=="Measurement checklist"),"Comparison provides measurement help");
        comparison.Results.ScrollIntoView(monster);comparison.UpdateLayout();Capture("monster-comparison",(FrameworkElement)comparison.Content,1180,820);comparison.Close();
        Check(inventory==JsonSerializer.Serialize(SelectedPetInventory),"Settings and comparison preserve Pet inventory");
        var art=new StackPanel{Orientation=Orientation.Horizontal};foreach(var style in new[]{IconArtworkStyle.Clean,IconArtworkStyle.Vibrant})
        {profile.IconStyle=style;IconArtwork.Apply(profile);var badge=new ActorBadge{Width=64,Height=64,Margin=new(8)};badge.Set("Pet",true,"Monster · Warrior/Berserker","Monster");art.Children.Add(badge);Check(IconArtwork.ActorRole("Pet",badge.Element)=="pet-monster","Monster icon identity in "+style);Capture("monster-icon-"+style,badge,64,64);}
        profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(profile);
        string Sha(string path)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        Json.Write(Path.Combine(folder,"october-smoke-checks.json"),new{Passed=checks.Count,Checks=checks,AppSha256=Sha(typeof(App).Assembly.Location),CoreSha256=Sha(typeof(CharacterProfile).Assembly.Location),Scope="Focused synthetic review; broad theme/DPI matrices deferred."});
    }
}
