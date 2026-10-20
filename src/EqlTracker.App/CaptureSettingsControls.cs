using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqlTracker.Core;
namespace EqlTracker.App;

internal sealed class CaptureSettingsControls : StackPanel
{
    private readonly TrackerController controller;
    private readonly CheckBox periodic=new(){Content="Periodic capture"},login=new(){Content="Capture after login"};
    private readonly ComboBox interval=UI.Select(["5 minutes","15 minutes","30 minutes","1 hour","2 hours","4 hours","Custom…"],"2 hours",130);
    private readonly TextBox custom=UI.Input("120",70);
    private readonly FrameworkElement customField;
    private readonly TextBlock status=UI.Text("",11,"ReadableNote"),error=UI.Text("",11,"Melee");
    private bool updating;
    private string signature="";
    internal CaptureSettingsControls(TrackerController controller)
    {
        this.controller=controller;custom.Name="CaptureCustomMinutes";customField=(FrameworkElement)UI.Field("Minutes · 1–1440",custom);
        var fields=new WrapPanel();foreach(var element in new UIElement[]{UI.ActionField(periodic),UI.ActionField(login),UI.Field("Capture every",interval),customField})fields.Children.Add(element);
        Children.Add(fields);Children.Add(status);Children.Add(error);
        periodic.ToolTip="Request a fresh capture on this schedule when the verified Sync shortcut and existing game-idle conditions allow it. This is independent of Gear Auto Update.";
        login.ToolTip="Request one capture after verified login, once the game is foreground, idle and out of combat.";
        interval.ToolTip="Time between successful captures. Due requests coalesce and wait for eligibility; the default is 2 hours.";
        custom.ToolTip="Enter whole minutes from 1 to 1440. Enter or leaving this field applies a valid value; Escape restores the saved interval.";
        periodic.Click+=(_,_)=>{controller.Profile.NativeSync.Periodic=periodic.IsChecked==true;Save();};login.Click+=(_,_)=>{controller.Profile.NativeSync.Login=login.IsChecked==true;Save();};
        interval.SelectionChanged+=(_,_)=>{if(updating)return;customField.Visibility=interval.SelectedIndex==6?Visibility.Visible:Visibility.Collapsed;if(interval.SelectedIndex<6){NativeSyncPolicy.SetMinutes(controller.Profile.NativeSync,new[]{5,15,30,60,120,240}[interval.SelectedIndex]);Save();}else custom.Focus();};
        custom.LostKeyboardFocus+=(_,_)=>Commit();custom.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}else if(e.Key==Key.Escape){error.Text="";signature="";Refresh(true);e.Handled=true;}};
        Refresh(true);
    }
    private void Commit()
    {
        if(updating||interval.SelectedIndex!=6)return;
        if(!int.TryParse(custom.Text,NumberStyles.None,CultureInfo.InvariantCulture,out int value)||!NativeSyncPolicy.SetMinutes(controller.Profile.NativeSync,value)){error.Text="Use whole minutes from 1 to 1440; the previous interval is retained.";return;}
        Save();
    }
    private void Save(){error.Text="";signature="";controller.Save();Refresh(true);}
    internal void Refresh(bool force=false)
    {
        var settings=controller.Profile.NativeSync;string key=$"{settings.Login}|{settings.Periodic}|{NativeSyncPolicy.Minutes(settings)}|{settings.Verified}|{settings.Key}|{settings.LastSuccess}";
        if(!force&&(key==signature||custom.IsKeyboardFocusWithin))return;signature=key;updating=true;
        periodic.IsChecked=settings.Periodic;login.IsChecked=settings.Login;int minutes=NativeSyncPolicy.Minutes(settings);int found=Array.IndexOf(new[]{5,15,30,60,120,240},minutes);interval.SelectedIndex=found<0?6:found;custom.Text=minutes.ToString(CultureInfo.InvariantCulture);customField.Visibility=found<0?Visibility.Visible:Visibility.Collapsed;
        status.Text="Scheduled capture: "+NativeSyncPolicy.Schedule(settings)+"\n"+(settings.LastSuccess is {} last?"Last verified capture: "+last.ToString("MMM dd HH:mm"):"No verified capture yet.")+(settings.Periodic&&NativeSyncPolicy.NextPeriodic(settings) is {} due?" · due "+due.ToString("MMM dd HH:mm")+" or next eligible idle":"")+"\nComplete capture files import within about 3 seconds. Auto Update controls whether they replace Current.";
        updating=false;
    }
}
