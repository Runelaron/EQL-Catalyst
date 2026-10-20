using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqlTracker.Core;

namespace EqlTracker.App;

internal sealed class ShortcutSettings : StackPanel
{
    internal static bool IsRecording { get;private set; }
    private static DateTime suppressUntil;
    internal static bool SuppressGlobal=>IsRecording||DateTime.UtcNow<suppressUntil;
    internal Dictionary<OverlayShortcutAction,GlobalShortcut> Draft { get; }
    private readonly TextBlock message=UI.Text("",11,"Melee");
    internal ShortcutSettings(CharacterProfile profile)
    {
        Draft=OverlayShortcuts.Effective(profile);
        Children.Add(UI.Text("Choose Change, then press a shortcut. Save changes applies these keys. Escape cancels recording; Reset restores that action’s default.",11,"SecondaryText"));
        foreach(var action in Enum.GetValues<OverlayShortcutAction>())
        {
            var title=action switch{OverlayShortcutAction.Objectives=>"Toggle objectives",OverlayShortcutAction.Dps=>"Toggle DPS",_=>"Toggle click-through"};
            var input=new TextBox{Text=Draft[action].Label,IsReadOnly=true,Width=170,Height=28,Margin=new(0,0,6,0),ToolTip="Current draft shortcut for "+title.ToLowerInvariant()+". Choose Change to record another combination."};
            System.Windows.Automation.AutomationProperties.SetName(input,title+" shortcut");
            bool recording=false;
            void Cancel(){if(!recording)return;recording=IsRecording=false;suppressUntil=DateTime.UtcNow.AddMilliseconds(250);input.Text=Draft[action].Label;}
            var change=UI.Button("Change…",()=>{input.Focus();recording=IsRecording=true;input.Text="Press shortcut…";message.Text="";});change.Height=28;change.Margin=new(0,0,6,0);change.ToolTip="Record a new shortcut for "+title.ToLowerInvariant()+".";
            var reset=UI.Button("Reset",()=>{Cancel();Draft[action]=OverlayShortcuts.Default(action);input.Text=Draft[action].Label;message.Text="Default selected. Choose Save changes to apply.";});reset.Height=28;reset.Margin=new(0);reset.ToolTip="Restore "+OverlayShortcuts.Default(action).Label+" after Save changes.";
            input.PreviewKeyDown+=(_,e)=>
            {
                if(!recording)return;
                var key=e.Key==Key.System?e.SystemKey:e.Key;
                if(key==Key.Escape){Cancel();e.Handled=true;return;}
                if(key==Key.Tab){Cancel();return;}
                e.Handled=true;
                if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
                var value=new GlobalShortcut((ShortcutModifiers)(int)Keyboard.Modifiers,KeyInterop.VirtualKeyFromKey(key));
                // WPF: Alt=1, Control=2, Shift=4, Windows=8, matching RegisterHotKey.
                if(!OverlayShortcuts.Valid(value)){message.Text="Use a modified letter/number or navigation key, or a function key except F12.";return;}
                var next=new Dictionary<OverlayShortcutAction,GlobalShortcut>(Draft){[action]=value};
                if(!OverlayShortcuts.Validate(next,out string error)){message.Text=error;return;}
                Draft[action]=value;Cancel();message.Text="Shortcut selected. Choose Save changes to apply.";
            };
            input.LostKeyboardFocus+=(_,_)=>Cancel();Unloaded+=(_,_)=>Cancel();
            var row=new WrapPanel();row.Children.Add(input);row.Children.Add(change);row.Children.Add(reset);
            Children.Add(UI.Stack(UI.Text(title,11,"SecondaryText",true),row));
        }
        Children.Add(message);
    }
    internal void Failed(string error)=>message.Text=error;
}

public sealed partial class SettingsWindow
{
    private ShortcutSettings shortcutSettings=null!;
}
