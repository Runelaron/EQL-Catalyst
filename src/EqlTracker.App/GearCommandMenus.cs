using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    private static FrameworkElement GearField(string label,FrameworkElement control,Button? info=null)
    {
        var heading=new Grid{Height=22};var text=UI.Label(label);text.Margin=new(0);text.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(text);
        if(info!=null){info.Style=(Style)Application.Current.FindResource(typeof(Button));info.Width=info.Height=20;info.MinHeight=0;info.Margin=new(0);info.FontSize=12;info.Padding=new(0);info.HorizontalAlignment=HorizontalAlignment.Right;info.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(info);}
        control.Height=28;var field=UI.Stack(heading,control);field.Margin=new(0,0,6,0);return field;
    }
    private static FrameworkElement GearAction(FrameworkElement control)
    {control.Height=28;var field=UI.Stack(new Border{Height=22},control);field.Margin=new(0,0,6,0);return field;}
    private Button GearCommandMenu(bool build)
    {
        var menu=new ContextMenu();
        void Item(string title,Action action,string help,Func<bool>? enabled=null,Func<bool>? visible=null)
        {
            var item=new MenuItem{Header=title,ToolTip=help};ToolTipService.SetShowOnDisabled(item,true);item.Click+=(_,_)=>action();menu.Items.Add(item);
            void State(){item.IsEnabled=enabled?.Invoke()??true;item.Visibility=visible==null||visible()?Visibility.Visible:Visibility.Collapsed;}
            menu.Opened+=(_,_)=>State();State();
        }
        if(build)
        {
            Item("New build from current",()=>NewGearBuild(false),"Copy Current into a separate named build.");Item("New empty build",()=>NewGearBuild(true),"Start a separate build with known-empty slots.");
            Item("Duplicate",DuplicateGearBuild,"Copy the selected build, including ranks and effects.");Item("Rename",RenameGearBuild,"Rename a saved build. Current keeps its name.",()=>SelectedGearBuild!=null);
            Item("Remove build",RemoveGearBuild,"Remove a saved build. Current cannot be removed.",()=>SelectedGearBuild!=null);
            Item("Use as current build",ConfirmGearDraft,"Choose whether to save the previous Current before replacing it.",visible:()=>SelectedGearBuild!=null&&gearTheory==null);
            Item("Save theorycraft build…",()=>SaveGearTheorycraft(),"Explicitly save this isolated workspace.",()=>gearTheory?.Dirty==true,()=>gearTheory!=null);
        }
        else
        {
            Item("Stat profiles…",OpenGearStats,"Review observed stats, AAs and advanced comparisons.");Item("Edit role weights…",EditWeights,"Edit the selected role's saved stat preferences.");Item("Manual equipment correction…",EditEquipment,"Correct the local Current baseline.");
            Item("View sources",ShowGearSources,"Select a candidate to inspect its acquisition sources.",()=>gearTable.SelectedItem is GearRow);
            Item("Break-even rank",CalculateBreakEven,"Select a candidate to estimate its first positive comparison through +10.",()=>gearTable.SelectedItem is GearRow);
            Item("Reset Gear panels",ResetGearPanels,"Restore equipment/candidates/details to 33% / 42% / 25%.");
        }
        var button=OverlayMenus.Button(menu,build?"Create and manage saved builds.":"Stat profiles, role preferences, item tools and panel layout.",build?"Build ▾":"Tools ▾");button.Width=double.NaN;button.Padding=new(9,3,9,3);return button;
    }
}
