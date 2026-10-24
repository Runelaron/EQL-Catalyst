using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EqlTracker.App;

/// <summary>Two native windows share an outline, never an overlapping translucent fill.</summary>
internal sealed class WorkspaceFrame : Border
{
    internal bool Joined { get; private set; }
    internal bool JoinTop { get; private set; }
    internal double JoinStart { get; private set; }
    internal double JoinEnd { get; private set; }
    internal void Join(bool top,double start,double end)
    { Joined=end>start;JoinTop=top;JoinStart=start;JoinEnd=end;InvalidateVisual(); }
    internal void Detach(){Joined=false;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        if(!Joined){base.OnRender(dc);return;}
        double w=ActualWidth,h=ActualHeight;if(w<=1||h<=1)return;
        double a=Math.Clamp(JoinStart,0,w),b=Math.Clamp(JoinEnd,0,w),r=Math.Min(4,Math.Min(w,h)/2);
        double tl=JoinTop&&a<1?0:r,tr=JoinTop&&b>w-1?0:r;
        double bl=!JoinTop&&a<1?0:r,br=!JoinTop&&b>w-1?0:r;
        // Fill reaches the native edge; only the exposed perimeter is stroked.
        var fill=new StreamGeometry();using(var c=fill.Open())
        {
            c.BeginFigure(new(tl,0),true,true);c.LineTo(new(w-tr,0),true,false);c.QuadraticBezierTo(new(w,0),new(w,tr),true,false);
            c.LineTo(new(w,h-br),true,false);c.QuadraticBezierTo(new(w,h),new(w-br,h),true,false);
            c.LineTo(new(bl,h),true,false);c.QuadraticBezierTo(new(0,h),new(0,h-bl),true,false);
            c.LineTo(new(0,tl),true,false);c.QuadraticBezierTo(new(0,0),new(tl,0),true,false);
        }
        dc.DrawGeometry(Background,null,fill);var pen=new Pen(BorderBrush,1);
        var edge=new StreamGeometry();using(var c=edge.Open())
        {
            void Segment(Point p,Point q){c.BeginFigure(p,false,false);c.LineTo(q,true,false);}
            if(JoinTop){if(a>tl)Segment(new(tl,.5),new(a,.5));if(b<w-tr)Segment(new(b,.5),new(w-tr,.5));}
            else Segment(new(tl,.5),new(w-tr,.5));
            if(!JoinTop){if(a>bl)Segment(new(bl,h-.5),new(a,h-.5));if(b<w-br)Segment(new(b,h-.5),new(w-br,h-.5));}
            else Segment(new(bl,h-.5),new(w-br,h-.5));
            Segment(new(.5,tl),new(.5,h-bl));Segment(new(w-.5,tr),new(w-.5,h-br));
            if(tl>0){c.BeginFigure(new(.5,tl),false,false);c.QuadraticBezierTo(new(.5,.5),new(tl,.5),true,false);}
            if(tr>0){c.BeginFigure(new(w-tr,.5),false,false);c.QuadraticBezierTo(new(w-.5,.5),new(w-.5,tr),true,false);}
            if(bl>0){c.BeginFigure(new(.5,h-bl),false,false);c.QuadraticBezierTo(new(.5,h-.5),new(bl,h-.5),true,false);}
            if(br>0){c.BeginFigure(new(w-br,h-.5),false,false);c.QuadraticBezierTo(new(w-.5,h-.5),new(w-.5,h-br),true,false);}
        }
        dc.DrawGeometry(null,pen,edge);
    }
}

/// <summary>Presentation-only progress. Native placement and all measured layout stay stationary.</summary>
internal sealed class WorkspacePanelMotion : Animatable
{
    private readonly FrameworkElement surface;
    private readonly TranslateTransform slide=new();
    private long revision;
    private bool above;
    internal bool Above {get=>above;set{above=value;Apply();}}
    internal bool Running {get;private set;}
    internal bool Opening {get;private set;}
    internal double Progress=> (double)GetValue(ProgressProperty);
    private static readonly DependencyProperty ProgressProperty=DependencyProperty.Register(nameof(Progress),typeof(double),typeof(WorkspacePanelMotion),new PropertyMetadata(1d,(d,_)=>((WorkspacePanelMotion)d).Apply()));
    internal WorkspacePanelMotion(FrameworkElement surface){this.surface=surface;surface.RenderTransform=slide;}
    protected override Freezable CreateInstanceCore()=>throw new NotSupportedException();
    private void Apply(){double p=Math.Clamp(Progress,0,1);surface.Opacity=p;slide.Y=(Above?4:-4)*(1-p);}
    internal void Snap(bool open)
    {revision++;Running=false;Opening=open;BeginAnimation(ProgressProperty,null);SetValue(ProgressProperty,open?1d:0d);surface.IsHitTestVisible=open;Apply();}
    internal void To(bool open,bool animate,Action completed)
    {
        if(Running&&Opening==open&&animate)return;
        double from=Progress,target=open?1:0;long current=++revision;Opening=open;
        BeginAnimation(ProgressProperty,null);SetValue(ProgressProperty,from);surface.IsHitTestVisible=open;
        if(!animate||Math.Abs(from-target)<.001){Snap(open);completed();return;}
        Running=true;
        var motion=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds((open?120:90)*Math.Abs(target-from)))
        {EasingFunction=new QuadraticEase{EasingMode=open?EasingMode.EaseOut:EasingMode.EaseIn},FillBehavior=FillBehavior.HoldEnd};
        motion.Completed+=(_,_)=>{if(current!=revision)return;Snap(open);completed();};
        BeginAnimation(ProgressProperty,motion,HandoffBehavior.SnapshotAndReplace);
    }
}
