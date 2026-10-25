using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EqlTracker.Core;

namespace EqlTracker.App;

internal static partial class PetArtworkReview
{
    private static void ReviewFamilies(TrackerController controller,SettingsWindow settings,string folder,Action<bool,string> check)
    {
        var original=controller.ArtworkPreferences;
        var style=controller.Profile.IconStyle;
        void Apply()=>IconArtwork.Apply(controller.Profile,controller.UndeadArtwork,controller.FireArtwork,controller.ArtworkPreferences);
        void Flush(){settings.UpdateLayout();settings.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);settings.UpdateLayout();}
        void Save(string name,FrameworkElement content,double width,double height,double scale)
        {
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(PreviewRenderer.Capture(content,width,height,scale)));
            using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
        }
        var cases=new[]{("Earth",settings.EarthChoice,SettingsWindow.EarthChoices),("Air",settings.AirChoice,SettingsWindow.AirChoices),("Water",settings.WaterChoice,SettingsWindow.WaterChoices),("Fire",settings.FireChoice,SettingsWindow.FireChoices),("Undead",settings.UndeadChoice,SettingsWindow.UndeadChoices),("Monster",settings.MonsterChoice,SettingsWindow.MonsterChoices)};
        try
        {
            using(var db=new TrackerStore(Path.Combine(folder,"family-preferences.db")))
            {
                db.Set(PetArtworkPreferences.StateKey,new{Undead="wraith-a",Fire="wizard-crystal"});
                var legacy=PetArtworkPreferences.Read(db);
                check(legacy.Undead=="wraith-a"&&legacy.Fire=="wizard-crystal"&&legacy.Air=="monk-fist"&&legacy.Water=="rogue"&&legacy.Monster=="monster-a","Legacy Fire/Undead preferences survive additive family defaults");
                db.Set(PetArtworkPreferences.StateKey,new{Undead="wraith-b",Fire="wizard-crystal",Air=42,Water="unsupported",Monster=42});
                var partial=PetArtworkPreferences.Read(db);
                check(partial.Undead=="wraith-b"&&partial.Fire=="wizard-crystal"&&partial.Air=="monk-fist"&&partial.Water=="rogue"&&partial.Monster=="monster-a","Invalid family values fall back independently without resetting valid siblings");
                new PetArtworkPreferences{Undead="wraith-a",Fire="classic",Air="classic",Water="torrent",Monster="dragon-diagonal"}.Write(db);
            }
            using(var db=new TrackerStore(Path.Combine(folder,"family-preferences.db")))
            {
                var saved=PetArtworkPreferences.Read(db);
                check(saved.Fire=="classic"&&saved.Air=="classic"&&saved.Water=="torrent"&&saved.Undead=="wraith-a"&&saved.Monster=="dragon-diagonal","Every independent appearance survives closing and reopening its character store");
            }
            settings.SelectSection(SettingsSection.Appearance);Flush();
            foreach(var (family,select,options) in cases)
            {
                check(select.Focusable&&KeyboardNavigation.GetIsTabStop(select)&&AutomationProperties.GetName(select)==family+" appearance",family+" choice has a labeled keyboard tab stop");
                foreach(var treatment in Enum.GetValues<IconArtworkStyle>())
                {
                    controller.Profile.IconStyle=treatment;
                    var hashes=new HashSet<string>();
                    foreach(var option in options)
                    {
                        var prior=controller.ArtworkPreferences;select.SelectedValue=option.Id;Apply();Flush();
                        check(IconArtwork.ActorRole("Pet",family)==option.Role,family+" "+option.Name+" routes immediately in "+treatment);
                        check(cases.Where(c=>c.Item1!=family).All(c=>prior.For(c.Item1)==controller.ArtworkPreferences.For(c.Item1)),family+" selection preserves every other family's choice");
                        var badge=new ActorBadge{Width=24,Height=24};badge.Set("Pet",false,"Synthetic",family);
                        var bitmap=PreviewRenderer.Capture(badge,24,24,1);var bytes=new byte[24*24*4];bitmap.CopyPixels(bytes,24*4,0);hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
                        check(IconArtwork.Painted(option.Role,192)?.PixelWidth>=192,option.Role+" has sufficient painted resolution for 64 DIP at 300%");
                    }
                    check(hashes.Count==options.Length,family+" choices retain distinct small artwork in "+treatment);
                }
            }
            check(IconArtwork.ActorRole("Pet","Warder")=="pet-warder"&&IconArtwork.ActorRole("Pet","Animation")=="pet-animation","Warder and Enchanter Animation use explicit shared family artwork");
            settings.MonsterChoice.SelectedValue="monster-a";Apply();
            check(IconArtwork.ActorRole("Pet","Monster")=="pet-monster"&&IconArtwork.ActorRole("Pet","Unknown")=="pet-generic","Monster A routing preserves the unresolved generic fallback");
            var other=new CharacterProfile{Character="Other art",Server="Test",IconStyle=style};
            IconArtwork.Apply(other,preferences:new PetArtworkPreferences{Air="classic",Water="torrent",Monster="aviak-overhead"});
            check(IconArtwork.Air=="classic"&&IconArtwork.Water=="torrent"&&IconArtwork.Monster=="aviak-overhead","Character change installs the new character's independent family choices");
            Apply();check(IconArtwork.Air==controller.ArtworkPreferences.Air&&IconArtwork.Water==controller.ArtworkPreferences.Water&&IconArtwork.Monster==controller.ArtworkPreferences.Monster,"Returning to the prior character restores its family settings");
            check(!IconArtwork.Apply(controller.Profile,controller.UndeadArtwork,controller.FireArtwork,controller.ArtworkPreferences),"Equivalent family refreshes do not invalidate artwork");
            foreach(var (family,select,_) in cases)select.SelectedValue=original.For(family);
            controller.Profile.IconStyle=style;Apply();
            foreach(string theme in new[]{"coldain","sunstrider","shadowman"})
            {
                Appearance.Apply(theme);
                foreach(var (width,height,name) in new[]{(940d,700d,"normal"),(640d,420d,"minimum")})
                {
                    settings.MinWidth=640;settings.MinHeight=420;settings.Width=width;settings.Height=height;Flush();
                    foreach(var (family,select,_) in cases)
                    {
                        select.BringIntoView();Flush();
                        var before=select.TransformToAncestor((Visual)settings.Content).TransformBounds(new Rect(select.RenderSize));
                        select.Focus();Flush();var after=select.TransformToAncestor((Visual)settings.Content).TransformBounds(new Rect(select.RenderSize));
                        check(before==after&&select.ActualHeight==28&&select.ActualWidth>=170,family+" "+theme+" "+name+" reserves 28-DIP keyboard-focus bounds");
                    }
                    settings.AirChoice.BringIntoView();Flush();
                    foreach(double scale in new[]{1d,1.5,2d,3d})Save("family-settings-"+theme+"-"+name+"-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"x",(FrameworkElement)settings.Content,settings.ActualWidth,settings.ActualHeight,scale);
                }
                var board=new StackPanel{Background=UI.Brush("SolidSurface")};
                foreach(string family in new[]{"Earth","Air","Water","Fire","Undead","Warder","Animation","Monster"})
                {
                    var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=family,Width=100,Foreground=UI.Brush("Foreground"),VerticalAlignment=VerticalAlignment.Center});
                    foreach(int dip in new[]{13,14,16,20,24,32,64}){var badge=new ActorBadge{Width=dip,Height=dip,Margin=new(5)};badge.Set("Pet",false,"Synthetic family",family);row.Children.Add(badge);}board.Children.Add(row);
                }
                Save("approved-families-"+theme,board,500,650,2);
            }
        }
        finally
        {
            foreach(var (family,select,_) in cases)select.SelectedValue=original.For(family);
            controller.Profile.IconStyle=style;Appearance.Apply(controller.Profile.Theme);Apply();
        }
    }
}
