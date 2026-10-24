using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    internal sealed record UndeadOption(string Id,string Name,string Role);
    internal static readonly UndeadOption[] UndeadChoices=[new("skeleton-a","Skeleton A","pet-undead-skeleton"),new("wraith-a","Wraith A","pet-undead-wraith-a"),new("wraith-b","Wraith B","pet-undead-wraith-b")];
    internal readonly ComboBox UndeadChoice=new(){Width=170,DisplayMemberPath="Name",SelectedValuePath="Id"};
    private UIElement BuildUndeadAppearance()
    {
        UndeadChoice.ItemsSource=UndeadChoices;UndeadChoice.SelectedValue=Controller.UndeadArtwork;
        UndeadChoice.ToolTip="Artwork for the Undead pet family, saved per character. Skeleton A is the default. This does not change detected pet identity or combat history.";
        System.Windows.Automation.AutomationProperties.SetName(UndeadChoice,"Undead appearance");
        UndeadChoice.SelectionChanged+=(_,_)=>
        {
            if(UndeadChoice.SelectedItem is not UndeadOption choice)return;
            Controller.SetUndeadArtwork(choice.Id);((App)Application.Current).ApplySettings();
        };
        var samples=new WrapPanel{Margin=new(0,2,0,0)};
        foreach(var choice in UndeadChoices)
        {
            var name=UI.Text(choice.Name,11);name.Margin=new(0);name.TextAlignment=TextAlignment.Center;
            var icons=UI.Row(new ApprovedPetArtworkPreview(choice.Role,24){Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center},new ApprovedPetArtworkPreview(choice.Role,40));
            var tile=UI.Stack(icons,name);tile.Margin=new(0,0,16,4);tile.ToolTip=choice.Name+" · 24 and 40 DIP · current Clean/Vibrant style";samples.Children.Add(tile);
        }
        var group=UI.Stack(UI.Field("Undead appearance · per character",UndeadChoice),samples);group.Margin=new(0,0,12,6);return group;
    }
}
