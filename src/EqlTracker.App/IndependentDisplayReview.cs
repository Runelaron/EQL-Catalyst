using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed partial class OverlayWindow
{
    internal async Task ReviewIndependentDisplays(string folder)
    {
        if(!Controller.Demo||!IsDps)throw new InvalidOperationException("Synthetic overlay review only");
        Directory.CreateDirectory(folder);var checks=new List<string>();var clock=Stopwatch.StartNew();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        void Flush(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        async Task Wait(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>10000)throw new InvalidOperationException("Independent display snapshot timed out");await Task.Delay(30);}}
        void Click(Button button){button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        void Choose(Button button,string value){Click(button);var menu=button.ContextMenu!;Check(menu.IsOpen,"Selector opens · "+button.Tag);menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,value=="Player"?"Self":value)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;}
        void Checked(Button button,string value){Click(button);var menu=button.ContextMenu!;Check(menu.Items.OfType<MenuItem>().Count(i=>i.IsChecked)==1&&menu.Items.OfType<MenuItem>().Single(i=>i.IsChecked).Header?.ToString()==(value=="Player"?"Self":value),"Independent checked choice · "+button.Tag+" · "+value);menu.IsOpen=false;}
        var primary=titleDisplaySelector!;var secondary=(Button)displayBadge.Parent;
        primary.ContextMenu!.Opacity=secondary.ContextMenu!.Opacity=0;
        Controller.Profile.SmoothMeterMotion=false;Controller.Profile.OverlayMeter=new(){Actor="Pet",Scope="Session"};Controller.Profile.OverlayFooterDisplay=new();
        Controller.StartProjectionValidation();await Wait(()=>Controller.Published?.OverlayCombat!=null);
        string lab=JsonSerializer.Serialize(Controller.Profile.Meter);SetMeterSize(440,300);Flush();
        foreach(string main in new[]{"Pet","Player","Combined","Group"})foreach(string lower in new[]{"Pet","Player","Combined","Group"})
        {
            string footerBefore=JsonSerializer.Serialize(Controller.Profile.OverlayFooterDisplay);Choose(primary,main);
            Check(footerBefore==JsonSerializer.Serialize(Controller.Profile.OverlayFooterDisplay),"Primary leaves secondary unchanged · "+main+" / "+lower);
            string mainBefore=JsonSerializer.Serialize(Controller.Profile.OverlayMeter);Choose(secondary,lower);
            Check(mainBefore==JsonSerializer.Serialize(Controller.Profile.OverlayMeter),"Secondary leaves primary unchanged · "+main+" / "+lower);
            await Wait(()=>Controller.Published?.OverlayCombat?.Filter.Owner==Controller.Profile.OverlayMeter.Actor&&((Controller.Published.OverlayGroup!=null)==DpsDisplayChoice.NeedsGroup(Controller.Profile)));
            Refresh();Flush();Checked(primary,main);Checked(secondary,lower);
            double? expected=Controller.Profile.OverlayFooterDisplay.Rate(Controller.Published!.OverlayCombat!,Controller.Published.OverlayGroup);
            Check(secondaryDps.Text==(expected?.ToString("N1")??"—"),"Secondary renders selected rate · "+main+" / "+lower);
            Check(AutomationProperties.GetName(primary)=="DPS display: "+(main=="Player"?"Self":main)&&AutomationProperties.GetName(secondary)=="Secondary DPS display: "+(lower=="Player"?"Self":lower),"Separate accessible labels · "+main+" / "+lower);
        }
        Check(lab==JsonSerializer.Serialize(Controller.Profile.Meter),"All selector combinations leave Combat Lab unchanged");
        Choose(primary,"Group");Choose(secondary,"Pet");await Wait(()=>Controller.Published?.OverlayGroup!=null);Refresh();Flush();
        var profile=JsonSerializer.Deserialize<CharacterProfile>(JsonSerializer.Serialize(Controller.Profile))!;TrackerPresentation.Migrate(profile);
        Check(profile.OverlayMeter!.DisplayMode==OverlayCombatMode.Group&&profile.OverlayFooterDisplay!.Value=="Pet","Different primary and secondary persist through profile reload");
        foreach(var (width,height,name) in new[]{(440d,300d,"independent-full"),(128d,72d,"independent-minimum")})
        {
            SetMeterSize(width,height);Refresh();Flush();Check(primary.IsVisible&&secondary.IsVisible,"Both selectors remain visible · "+name);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture((FrameworkElement)Content,width,height,1)));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);
        }
        Choose(primary,"Pet");Choose(secondary,"Group");await Wait(()=>Controller.Published?.OverlayGroup!=null);Refresh();Flush();
        Check(Controller.Profile.OverlayMeter!.DisplayMode==OverlayCombatMode.Personal&&Controller.Published!.OverlayGroup!=null,"Footer-only Group has a background group projection");
        Choose(secondary,"Combined");await Wait(()=>Controller.Published?.OverlayGroup==null);Refresh();Flush();
        Check(!DpsDisplayChoice.NeedsGroup(Controller.Profile)&&Controller.Published!.OverlayGroup==null,"Unused group projection is released");
        var savedIdentities=Controller.Profile.PetIdentities;string savedOverride=Controller.Profile.PetElementOverride;
        try
        {
            Controller.Profile.PetElementOverride="";
            var start=new DateTime(2026,10,24,10,0,0);
            Controller.Profile.PetIdentities=[new(){Name="Windling",Element="Air",From=start,Until=start.AddMinutes(1),Confirmed=true},new(){Name="Conjuration",Element="Monster",From=start.AddMinutes(1),Confirmed=true}];
            var options=HeaderActions.Children.OfType<Button>().Single(b=>b.ContextMenu==HeadingTitle.ContextMenu);
            var entries=options.ContextMenu!.Items.OfType<MenuItem>().Select(i=>i.Header?.ToString()).ToList();
            Check(entries.Count(x=>x=="Catalyst Toolbar")==1&&!entries.Contains("Combat Lab")&&!entries.Contains("Quest & Gear Tracker"),"Panel options expose one Catalyst Toolbar destination");
            var menuPet=primary.ContextMenu!.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"Pet"));
            Click(primary);Check(((ActorBadge)menuPet.Icon).Element=="Monster"&&menuPet.ToolTip.ToString()!.Contains("Berserker"),"Pet menu resolves the most recent owned Monster and names its family");
            Controller.Profile.PetIdentities[^1].Until=start.AddMinutes(2);
            Controller.Profile.PetIdentities.Add(new(){Name="Unresolved",From=start.AddMinutes(2),Confirmed=true});Refresh();Flush();
            Check(((ActorBadge)menuPet.Icon).Element=="Unknown","An open menu refreshes to generic for a newer unresolved pet");
            Controller.Profile.PetIdentities[^1].Element="Air";Refresh();Flush();
            Check(((ActorBadge)menuPet.Icon).Element=="Air","An open menu immediately follows corrected Air evidence");primary.ContextMenu.IsOpen=false;
        }
        finally{Controller.Profile.PetIdentities=savedIdentities;Controller.Profile.PetElementOverride=savedOverride;Refresh();}
        Json.Write(Path.Combine(folder,"independent-display-checks.json"),new{Passed=checks.Count,Checks=checks,Milliseconds=clock.Elapsed.TotalMilliseconds,AppSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CharacterProfile).Assembly.Location))),Scope="Focused synthetic routed selector/menu events, all 16 combinations, actual background group publication, readout values, persistence and normal/minimum sizing. No live profile or physical game input."});
    }
}
