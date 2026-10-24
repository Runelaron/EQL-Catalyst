namespace EqlTracker.Core;

internal sealed class WorkspaceToolbarState
{
    public List<string> Order {get;set;}=["Tracker","CombatLab"];
    public string Active {get;set;}="";
    public string Last {get;set;}="Tracker";
    public bool Visible {get;set;}=true;
    public bool Locked {get;set;}
    public PanelState Placement {get;set;}=new(){Width=370,Height=28};
    internal void Normalize()
    {
        Order=(Order??[]).Where(WorkspaceToolbarLayout.Known).Distinct().Concat(new[]{"Tracker","CombatLab"}).Distinct().ToList();
        if(!WorkspaceToolbarLayout.Known(Active))Active="";if(!WorkspaceToolbarLayout.Known(Last))Last="Tracker";
        Placement??=new(){Width=370};Placement.Height=28;
    }
}
internal sealed record ToolbarWorkArea(double Left,double Top,double Width,double Height,double DpiX,double DpiY);
internal sealed record AttachedWorkspaceBounds(double BarTop,double Left,double Top,double Width,double Height,bool Above);
internal static class WorkspaceToolbarLayout
{
    internal static bool Known(string id)=>id is "Tracker" or "CombatLab";
    internal static bool Move(WorkspaceToolbarState state,string id,int direction)
    {
        state.Normalize();int old=state.Order.IndexOf(id),next=old+Math.Sign(direction);
        if(old<0||next<0||next>=state.Order.Count||next==old)return false;
        state.Order.RemoveAt(old);state.Order.Insert(next,id);return true;
    }
    // Work/anchor coordinates are native pixels. Window dimensions are DIP.
    internal static AttachedWorkspaceBounds Attach(double barLeft,double barTop,double barHeight,double width,double height,double minWidth,double minHeight,ToolbarWorkArea work)
    {
        if(work.Width<=0||work.Height<=0||work.DpiX<=0||work.DpiY<=0)throw new ArgumentException("A valid display is required.");
        double w=Math.Clamp(width,Math.Min(minWidth,work.Width/work.DpiX),work.Width/work.DpiX);
        double min=Math.Min(minHeight,Math.Max(1,(work.Height-barHeight)/work.DpiY));
        double y=Math.Clamp(barTop,work.Top,Math.Max(work.Top,work.Top+work.Height-barHeight));
        double below=work.Top+work.Height-y-barHeight,above=y-work.Top;bool up=(below<height*work.DpiY&&above>=height*work.DpiY)||(below<min*work.DpiY&&above>=min*work.DpiY);
        if(!up&&below<min*work.DpiY){y=Math.Clamp(y,work.Top,Math.Max(work.Top,work.Top+work.Height-barHeight-min*work.DpiY));below=work.Top+work.Height-y-barHeight;}
        double space=up?y-work.Top:below,h=Math.Clamp(height,Math.Min(min,space/work.DpiY),space/work.DpiY);
        double x=Math.Clamp(barLeft,work.Left,Math.Max(work.Left,work.Left+work.Width-w*work.DpiX));
        return new(y,x,up?y-h*work.DpiY:y+barHeight,w,h,up);
    }
}
