using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqlTracker.Core;
namespace EqlTracker.App;
public sealed partial class QuestWindow
{
    private Dictionary<string,GearPlacementResult>? gearDragTargets;
    private void ConfigureGearDrag()
    {
        gearEquipment.RowStyle=GearDragRows.Style(gearEquipment.RowStyle);
        void Row(DataGridRow row){GearDragRows.State(row,gearDragTargets==null?0:row.Item is EquipmentPosition p&&gearDragTargets.GetValueOrDefault(p.Id)?.Target.Allowed==true?2:1);}
        void Rows(){foreach(var row in Usability.Tree(gearEquipment).OfType<DataGridRow>())Row(row);}
        gearEquipment.LoadingRow+=(_,e)=>Row(e.Row);gearEquipment.UnloadingRow+=(_,e)=>GearDragRows.State(e.Row,0);
        Point? start=null;GearRow? origin=null;
        gearTable.PreviewMouseLeftButtonDown+=(_,e)=>{origin=Ancestor<DataGridRow>(e.OriginalSource)?.Item as GearRow;start=origin==null?null:e.GetPosition(gearTable);};
        gearTable.PreviewMouseLeftButtonUp+=(_,_)=>{start=null;origin=null;};
        gearTable.PreviewMouseMove+=(_,e)=>{
            if(start is not {} p||origin is not {} row||e.LeftButton!=MouseButtonState.Pressed||(e.GetPosition(gearTable)-p).Length<8)return;
            start=null;row=row with{Rank=GearInspectedRank(row),Instance=GearInspectedInstance(row)};
            using(var measure=UpdateDiagnostics.Measure("Gear drag eligibility"))gearDragTargets=PrepareGearPlacement(row).ToDictionary(r=>r.Target.Id);
            Rows();long revision=Controller.EquipmentRevision,workspace=gearWorkspaceRevision;string build=gearWorkspaceId;
            try{ItemDragFeedback.Run(gearTable,new DataObject("EQL.GearCandidate",row),DragDropEffects.Copy,row.Item,row.Rank,()=>SelectedTab==TrackerTab.Gear&&gearWorkspaceRevision==workspace&&Controller.EquipmentRevision==revision&&gearWorkspaceId==build,()=>{gearDragTargets=null;Rows();});}
            finally{gearDragTargets=null;Rows();}e.Handled=true;
        };
        gearEquipment.AllowDrop=true;
        GearPlacementResult? Target(DragEventArgs e)=>Ancestor<DataGridRow>(e.OriginalSource)?.Item is EquipmentPosition p?gearDragTargets?.GetValueOrDefault(p.Id):null;
        string lastTarget="";
        gearEquipment.DragOver+=(_,e)=>{
            var target=Target(e);bool valid=e.Data.GetDataPresent("EQL.GearCandidate")&&target?.Target.Allowed==true;
            e.Effects=valid?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
            string key=target?.Target.Id??"background";if(!valid&&key!=lastTarget)gearBreakEven.Text=target?.Target.Reason??"Drop onto a compatible equipment row.";lastTarget=key;
            if(e.Data.GetData("EQL.GearCandidate") is GearRow row)ItemDragFeedback.Destination(row.Item,valid?visibleGearPositions.FirstOrDefault(p=>p.Id==target!.Target.Id)?.Slot:null);
        };
        gearEquipment.Drop+=(_,e)=>{
            var target=Target(e);
            if(e.Data.GetData("EQL.GearCandidate") is GearRow row&&target?.Target.Allowed==true)TryGearDraft(row,target.Target.Id);
            else gearBreakEven.Text=target?.Target.Reason??"Drop onto a compatible equipment row.";
            e.Effects=target?.Target.Allowed==true?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
        };
        gearEquipment.DragLeave+=(_,e)=>{if(e.Data.GetData("EQL.GearCandidate") is GearRow row)ItemDragFeedback.Destination(row.Item,null);};
    }
}
