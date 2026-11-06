using System.Windows;
using System.Windows.Controls;
namespace EqlTracker.App;
public sealed partial class DpsWindow
{
    private readonly ComboBox comparisonKind=WorkspaceForms.Control(UI.Select(["Combat samples","Songs"],"Combat samples",150),"Loadout comparison view");
    private BardComparisonPanel? songComparison;
    private FrameworkElement? comparisonCombatOverview;
    private bool SongsSelected=>SelectedTab==EqlTracker.Core.CombatLabTab.Comparison&&comparisonKind.SelectedItem?.ToString()=="Songs";
    private UIElement SongComparisonWorkspace(UIElement measured)
    {
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        comparisonKind.HorizontalAlignment=HorizontalAlignment.Left;root.Children.Add(comparisonKind);Grid.SetRow(measured,1);root.Children.Add(measured);songComparison=new(Controller){Visibility=Visibility.Collapsed};Grid.SetRow(songComparison,1);root.Children.Add(songComparison);
        comparisonKind.SelectionChanged+=(_,_)=>{bool songs=comparisonKind.SelectedItem?.ToString()=="Songs";songComparison.Visibility=songs?Visibility.Visible:Visibility.Collapsed;measured.Visibility=songs?Visibility.Collapsed:Visibility.Visible;ApplySongPresentation();if(songs)songComparison.Refresh();};
        group.SongCoverageRequested+=()=>{ShowView(mode,EqlTracker.Core.CombatLabTab.Comparison);comparisonKind.SelectedItem="Songs";songComparison.Refresh();};return root;
    }
    private void ApplySongPresentation()
    {if(comparisonCombatOverview!=null)comparisonCombatOverview.Visibility=SongsSelected?Visibility.Collapsed:Visibility.Visible;if(SongsSelected)songComparison?.Refresh();}
    internal BardComparisonPanel OpenSongsReview(){ShowView(mode,EqlTracker.Core.CombatLabTab.Comparison);comparisonKind.SelectedItem="Songs";ApplySongPresentation();return songComparison!;}
}
