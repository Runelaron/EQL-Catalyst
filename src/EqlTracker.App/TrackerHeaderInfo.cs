using System.Windows;
using System.Windows.Controls;

namespace EqlTracker.App;

public sealed partial class QuestWindow
{
    private void InstallTrackerHeaderInfo()=>WorkspaceHeaderBand.Install(this);
}

// Measure the actual title, rather than treating outer width as title width.
internal sealed class WorkspaceHeaderBand(TextBlock title,TextBlock context):Panel
{
    private bool stacked;
    private double titleWidth,titleHeight;
    internal static void Install(FrostWindow window)
    {
        window.Heading.Children.Remove(window.HeadingTitle);window.Heading.Children.Remove(window.Subtitle);
        var band=new WorkspaceHeaderBand(window.HeadingTitle,window.Subtitle);
        band.Children.Add(window.HeadingTitle);band.Children.Add(window.Subtitle);window.Heading.Children.Add(band);
        window.Subtitle.Margin=new(0);window.Subtitle.TextAlignment=TextAlignment.Right;window.Subtitle.VerticalAlignment=VerticalAlignment.Center;
        window.Subtitle.ToolTip="Character, pet and location context";
        window.Subtitle.ToolTipOpening+=(_,_)=>window.Subtitle.ToolTip=window.Subtitle.Text;
    }
    protected override Size MeasureOverride(Size available)
    {
        title.Measure(new(double.PositiveInfinity,double.PositiveInfinity));context.Measure(new(double.PositiveInfinity,double.PositiveInfinity));
        double width=double.IsFinite(available.Width)?available.Width:title.DesiredSize.Width+context.DesiredSize.Width+20;
        bool hasContext=context.Visibility!=Visibility.Collapsed&&context.DesiredSize.Width>0;
        double minimumContext=Math.Min(168,context.DesiredSize.Width);
        stacked=hasContext&&width<title.DesiredSize.Width+minimumContext+20;
        titleWidth=stacked||!hasContext?width:Math.Min(title.DesiredSize.Width+16,Math.Max(0,width-minimumContext-20));
        title.Measure(new(titleWidth,double.PositiveInfinity));titleHeight=title.DesiredSize.Height;
        context.Measure(new(Math.Max(0,stacked?width-8:width-titleWidth-20),double.PositiveInfinity));
        return new(width,stacked?titleHeight+context.DesiredSize.Height:Math.Max(titleHeight,context.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size size)
    {
        title.Arrange(new(0,0,stacked?size.Width:titleWidth,stacked?titleHeight:size.Height));
        double left=stacked?0:titleWidth+12;
        context.Arrange(new(left,stacked?titleHeight:0,Math.Max(0,size.Width-left-8),stacked?Math.Max(0,size.Height-titleHeight):size.Height));
        return size;
    }
}
