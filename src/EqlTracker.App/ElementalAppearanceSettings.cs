using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    internal static readonly UndeadOption[] AirChoices=[new("monk-fist","Classes — Monk","pet-air"),new("classic","Classic","pet-air-classic")];
    internal static readonly UndeadOption[] WaterChoices=[new("rogue","Classes — Rogue","pet-water"),new("classic","Classic","pet-water-classic"),new("torrent","Elemental torrent","pet-water-torrent")];
    internal static readonly UndeadOption[] EarthChoices=[new("classic","Classic","pet-earth")];
    internal static readonly UndeadOption[] MonsterChoices=[
        new("monster-a","Monster A (default)","pet-monster"),
        new("aviak-overhead","Aviak C — Overhead","pet-monster-aviak-c"),
        new("dragon-overhead","Dragon C — Overhead","pet-monster-dragon-c"),
        new("aviak-diagonal","Aviak B — Diagonal","pet-monster-aviak-b"),
        new("dragon-diagonal","Dragon B — Diagonal","pet-monster-dragon-b")];
    internal readonly ComboBox AirChoice=new(){Width=210,DisplayMemberPath="Name",SelectedValuePath="Id"};
    internal readonly ComboBox WaterChoice=new(){Width=210,DisplayMemberPath="Name",SelectedValuePath="Id"};
    internal readonly ComboBox EarthChoice=new(){Width=210,DisplayMemberPath="Name",SelectedValuePath="Id"};
    internal readonly ComboBox MonsterChoice=new(){Width=210,DisplayMemberPath="Name",SelectedValuePath="Id"};
    private UIElement BuildElementalAppearance(string family,ComboBox select,UndeadOption[] options)
    {
        WorkspaceForms.Control(select,family+" appearance");
        select.ItemsSource=options;select.SelectedValue=Controller.ArtworkPreferences.For(family);
        select.ToolTip=family+" artwork, saved independently per character. Appearance changes no pet identity or combat evidence.";
        System.Windows.Automation.AutomationProperties.SetName(select,family+" appearance");
        var samples=UI.Row();samples.Margin=new(0,4,0,0);
        void Preview()
        {
            if(select.SelectedItem is not UndeadOption choice)return;
            samples.Children.Clear();samples.Children.Add(new ApprovedPetArtworkPreview(choice.Role,24){Margin=new(0,0,8,0),VerticalAlignment=VerticalAlignment.Center});samples.Children.Add(new ApprovedPetArtworkPreview(choice.Role,40));
            samples.ToolTip=choice.Name+" · 24 and 40 DIP · current Clean/Vibrant style"+(family=="Monster"&&choice.Id!="monster-a"?" · Interim artwork; axe shaft alignment refinement pending.":"");
        }
        select.SelectionChanged+=(_,_)=>{if(select.SelectedItem is not UndeadOption choice)return;Controller.SetPetArtwork(family,choice.Id);((App)Application.Current).ApplySettings();Preview();};Preview();
        var group=UI.Stack(UI.Field(family+" appearance",select),samples);group.Margin=new(0,0,8,8);return group;
    }
}
