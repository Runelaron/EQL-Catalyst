using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static class PetArtworkReview
{
    internal static async Task Run(TrackerController controller,OverlayWindow overlay,string folder)
    {
        if(!controller.Demo)throw new InvalidOperationException("Synthetic review only");Directory.CreateDirectory(folder);
        var checks=new List<string>();var timer=Stopwatch.StartNew();
        void Check(bool value,string label){if(!value){Json.Write(Path.Combine(folder,"partial-pet-art-checks.json"),new{Checks=checks,Failed=label});throw new InvalidOperationException(label);}checks.Add(label);}
        if(Environment.GetCommandLineArgs().Contains("--art-restore-only"))
        {Check(controller.UndeadArtwork=="wraith-b"&&IconArtwork.Undead=="wraith-b","Separate process applies the saved character's Wraith B before rendering");Json.Write(Path.Combine(folder,"art-restored.json"),new{Passed=true,controller.UndeadArtwork});return;}
        string identity=PetResearch.Hash(controller.Profile.PetIdentities),main=PetResearch.Hash(controller.Profile.OverlayMeter!),footer=PetResearch.Hash(controller.Profile.OverlayFooterDisplay!),combat=PetResearch.Hash(controller.Combat.Encounters);
        string original=controller.UndeadArtwork;var priorStyle=controller.Profile.IconStyle;
        void Flush(Window window){window.UpdateLayout();window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();}
        void Save(string name,FrameworkElement visual,double width,double height)
        {var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(visual,width,height,1)));using var stream=File.Create(Path.Combine(folder,name+".png"));png.Save(stream);}
        string Pixels(string family,int dip)
        {
            var badge=new ActorBadge{Width=dip,Height=dip};badge.Set("Pet",false,"Synthetic family rendering",family);
            var bitmap=PreviewRenderer.Capture(badge,dip,dip,1);byte[] buffer=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(buffer,bitmap.PixelWidth*4,0);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer));
        }
        Check(PetArtworkPreferences.Resolve(null)=="skeleton-a"&&PetArtworkPreferences.Resolve("unsupported")=="skeleton-a","Missing and unsupported Undead choices use Skeleton A");
        string stateFolder=Path.Combine(folder,"state-tests");Directory.CreateDirectory(stateFolder);string statePath=Path.Combine(stateFolder,"character-a.db");
        using(var store=new TrackerStore(statePath))
        {
            Check(PetArtworkPreferences.Read(store).Undead=="skeleton-a","A new character needs no preference migration");
            store.Set(PetArtworkPreferences.StateKey,new{Undead=42});Check(PetArtworkPreferences.Read(store).Undead=="skeleton-a","An unsupported persisted value falls back without preventing profile startup");
            new PetArtworkPreferences{Undead="wraith-a"}.Write(store);Check(PetArtworkPreferences.Read(store).Undead=="wraith-a","Wraith A round-trips through the character state store");
            new PetArtworkPreferences{Undead="wraith-b"}.Write(store);
        }
        using(var store=new TrackerStore(statePath))Check(PetArtworkPreferences.Read(store).Undead=="wraith-b","Reopening the character database retains Wraith B");
        using(var store=new TrackerStore(Path.Combine(stateFolder,"character-b.db")))Check(PetArtworkPreferences.Read(store).Undead=="skeleton-a","A separate character does not inherit another character's appearance");
        var settings=new SettingsWindow(controller){Opacity=0,ShowActivated=false};settings.Show();Flush(settings);
        Check(settings.UndeadChoice.Items.Count==3&&Equals(settings.UndeadChoice.SelectedValue,original),"Settings offers exactly Skeleton A, Wraith A and Wraith B with the saved choice selected");
        Check(settings.UndeadChoice.Focusable&&KeyboardNavigation.GetIsTabStop(settings.UndeadChoice)&&System.Windows.Automation.AutomationProperties.GetName(settings.UndeadChoice)=="Undead appearance","Undead choice remains a labeled keyboard tab stop");
        settings.UndeadChoice.Focus();settings.UndeadChoice.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(settings)!,Environment.TickCount,Key.F4){RoutedEvent=Keyboard.KeyDownEvent});Flush(settings);
        Check(settings.UndeadChoice.IsDropDownOpen,"F4 opens the real Undead ComboBox through its keyboard handler");settings.UndeadChoice.IsDropDownOpen=false;
        var hashes=new HashSet<string>();
        foreach(var option in SettingsWindow.UndeadChoices)
        {
            settings.UndeadChoice.SelectedValue=option.Id;Flush(settings);
            Check(controller.UndeadArtwork==option.Id&&IconArtwork.ActorRole("Pet","Undead")==option.Role,"Settings immediately routes "+option.Name+" through the shared renderer");
            hashes.Add(Pixels("Undead",24));
        }
        Check(hashes.Count==3,"All three Undead settings produce distinct small-size artwork");
        controller.Profile.IconStyle=IconArtworkStyle.Clean;IconArtwork.Apply(controller.Profile,controller.UndeadArtwork);hashes.Clear();
        foreach(var option in SettingsWindow.UndeadChoices){settings.UndeadChoice.SelectedValue=option.Id;Flush(settings);hashes.Add(Pixels("Undead",24));}
        Check(hashes.Count==3,"Clean preserves three distinct Undead silhouettes");
        controller.Profile.IconStyle=IconArtworkStyle.Vibrant;IconArtwork.Apply(controller.Profile,controller.UndeadArtwork);Flush(settings);int cache=IconArtwork.CachedImages;_=Pixels("Undead",13);
        Check(IconArtwork.CachedImages==cache,"Small approved artwork renders without decoding an additional painted bitmap");
        Check(SettingsWindow.UndeadChoices.All(c=>IconArtwork.Painted(c.Role,192)?.PixelWidth>=192),"Larger high-DPI Undead artwork decodes at the required device resolution");
        var other=new CharacterProfile{Character="Other",Server="Test",IconStyle=controller.Profile.IconStyle};
        Check(IconArtwork.Apply(other,"skeleton-a")&&IconArtwork.Undead=="skeleton-a","Character changes invalidate artwork even when the overall icon style is unchanged");
        IconArtwork.Apply(controller.Profile,controller.UndeadArtwork);Check(IconArtwork.Undead=="wraith-b","Returning to the prior character restores its appearance");
        Check(!IconArtwork.Apply(controller.Profile,controller.UndeadArtwork),"Equivalent appearance refreshes do not increment the rendering revision");
        foreach(string family in new[]{"Water","Fire","Monster","Undead"})
        {
            Check(IconArtwork.ActorRole("Pet",family).StartsWith("pet-",StringComparison.Ordinal),family+" resolves to explicit family artwork");
            foreach(var style in Enum.GetValues<IconArtworkStyle>())
            {
                controller.Profile.IconStyle=style;IconArtwork.Apply(controller.Profile,controller.UndeadArtwork);
                var row=new StackPanel{Orientation=Orientation.Horizontal,Background=UI.Brush("SolidSurface")};row.Children.Add(UI.Text(family+" · "+style,12));
                foreach(int dip in new[]{13,14,16,20,24,32,64}){var badge=new ActorBadge{Width=dip,Height=dip,Margin=new(6)};badge.Set("Pet",true,"Synthetic "+family,family);row.Children.Add(badge);}Save("approved-"+family.ToLowerInvariant()+"-"+style.ToString().ToLowerInvariant(),row,560,90);
            }
        }
        Check(IconArtwork.ActorRole("Pet","Unknown")=="pet-generic"&&IconArtwork.ActorRole("Player","Undead")=="actor-self","Unknown pets retain generic artwork and player selectors retain player artwork");
        Check(!ApprovedPetArtwork.Detailed(24,IconArtworkStyle.Vibrant)&&ApprovedPetArtwork.Detailed(32,IconArtworkStyle.Vibrant)&&!ApprovedPetArtwork.Detailed(64,IconArtworkStyle.Clean),"Approved families choose detail by DIP size independently of DPI");
        var decode=new List<object>();foreach(double dpi in new[]{1d,1.5,2d,3d})foreach(int dip in new[]{13,14,16,20,24,32,64})decode.Add(new{DIP=dip,DPI=dpi,Pixels=PetIconCandidateView.DevicePixels(dip,dpi),Detailed=ApprovedPetArtwork.Detailed(dip,IconArtworkStyle.Vibrant)});
        controller.Profile.IconStyle=priorStyle;settings.UndeadChoice.SelectedValue=original;IconArtwork.Apply(controller.Profile,controller.UndeadArtwork);
        settings.Width=940;settings.Height=610;Flush(settings);Save("undead-settings",(FrameworkElement)settings.Content,settings.ActualWidth,settings.ActualHeight);
        settings.MinWidth=640;settings.Width=640;settings.MinHeight=420;settings.Height=420;Flush(settings);Save("undead-settings-minimum",(FrameworkElement)settings.Content,settings.ActualWidth,settings.ActualHeight);
        Check(settings.UndeadChoice.ActualWidth>=160&&settings.UndeadChoice.ActualHeight>=24,"Undead selector retains usable bounds at minimum Settings width");
        settings.Hide();controller.SetUndeadArtwork(original);controller.Profile.IconStyle=priorStyle;IconArtwork.Apply(controller.Profile,controller.UndeadArtwork);
        Check(identity==PetResearch.Hash(controller.Profile.PetIdentities)&&combat==PetResearch.Hash(controller.Combat.Encounters),"Appearance changes preserve detected identities and historical combat snapshots");
        Check(main==PetResearch.Hash(controller.Profile.OverlayMeter!)&&footer==PetResearch.Hash(controller.Profile.OverlayFooterDisplay!),"Appearance changes preserve both independent DPS selectors");
        string restart=Path.Combine(folder,"restart");Directory.CreateDirectory(Path.Combine(restart,".preview-state"));using(var store=new TrackerStore(Path.Combine(restart,".preview-state","tracker.db")))new PetArtworkPreferences{Undead="wraith-b"}.Write(store);
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")start.ArgumentList.Add(typeof(App).Assembly.Location);
        foreach(string arg in new[]{"--preview",restart,"--pet-art-only","--art-restore-only","--character","Sample","--server","Test","--offline"})start.ArgumentList.Add(arg);
        using(var child=Process.Start(start)!){await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));Check(child.ExitCode==0&&Json.Read<Dictionary<string,object>>(Path.Combine(restart,"art-restored.json")).ContainsKey("Passed"),"Separate-process restart loads the stored appearance before first render");}
        await overlay.ReviewPetIconCandidates(folder);
        Json.Write(Path.Combine(folder,"pet-art-checks.json"),new{Passed=checks.Count,Checks=checks,SizePolicy=decode,PrivateResearch=TrackerController.PetResearchEnabled,Milliseconds=timer.Elapsed.TotalMilliseconds,AppSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),CoreSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Catalog).Assembly.Location)))});
    }
}
