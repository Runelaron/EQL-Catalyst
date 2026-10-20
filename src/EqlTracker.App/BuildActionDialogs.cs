using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;
namespace EqlTracker.App;

internal sealed class CurrentBuildBackupDialog : FrostDialog
{
    private readonly TextBox name=UI.Input();
    internal CurrentBackupChoice Choice{get;private set;}=CurrentBackupChoice.Cancel;
    internal string BackupName=>name.Text.Trim();
    internal CurrentBuildBackupDialog(string incoming,string initialName="",bool refreshed=false):base("Save current build being overwritten?",520,230)
    {
        MinHeight=220;var content=UI.Stack(UI.Text("Before using “"+incoming+"” as Current.",14,"Accent",true));
        if(refreshed)content.Children.Add(UI.Text("Current changed while the prompt was open. Review this choice again.",11,"Melee"));
        name.Text=initialName;name.ToolTip="Name a separate backup of Current, including its ranks and effects. Undo remains available if you choose No.";
        content.Children.Add(UI.Field("Save Current as",name));content.VerticalAlignment=VerticalAlignment.Top;Editor.Children.Add(content);
        var save=UI.Button("Save & use build",()=>{Choice=CurrentBackupChoice.Save;DialogResult=true;});save.IsEnabled=BackupName.Length>0;name.TextChanged+=(_,_)=>save.IsEnabled=BackupName.Length>0;
        Actions.Children.Add(save);Actions.Children.Add(UI.Button("No, replace Current",()=>{Choice=CurrentBackupChoice.Replace;DialogResult=true;}));Actions.Children.Add(UI.Button("Cancel",Close));
        Loaded+=(_,_)=>name.Focus();
    }
}
internal sealed class GearSlotPickerDialog : FrostDialog
{
    private readonly ListBox choices=new(){DisplayMemberPath="Label",MinHeight=100};
    internal string? PositionId=>(choices.SelectedItem as GearPlacementTarget)?.Id;
    internal GearSlotPickerDialog(string name,IEnumerable<GearPlacementTarget> targets):base("Choose replacement slot",500,330)
    {
        choices.MaxHeight=200;choices.ItemsSource=targets.ToArray();choices.ToolTip="Compatible destinations with the item currently assigned to each slot.";Editor.Children.Add(UI.Stack(UI.Text(name,15,"Accent",true),UI.Text("Choose which equipped item to replace.",12),choices));
        var add=UI.Button("Add to build",()=>DialogResult=true);add.IsEnabled=false;choices.SelectionChanged+=(_,_)=>add.IsEnabled=PositionId!=null;Actions.Children.Add(add);Actions.Children.Add(UI.Button("Cancel",Close));
    }
}
