using System.Windows;
using System.Windows.Controls;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class EncounterTimingControls : StackPanel
{
    internal ComboBox Cutoff { get; }
    internal StackPanel Custom { get; }
    internal TextBlock Error { get; }=UI.Text("",11,"Melee");
    private readonly TextBox idle,resume;
    internal EncounterTimingControls(TextBox idle,TextBox resume,int current)
    {
        this.idle=idle;this.resume=resume;
        string selected=EncounterTimingSettings.Presets.Contains(current)?$"{current} seconds":"Custom";
        Cutoff=UI.Select(EncounterTimingSettings.Presets.Select(s=>$"{s} seconds").Append("Custom"),selected,136);Cutoff.Height=28;Cutoff.Margin=new(0);Cutoff.Tag="idle-cutoff-selector";
        Cutoff.ToolTip="Time without relevant combat before a fight becomes idle. The closing idle time is excluded from DPS. Custom accepts 2–120 whole seconds.";
        idle.Width=76;idle.Height=28;idle.Margin=new(0);idle.ToolTip="Custom idle cutoff in whole seconds, from 2 to 120. Saved only with Save changes.";
        Custom=UI.Row(idle,Unit());Custom.Margin=new(8,0,0,0);Custom.Visibility=selected=="Custom"?Visibility.Visible:Visibility.Collapsed;
        var row=UI.Row(Cutoff,Custom);
        resume.Width=76;resume.Height=28;resume.Margin=new(0);resume.ToolTip="Resume the same unresolved targets within this interval. Includes interruptions in elapsed time. 0 disables resumption; otherwise use at least the idle cutoff, up to 300 seconds.";
        var fields=new WrapPanel();fields.Children.Add(Field("Idle cutoff",row,Cutoff.ToolTip.ToString()!));fields.Children.Add(Field("Resume",UI.Row(resume,Unit()),resume.ToolTip.ToString()!));Children.Add(fields);
        Error.Visibility=Visibility.Collapsed;Children.Add(Error);
        Cutoff.SelectionChanged+=(_,_)=>{Custom.Visibility=Cutoff.SelectedItem?.ToString()=="Custom"?Visibility.Visible:Visibility.Collapsed;ClearError();};
        idle.TextChanged+=(_,_)=>ClearError();resume.TextChanged+=(_,_)=>ClearError();
        System.Windows.Automation.AutomationProperties.SetName(Cutoff,"Idle cutoff");System.Windows.Automation.AutomationProperties.SetName(idle,"Custom idle cutoff in seconds");System.Windows.Automation.AutomationProperties.SetName(resume,"Resume in seconds");
    }
    private static TextBlock Unit(){var text=UI.Text("seconds",11,"SecondaryText");text.Margin=new(6,0,0,0);text.VerticalAlignment=VerticalAlignment.Center;return text;}
    private static UIElement Field(string title,FrameworkElement control,string help)
    {
        var label=UI.Text(title,11,"SecondaryText",true);label.TextWrapping=TextWrapping.NoWrap;label.ToolTip=help;label.Margin=new(0);
        var grid=new Grid{Margin=new(0,0,16,6),VerticalAlignment=VerticalAlignment.Top};grid.RowDefinitions.Add(new(){Height=new(19)});grid.RowDefinitions.Add(new(){Height=new(28)});
        Grid.SetRow(control,1);grid.Children.Add(label);grid.Children.Add(control);return grid;
    }
    internal string IdleText=>Cutoff.SelectedItem?.ToString()=="Custom"?idle.Text:Cutoff.SelectedItem?.ToString()?.Split(' ')[0]??"";
    internal bool TryRead(out int seconds,out int resumeSeconds)
    {bool valid=EncounterTimingSettings.TryRead(IdleText,resume.Text,out seconds,out resumeSeconds,out string message);Error.Text=message;Error.Visibility=valid?Visibility.Collapsed:Visibility.Visible;return valid;}
    private void ClearError(){Error.Text="";Error.Visibility=Visibility.Collapsed;}
}
