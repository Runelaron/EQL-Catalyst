using System.Windows;
using System.Windows.Media;

namespace EqlTracker.App;

public sealed class MaxHitGlyph : FrameworkElement
{
    internal EqlTracker.Core.IconArtworkStyle? PreviewStyle { get; init; }
    private static readonly Geometry burst = Geometry.Parse("M12,1 L14,7 L20,3 L17,9 L23,12 L17,14 L20,21 L14,17 L12,23 L9,17 L3,21 L7,14 L1,12 L7,9 L3,3 L9,7 Z");
    public MaxHitGlyph() { IconArtwork.Observe(this); ToolTip="Max hit · largest logged hit in this selection";System.Windows.Automation.AutomationProperties.SetName(this,"Max hit"); }
    protected override void OnRender(DrawingContext dc)
    {
        double size=Math.Min(ActualWidth,ActualHeight);if(size<=0)return;
        if(IconArtwork.DrawPainted(dc,"combat-max-hit",new(0,0,size,size),VisualTreeHelper.GetDpi(this).PixelsPerDip,PreviewStyle))return;
        dc.PushTransform(new ScaleTransform(size/24,size/24));dc.DrawGeometry(UI.Brush("Melee"),null,burst);dc.DrawEllipse(UI.Brush("Foreground"),null,new(12,12),3,3);dc.Pop();
    }
}
