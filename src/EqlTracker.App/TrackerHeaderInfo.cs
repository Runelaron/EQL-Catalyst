using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private void InstallTrackerHeaderInfo()
    {
        Heading.Children.Remove(HeadingTitle);Heading.Children.Remove(Subtitle);
        var band=new Grid();band.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});band.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        band.RowDefinitions.Add(new(){Height=GridLength.Auto});band.RowDefinitions.Add(new(){Height=GridLength.Auto});
        band.Children.Add(HeadingTitle);band.Children.Add(Subtitle);Heading.Children.Add(band);
        Subtitle.HorizontalAlignment=HorizontalAlignment.Right;Subtitle.TextAlignment=TextAlignment.Right;Subtitle.VerticalAlignment=VerticalAlignment.Center;
        void Adapt()
        {
            bool narrow=(ActualWidth>0?ActualWidth:Width)<850;
            Grid.SetColumn(Subtitle,narrow?0:1);Grid.SetRow(Subtitle,narrow?1:0);
            Subtitle.Margin=new(narrow?0:12,0,8,0);
            Subtitle.MaxWidth=Math.Max(80,narrow?ActualWidth-82:ActualWidth*.43);
            Subtitle.Visibility=!IsCollapsed&&Subtitle.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        }
        SizeChanged+=(_,_)=>Adapt();Loaded+=(_,_)=>Adapt();CollapseChanged+=Adapt;
        Subtitle.ToolTipOpening+=(_,_)=>Subtitle.ToolTip=Subtitle.Text;
        Subtitle.ToolTip="Character, current zone and inventory update evidence";Adapt();
    }
}
