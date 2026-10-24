using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    internal static readonly UndeadOption[] FireChoices=[new("wizard-staff","Wizard staff","pet-fire"),new("wizard-crystal","Wizard crystal","pet-fire-crystal")];
    internal readonly ComboBox FireChoice=new(){Width=170,DisplayMemberPath="Name",SelectedValuePath="Id"};
    private UIElement BuildFireAppearance()
    {
        FireChoice.ItemsSource=FireChoices;FireChoice.SelectedValue=Controller.FireArtwork;
        FireChoice.ToolTip="Fire pet artwork, saved per character. This changes appearance only.";
        System.Windows.Automation.AutomationProperties.SetName(FireChoice,"Fire appearance");
        FireChoice.SelectionChanged+=(_,_)=>{if(FireChoice.SelectedItem is not UndeadOption choice)return;Controller.SetFireArtwork(choice.Id);((App)Application.Current).ApplySettings();};
        var samples=new WrapPanel{Margin=new(0,2,0,0)};
        foreach(var choice in FireChoices)
        {
            var name=UI.Text(choice.Name,11);name.Margin=new(0);name.TextAlignment=TextAlignment.Center;
            var tile=UI.Stack(UI.Row(new ApprovedPetArtworkPreview(choice.Role,24){Margin=new(0,0,5,0),VerticalAlignment=VerticalAlignment.Center},new ApprovedPetArtworkPreview(choice.Role,40)),name);tile.Margin=new(0,0,16,4);samples.Children.Add(tile);
        }
        var group=UI.Stack(UI.Field("Fire appearance · per character",FireChoice),samples);group.Margin=new(0,0,12,6);return group;
    }
}
