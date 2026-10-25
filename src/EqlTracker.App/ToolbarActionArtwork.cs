using System.Windows;
using System.Windows.Media;

namespace EqlTracker.App;

// Original small vector: broad brass teeth, central bearing and four rivets.
internal sealed class ToolbarActionArtwork(bool close):FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/18,ActualHeight/18));
        if(close)
        {
            var pen=new Pen(UI.Brush("Melee"),1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            dc.DrawLine(pen,new(1,1),new(17,17));dc.DrawLine(pen,new(17,1),new(1,17));
        }
        else
        {
            var brass=new SolidColorBrush(Color.FromRgb(193,145,54));var edge=new Pen(UI.Brush("Gold"),.8);
            var shape=Geometry.Parse("M7,0L11,0L11.5,3L13,3.7L15.5,2L18,5L15.8,7L16,9L18,11L16,14L13,13.5L11.5,15L11,18L7,18L6.5,15L5,14L2,15L0,12L2.5,9L2.2,7L0,5L2.5,2L5,3.8L6.5,3Z");
            dc.DrawGeometry(brass,edge,shape);dc.DrawEllipse(UI.Brush("InnerSurface"),edge,new(9,9),4,4);
            dc.DrawEllipse(null,new Pen(UI.Brush("Gold"),.6),new(9,9),2.7,2.7);
            foreach(var p in new[]{new Point(9,2),new Point(16,9),new Point(9,16),new Point(2,9)})dc.DrawEllipse(UI.Brush("InnerSurface"),null,p,.75,.75);
        }
        dc.Pop();
    }
}
