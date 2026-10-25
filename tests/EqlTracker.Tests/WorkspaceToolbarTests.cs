using EqlTracker.Core;

internal static class WorkspaceToolbarTests
{
    internal static void Register(Action<string,Action> test)
    {
        static void Check(bool value){if(!value)throw new Exception("Toolbar layout invariant failed");}
        test("Workspace toolbar normalizes unknown and duplicated buttons",()=>{var state=new WorkspaceToolbarState{Order=["CombatLab","unknown","CombatLab"],Active="gone",Last="gone"};state.Normalize();Check(state.Order.SequenceEqual(["CombatLab","Tracker","Overview"])&&state.Active==""&&state.Last=="Tracker"&&state.Placement.Height==28);});
        test("Workspace toolbar reordering is bounded and reversible",()=>{var state=new WorkspaceToolbarState();Check(!WorkspaceToolbarLayout.Move(state,"Tracker",-1)&&!WorkspaceToolbarLayout.Move(state,"missing",1));Check(WorkspaceToolbarLayout.Move(state,"Tracker",1)&&state.Order[0]=="CombatLab");Check(WorkspaceToolbarLayout.Move(state,"Tracker",1)&&state.Order[2]=="Tracker");Check(!WorkspaceToolbarLayout.Move(state,"Tracker",1));Check(WorkspaceToolbarLayout.Move(state,"Tracker",-1));Check(WorkspaceToolbarLayout.Move(state,"Tracker",-1)&&state.Order[0]=="Tracker");});
        test("Workspace toolbar state roundtrips independently of panel layouts",()=>{var state=new WorkspaceToolbarState{Active="CombatLab",Last="CombatLab",Visible=false,Placement=new(){Width=500,Height=28,ScreenLeft=-100,ScreenTop=90,DpiX=1.5,DpiY=1.5}};var copy=EquipmentPositions.Copy(state);Check(copy.Active==state.Active&&!copy.Visible&&copy.Placement.Width==500&&copy.Placement.ScreenLeft==-100&&copy.Placement.DpiX==1.5);});
        test("Workspace toolbar attaches below in physical pixels with DIP dimensions",()=>{var r=WorkspaceToolbarLayout.Attach(120,30,42,915,700,640,420,new(0,0,3000,1800,1.5,1.5));Check(r==new AttachedWorkspaceBounds(30,120,72,915,700,false));});
        test("Workspace toolbar uses available space above near bottom edge",()=>{var r=WorkspaceToolbarLayout.Attach(120,1000,28,915,700,640,420,new(0,0,1920,1080,1,1));Check(r.Above&&r.Top==300&&r.Height==700&&r.BarTop==1000);});
        test("Workspace toolbar recovers small monitors without offscreen panels",()=>{foreach(double dpi in new[]{1d,1.5,2d,3d}){var r=WorkspaceToolbarLayout.Attach(-900,200,28*dpi,1500,1400,640,420,new(0,0,1200,650,dpi,dpi));Check(r.Left>=0&&r.Top>=0&&r.Left+r.Width*dpi<=1200.01&&r.Top+r.Height*dpi<=650.01&&r.Height>0);}});
        test("Workspace toolbar does not mutate saved expanded or anchor dimensions",()=>{var state=new WorkspaceToolbarState{Placement=new(){Width=420,Height=28}};var panel=new PanelState{Width=915,Height=800};for(int i=0;i<20;i++)_ = WorkspaceToolbarLayout.Attach(20,500,28,panel.Width,panel.Height,640,420,new(0,0,1000,700,1,1));Check(panel.Width==915&&panel.Height==800&&state.Placement.Width==420);});
        test("Workspace toolbar rejects invalid work areas",()=>{foreach(var work in new[]{new ToolbarWorkArea(0,0,0,800,1,1),new(0,0,1000,800,0,1)}){bool rejected=false;try{WorkspaceToolbarLayout.Attach(0,0,28,915,800,640,420,work);}catch(ArgumentException){rejected=true;}Check(rejected);}});
    }
}
