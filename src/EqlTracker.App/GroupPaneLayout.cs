using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed class GroupPaneLayout : Grid
{
    private readonly UIElement recent,members,details;
    private readonly GridSplitter first=new(){Background=Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch},second=new(){Background=Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
    private readonly CharacterProfile profile; private readonly Action changed;
    private int layout=-1;
    public GroupPaneLayout(UIElement recent,UIElement members,UIElement details,CharacterProfile profile,Action changed)
    {
        this.recent=recent;this.members=members;this.details=details;this.profile=profile;this.changed=changed;
        foreach(var item in new[]{recent,first,members,second,details})Children.Add(item);
        SizeChanged+=(_,_)=>ArrangePanes();first.DragCompleted+=(_,e)=>{if(!e.Handled)Save();};second.DragCompleted+=(_,e)=>{if(!e.Handled)Save();};ArrangePanes();
    }
    private void Save()
    {
        if(layout==0){double total=ColumnDefinitions.Where(c=>c.Width.IsStar).Sum(c=>c.Width.Value);profile.PanelSplits["group-recent-width"]=ColumnDefinitions[0].Width.Value/total;profile.PanelSplits["group-members-width"]=ColumnDefinitions[2].Width.Value/total;}
        else{profile.PanelSplits["group-recent-height"]=RowDefinitions[0].ActualHeight;if(layout==1)profile.PanelSplits["group-members-medium"]=ColumnDefinitions[0].Width.Value/(ColumnDefinitions[0].Width.Value+ColumnDefinitions[2].Width.Value);else profile.PanelSplits["group-members-height"]=RowDefinitions[2].ActualHeight;}
        changed();
    }
    private void ArrangePanes()
    {
        int next=ActualWidth>=980?0:ActualWidth>=620?1:2;if(next==layout)return;PaneLayout.Cancel(this);layout=next;ColumnDefinitions.Clear();RowDefinitions.Clear();
        void Place(UIElement child,int column,int row,int span=1){SetColumn(child,column);SetRow(child,row);SetColumnSpan(child,span);}
        if(layout==0)
        {
            double a=Math.Clamp(profile.PanelSplits.GetValueOrDefault("group-recent-width",.22),.14,.32),b=Math.Clamp(profile.PanelSplits.GetValueOrDefault("group-members-width",.37),.22,.45);
            foreach(double width in new[]{a,-5d,b,-5d,1-a-b})ColumnDefinitions.Add(new(){Width=width<0?new(-width):new(width,GridUnitType.Star)});
            Place(recent,0,0);Place(first,1,0);Place(members,2,0);Place(second,3,0);Place(details,4,0);first.ResizeDirection=second.ResizeDirection=GridResizeDirection.Columns;first.Cursor=second.Cursor=Cursors.SizeWE;
            ColumnDefinitions[0].MinWidth=140;ColumnDefinitions[2].MinWidth=200;ColumnDefinitions[4].MinWidth=220;
            PaneLayout.Register(this,[recent,members,details],[ColumnDefinitions[0],ColumnDefinitions[2],ColumnDefinitions[4]],[first,second],saved:Save);
        }
        else
        {
            RowDefinitions.Add(new(){Height=new(Math.Clamp(profile.PanelSplits.GetValueOrDefault("group-recent-height",76),55,160))});RowDefinitions.Add(new(){Height=new(5)});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
            first.ResizeDirection=GridResizeDirection.Rows;first.Cursor=Cursors.SizeNS;
            if(layout==1)
            {
                double b=Math.Clamp(profile.PanelSplits.GetValueOrDefault("group-members-medium",.42),.3,.6);ColumnDefinitions.Add(new(){Width=new(b,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=new(5)});ColumnDefinitions.Add(new(){Width=new(1-b,GridUnitType.Star)});
                Place(recent,0,0,3);Place(first,0,1,3);Place(members,0,2);Place(second,1,2);Place(details,2,2);second.ResizeDirection=GridResizeDirection.Columns;second.Cursor=Cursors.SizeWE;
                ColumnDefinitions[0].MinWidth=200;ColumnDefinitions[2].MinWidth=220;
                PaneLayout.Register(this,[members,details],[ColumnDefinitions[0],ColumnDefinitions[2]],[second],saved:Save);
            }
            else
            {
                RowDefinitions[2].Height=new(Math.Clamp(profile.PanelSplits.GetValueOrDefault("group-members-height",100),60,200));RowDefinitions.Add(new(){Height=new(5)});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
                Place(recent,0,0);Place(first,0,1);Place(members,0,2);Place(second,0,3);Place(details,0,4);second.ResizeDirection=GridResizeDirection.Rows;second.Cursor=Cursors.SizeNS;
                PaneLayout.Register(this,[],[],[]);
            }
        }
    }
}
